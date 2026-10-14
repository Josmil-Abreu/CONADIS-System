using CONADIS.Web.Infraestructura;
using CONADIS.Web.Models.ViewModels;
using CONADIS.Web.Negocio.Datos;
using CONADIS.Web.Negocio.Modelos;
using CONADIS.Web.Negocio.Reportes;
using CONADIS.Web.Negocio.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Web.Controllers;

/// <summary>
/// Pago en línea de tasas (duplicado de carnet, certificación de accesibilidad...).
/// La orden viene del Core y el cobro lo hace Integración con la pasarela (simulada en la demo).
/// </summary>
[Authorize]
public class PagosController(ServicioPortal portal, ServicioReportesPortal reportes) : ControladorPortal
{
    /// <summary>
    /// Token que devolvería la pasarela al tokenizar la tarjeta en el navegador. El simulador de Integración
    /// aprueba cualquier token salvo "tok_rechazada" (fondos insuficientes) y "tok_error" (pasarela caída).
    /// </summary>
    private const string TokenDemo = "tok_demo_visa";

    // GET /Pagos/Index/ORD-2026-000123  -> resumen de la orden
    [HttpGet]
    public async Task<IActionResult> Index(string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return RedirectToAction("Index", "Tramites");

        var orden = await ObtenerOrdenAsync(id, ct);
        if (orden is null) return NotFound();
        if (orden.Estado == "Pagada") return View(nameof(Retorno), Vista(orden, "Pagada"));

        // Clave de idempotencia de este intento: un doble clic en "Pagar" no cobra dos veces,
        // pero volver a esta página después de un rechazo permite intentar de nuevo.
        HttpContext.Session.SetString(ClaveSesion(id), Guid.NewGuid().ToString("N"));
        return View(Vista(orden, orden.Estado));
    }

    // POST /Pagos/Pagar  -> cobra con la pasarela a través de Integración
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pagar(string id, CancellationToken ct)
    {
        var orden = await ObtenerOrdenAsync(id, ct);
        if (orden is null) return NotFound();

        var intento = HttpContext.Session.GetString(ClaveSesion(id)) ?? Guid.NewGuid().ToString("N");
        try
        {
            var pago = await portal.PagarAsync(Cuenta, id, TokenDemo, $"pago-{id}-{intento}", Ip, ct);
            await reportes.RegistrarOperacionAsync(Cuenta, "PagoEnLinea",
                $"Orden {id} · RD$ {orden.Total:N2} · {pago.Estado} · e-NCF {pago.NumeroComprobante} · autorización {pago.CodigoAutorizacion}", Ip, ct);
            var vm = Vista(orden, pago.Estado == "Rechazado" ? "Fallida" : "Pagada");
            vm.Referencia = pago.NumeroComprobante;
            return View(nameof(Retorno), vm);
        }
        catch (ReglaNegocioException ex)
        {
            TempData["Error"] = ex.Message;   // p. ej. "Fondos insuficientes."
            return View(nameof(Retorno), Vista(orden, "Fallida"));
        }
    }

    // GET /Pagos/Retorno/ORD-2026-000123  -> estado actual de la orden (nunca se confía en parámetros de la URL)
    [HttpGet]
    public async Task<IActionResult> Retorno(string id, CancellationToken ct)
    {
        var orden = await ObtenerOrdenAsync(id, ct);
        return orden is null ? NotFound() : View(Vista(orden, orden.Estado == "Pagada" ? "Pagada" : "Fallida"));
    }

    // GET /Pagos/Comprobante/ORD-2026-000123?formato=Pdf|Excel  → comprobante del pago (RDLC)
    [HttpGet]
    public async Task<IActionResult> Comprobante(string id, FormatoReporte formato, CancellationToken ct)
    {
        var orden = await ObtenerOrdenAsync(id, ct);
        if (orden is null) return NotFound();
        return Archivo(await reportes.ComprobantePagoAsync(Cuenta, orden, null, formato, Ip, ct));
    }

    // POST /Pagos/Ventanilla  → "Pagar después en ventanilla"
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Ventanilla(string id)
    {
        // La orden ya existe en el Core: en la Caja se busca por este número y se cobra el monto exacto.
        TempData["Mensaje"] = $"Presenta la orden {id} en cualquier caja del CONADIS para pagar.";
        return RedirectToAction("Index", "Tramites");
    }

    private async Task<OrdenCobro?> ObtenerOrdenAsync(string id, CancellationToken ct)
    {
        var orden = await portal.OrdenAsync(Cuenta, id, ct);
        // Solo el titular de la orden puede verla o pagarla.
        return orden is not null && orden.Documento == Cuenta.Cedula ? orden : null;
    }

    private static OrdenPagoVM Vista(OrdenCobro o, string estado) => new()
    {
        NumeroOrden = o.NumeroOrden,
        Concepto = o.Concepto,
        Monto = o.Total,
        Referencia = o.NumeroComprobante ?? o.Referencia,
        Estado = estado
    };

    private static string ClaveSesion(string orden) => $"Pago.Intento.{orden}";
}
