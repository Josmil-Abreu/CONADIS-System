using CONADIS.Web.Infraestructura;
using CONADIS.Web.Models.ViewModels;
using CONADIS.Web.Negocio.Datos;
using CONADIS.Web.Negocio.Modelos;
using CONADIS.Web.Negocio.Reportes;
using CONADIS.Web.Negocio.Seguridad;
using CONADIS.Web.Negocio.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Web.Controllers;

/// <summary>Mi carnet digital: ver, descargar y pedir duplicado.</summary>
[Authorize]
public class CarnetController(ServicioPortal portal, ServicioReportesPortal reportes, BandejaSalida bandeja) : ControladorPortal
{
    // GET /Carnet  → "Mi carnet digital"
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var vm = await ObtenerCarnetAsync(ct);
        if (vm is null)
        {
            TempData["Aviso"] = "Tu carnet aún no ha sido emitido. Se emite cuando el comité apruebe tu solicitud.";
            return RedirectToAction("Index", "Tramites");
        }
        return View(vm);
    }

    // GET /Carnet/DescargarPdf  → carnet digital (RDLC) con el código y el enlace de verificación
    [HttpGet]
    public async Task<IActionResult> DescargarPdf(CancellationToken ct)
    {
        var c = await portal.MiCarnetAsync(Cuenta, Ip, ct);
        if (c is null) return NotFound();
        var url = Url.Action("Index", "Verificacion", new { codigo = c.CodigoVerificacion }, Request.Scheme) ?? "";
        return Archivo(await reportes.CarnetAsync(Cuenta, c, url, FormatoReporte.Pdf, Ip, ct));
    }

    // GET /Carnet/DescargarCertificado  → el mismo documento (incluye tipo, grado y n.º de certificado)
    [HttpGet]
    public Task<IActionResult> DescargarCertificado(CancellationToken ct) => DescargarPdf(ct);

    // GET /Carnet/SolicitarDuplicado
    [HttpGet]
    public IActionResult SolicitarDuplicado() => View(new SolicitarDuplicadoVM());

    // POST /Carnet/SolicitarDuplicado  → registra la solicitud de duplicado; el Core crea la orden de cobro
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SolicitarDuplicado(SolicitarDuplicadoVM vm, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(vm);

        var carnet = await portal.MiCarnetAsync(Cuenta, Ip, ct);
        if (carnet is null)
        {
            TempData["Aviso"] = "No tienes un carnet emitido del cual pedir duplicado.";
            return RedirectToAction("Index", "Tramites");
        }

        var (nombres, apellidos) = Separar(carnet.NombreCompleto);
        try
        {
            var solicitud = new SolicitudPortal
            {
                Tipo = "Duplicado",
                Cedula = Cuenta.Cedula,
                Nombres = nombres,
                Apellidos = apellidos,
                Correo = Cuenta.Correo,
                MotivoDuplicado = vm.Motivo,
                ConsentimientoDatos = true
            };
            var clave = ClaveIdempotencia("duplicado", carnet.NumeroCarnet);
            var envio = await bandeja.EnviarOEncolarAsync(Cuenta, portal.PeticionSolicitud(Cuenta, solicitud, clave),
                () => portal.EnviarSolicitudAsync(Cuenta, solicitud, clave, Ip, ct), Ip, ct);
            if (envio.EnCola)
            {
                TempData["Aviso"] = $"El sistema central no está disponible. Tu solicitud de duplicado quedó guardada (envío N.º {envio.EnvioId}); " +
                                    "cuando se envíe, la orden de pago aparecerá en Mis trámites.";
                return RedirectToAction("Index", "Tramites");
            }
            var r = envio.Valor!;

            await reportes.RegistrarOperacionAsync(Cuenta, "DuplicadoSolicitado", $"Trámite {r.NumeroTramite} · {vm.Motivo}", Ip, ct);
            if (r.NumeroOrden is null)
            {
                TempData["Mensaje"] = $"Recibimos tu solicitud de duplicado (trámite {r.NumeroTramite}).";
                return RedirectToAction("Detalle", "Tramites", new { id = r.NumeroTramite });
            }
            return RedirectToAction("Index", "Pagos", new { id = r.NumeroOrden });
        }
        catch (ReglaNegocioException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(vm);
        }
    }

    // ---------------------------------------------------------------

    private async Task<MiCarnetVM?> ObtenerCarnetAsync(CancellationToken ct)
    {
        var c = await portal.MiCarnetAsync(Cuenta, Ip, ct);
        if (c is null) return null;

        return new MiCarnetVM
        {
            NombreCompleto = c.NombreCompleto,
            CedulaEnmascarada = c.Cedula.Length == 11 ? $"***-***{c.Cedula[6..10]}-{c.Cedula[10]}" : "***",
            NumeroCarnet = c.NumeroCarnet,
            FechaEmision = c.FechaEmision,
            FechaVencimiento = c.FechaVencimiento,
            Estado = c.Estado,
            TipoDiscapacidad = c.TipoDiscapacidad,
            Grado = c.GradoDiscapacidad,
            NumeroCertificado = c.NumeroCertificado,
            CodigoVerificacion = c.CodigoVerificacion,
            UrlVerificacion = Url.Action("Index", "Verificacion", new { codigo = c.CodigoVerificacion }, Request.Scheme) ?? ""
        };
    }

    /// <summary>"María Pérez Gómez" → ("María", "Pérez Gómez"). Con 4+ palabras, dos nombres y el resto apellidos.</summary>
    internal static (string Nombres, string Apellidos) Separar(string nombreCompleto)
    {
        var partes = nombreCompleto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var n = partes.Length >= 4 ? 2 : 1;
        return partes.Length <= 1
            ? (nombreCompleto.Trim(), "-")
            : (string.Join(' ', partes[..n]), string.Join(' ', partes[n..]));
    }
}
