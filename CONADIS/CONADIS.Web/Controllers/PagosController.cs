using CONADIS.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Web.Controllers;

/// <summary>
/// Pago de tasas (duplicado de carnet, certificación de accesibilidad...).
/// Separado para que solo este controlador cambie al conectar la pasarela real.
/// </summary>
[Authorize]
public class PagosController : Controller
{
    // TODO (capa Application): IPagoService con ObtenerOrdenAsync, IniciarPagoAsync,
    // ConfirmarPagoAsync y GenerarVolanteVentanillaAsync. Reemplazar ObtenerOrdenSimulada().

    // GET /Pagos/Index/ORD-20261008133000  -> resumen de la orden
    [HttpGet]
    public IActionResult Index(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return RedirectToAction("Index", "Tramites");
        return View(ObtenerOrdenSimulada(id));
    }

    // POST /Pagos/Pagar  -> manda a la pasarela de pago
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Pagar(string id)
    {
        // TODO: var urlPasarela = await _pagos.IniciarPagoAsync(id, urlRetorno);
        //       return Redirect(urlPasarela);
        // SIMULADO: se va directo al retorno como si el pago fuera aprobado.
        return RedirectToAction(nameof(Retorno), new { id, estado = "aprobado" });
    }

    // GET /Pagos/Retorno?id=...&estado=aprobado  -> la pasarela vuelve aquí
    [HttpGet]
    public IActionResult Retorno(string id, string estado)
    {
        // TODO: verificar la firma/estado del pago con la pasarela; nunca confiar solo en el querystring.
        var vm = ObtenerOrdenSimulada(id);
        vm.Estado = estado == "aprobado" ? "Pagada" : "Fallida";
        return View(vm);
    }

    // POST /Pagos/Ventanilla  → "Pagar después en ventanilla"
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Ventanilla(string id)
    {
        // TODO: generar el volante de pago con su referencia.
        TempData["Mensaje"] = $"Presenta la referencia {id} en cualquier sede del CONADIS para pagar.";
        return RedirectToAction(nameof(Index), new { id });
    }

    private static OrdenPagoVM ObtenerOrdenSimulada(string id) => new()
    {
        NumeroOrden = id,
        Concepto = "Duplicado de carnet",
        Monto = 500m,
        Estado = "Pendiente"
    };
}
