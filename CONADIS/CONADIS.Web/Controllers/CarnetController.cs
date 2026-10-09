using CONADIS.Application.Interfaces;
using CONADIS.Web.Extensions;
using CONADIS.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Web.Controllers;

/// <summary>Mi carnet digital: ver, descargar y pedir duplicado.</summary>
[Authorize]
public class CarnetController : Controller
{
    private readonly ISolicitudExpedienteService _solicitudes;

    public CarnetController(ISolicitudExpedienteService solicitudes)
    {
        _solicitudes = solicitudes;
    }

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

    // GET /Carnet/DescargarPdf
    [HttpGet]
    public async Task<IActionResult> DescargarPdf(CancellationToken ct)
    {
        var vm = await ObtenerCarnetAsync(ct);
        if (vm is null) return NotFound();

        // TODO: ICarnetPdfService.GenerarAsync(...) con el QR firmado (IFirmaDigitalService).
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            $"CARNET PCD\n{vm.NombreCompleto}\nN.º {vm.NumeroCarnet}\nVerificar: {vm.UrlVerificacion}");
        return File(bytes, "text/plain", $"carnet-{vm.NumeroCarnet}.txt");
    }

    // GET /Carnet/DescargarCertificado
    [HttpGet]
    public async Task<IActionResult> DescargarCertificado(CancellationToken ct)
    {
        var vm = await ObtenerCarnetAsync(ct);
        if (vm is null) return NotFound();

        // TODO: generar el PDF real del certificado de discapacidad.
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            $"CERTIFICADO DE DISCAPACIDAD\n{vm.NombreCompleto}\nTipo: {vm.TipoDiscapacidad}\nGrado: {vm.Grado}");
        return File(bytes, "text/plain", $"certificado-{vm.NumeroCertificado}.txt");
    }

    // GET /Carnet/SolicitarDuplicado
    [HttpGet]
    public IActionResult SolicitarDuplicado() => View(new SolicitarDuplicadoVM());

    // POST /Carnet/SolicitarDuplicado  → crea la orden de cobro y manda a Pagos
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SolicitarDuplicado(SolicitarDuplicadoVM vm)
    {
        if (!ModelState.IsValid) return View(vm);

        // TODO: IPagoService.CrearOrdenAsync("DUPLICADO_CARNET", User.Cedula(), vm.Motivo)
        var numeroOrden = $"ORD-{DateTime.Now:yyyyMMddHHmmss}";
        return RedirectToAction("Index", "Pagos", new { id = numeroOrden });
    }

    // ---------------------------------------------------------------

    private async Task<MiCarnetVM?> ObtenerCarnetAsync(CancellationToken ct)
    {
        // TODO (capa Application): ICarnetService.ObtenerVigentePorCedulaAsync(User.Cedula()).
        // Mientras tanto se usa el número de expediente guardado como claim (si existe).
        var numero = User.NumeroExpediente();
        if (string.IsNullOrEmpty(numero)) return null;

        var r = await _solicitudes.ObtenerPorNumeroAsync(numero, ct);
        if (r.IsFailure || string.IsNullOrEmpty(r.Value.NumeroCarnet)) return null;

        var e = r.Value;
        var cedula = User.Cedula() ?? e.Cedula;
        var codigo = e.NumeroCarnet!;

        return new MiCarnetVM
        {
            NombreCompleto = User.Identity?.Name ?? e.NombreCompleto,
            CedulaEnmascarada = cedula.Length == 11 ? $"***-***{cedula[6..10]}-{cedula[10]}" : "***",
            NumeroCarnet = e.NumeroCarnet!,
            Estado = e.Estado,
            TipoDiscapacidad = e.TipoDiscapacidad,
            Grado = e.GradoDiscapacidad,
            CodigoVerificacion = codigo,
            UrlVerificacion = Url.Action("Index", "Verificacion", new { codigo }, Request.Scheme) ?? ""
        };
    }
}
