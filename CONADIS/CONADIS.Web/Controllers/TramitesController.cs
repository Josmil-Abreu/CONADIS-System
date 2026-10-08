using CONADIS.Application.Interfaces;
using CONADIS.Web.Extensions;
using CONADIS.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Web.Controllers;

/// <summary>Panel "Mis trámites", detalle de un trámite y consulta sin cuenta.</summary>
[Authorize]
public class TramitesController : Controller
{
    private readonly ISolicitudExpedienteService _solicitudes;

    public TramitesController(ISolicitudExpedienteService solicitudes)
    {
        _solicitudes = solicitudes;
    }

    // GET /Tramites?estado=EnEvaluacion  → panel "Mis trámites"
    [HttpGet]
    public IActionResult Index(string? estado)
    {
        // TODO (capa Application): ISolicitudExpedienteService.ListarPorCedulaAsync(User.Cedula())
        // Hoy el servicio solo busca por número, así que el listado se arma vacío.
        var vm = new MisTramitesVM
        {
            FiltroEstado = estado,
            EstadoCarnet = "Aún no emitido"
        };

        if (!string.IsNullOrEmpty(estado))
            vm.Tramites = vm.Tramites.Where(t => t.Estado == estado).ToList();

        return View(vm);
    }

    // GET /Tramites/Detalle/EXP-2026-12345
    [HttpGet]
    public async Task<IActionResult> Detalle(string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return RedirectToAction(nameof(Index));

        var r = await _solicitudes.ObtenerPorNumeroAsync(id, ct);
        if (r.IsFailure) return NotFound();

        // TODO: comprobar que el expediente pertenece a User.Cedula() (o a un representado).
        return View(new DetalleTramiteVM { Expediente = r.Value });
    }

    // GET /Tramites/Estado/EXP-2026-12345  → JSON para que la página "se actualice sola"
    [HttpGet]
    public async Task<IActionResult> Estado(string id, CancellationToken ct)
    {
        var r = await _solicitudes.ObtenerPorNumeroAsync(id, ct);
        if (r.IsFailure) return NotFound();

        return Json(new { estado = r.Value.Estado, carnet = r.Value.NumeroCarnet });
    }

    // GET /Tramites/Consultar  → consulta sin cuenta (pública)
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Consultar() => View(new ConsultarTramiteVM());

    // POST /Tramites/Consultar
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Consultar(ConsultarTramiteVM vm, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(vm);

        var r = await _solicitudes.ObtenerPorNumeroAsync(vm.NumeroTramite.Trim(), ct);

        // TODO: comparar también la cédula del expediente con vm.Cedula
        // (el DTO actual de ObtenerPorNumeroAsync devuelve la cédula vacía).
        if (r.IsFailure)
        {
            // Mensaje genérico: no revelar si el número existe o no.
            ModelState.AddModelError(string.Empty, "No encontramos un trámite con esos datos.");
            return View(vm);
        }

        vm.Resultado = r.Value;
        return View(vm);
    }

    // GET /Tramites/CambiarCita/EXP-2026-12345
    [HttpGet]
    public IActionResult CambiarCita(string id) => View(new CambiarCitaVM { NumeroTramite = id });

    // POST /Tramites/CambiarCita
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CambiarCita(CambiarCitaVM vm)
    {
        if (vm.NuevaFecha is not null && vm.NuevaFecha <= DateTime.Now)
            ModelState.AddModelError(nameof(vm.NuevaFecha), "Elige una fecha futura.");
        if (!ModelState.IsValid) return View(vm);

        // TODO: ICitaService.ReprogramarAsync(vm.NumeroTramite, vm.NuevaFecha)
        TempData["Mensaje"] = "Tu cita fue reprogramada.";
        return RedirectToAction(nameof(Detalle), new { id = vm.NumeroTramite });
    }

    // POST /Tramites/AgregarDocumento  → "Agregar otro documento" / "Subir documento solicitado"
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public IActionResult AgregarDocumento(string id, IFormFile? archivo)
    {
        if (archivo is null || archivo.Length == 0)
        {
            TempData["Error"] = "Elige un archivo.";
            return RedirectToAction(nameof(Detalle), new { id });
        }

        // TODO: IDocumentoService.AdjuntarAsync(id, archivo.OpenReadStream(), archivo.FileName)
        TempData["Mensaje"] = $"Recibimos {archivo.FileName}.";
        return RedirectToAction(nameof(Detalle), new { id });
    }

    // GET /Tramites/DescargarAcuse/EXP-2026-12345
    [HttpGet]
    public async Task<IActionResult> DescargarAcuse(string id, CancellationToken ct)
    {
        var r = await _solicitudes.ObtenerPorNumeroAsync(id, ct);
        if (r.IsFailure) return NotFound();

        // TODO: IDocumentoPdfService.GenerarAcuseAsync(r.Value) para un PDF accesible real.
        var texto = $"ACUSE DE RECIBO\nTrámite: {r.Value.NumeroExpediente}\n" +
                    $"Fecha: {r.Value.FechaSolicitud:dd/MM/yyyy HH:mm}\nEstado: {r.Value.Estado}";
        return File(System.Text.Encoding.UTF8.GetBytes(texto), "text/plain", $"acuse-{id}.txt");
    }

    // GET /Tramites/Apelar/EXP-2026-12345
    [HttpGet]
    public async Task<IActionResult> Apelar(string id, CancellationToken ct)
    {
        var r = await _solicitudes.ObtenerPorNumeroAsync(id, ct);
        if (r.IsFailure) return NotFound();
        if (r.Value.Estado != "Rechazado") return RedirectToAction(nameof(Detalle), new { id });

        return View(new ApelacionVM { NumeroTramite = id });
    }

    // POST /Tramites/Apelar
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Apelar(ApelacionVM vm)
    {
        if (!ModelState.IsValid) return View(vm);

        // TODO: IApelacionService.ApelarAsync(...) → llama a Expediente.Apelar() del dominio
        // y valida el plazo de 30 días.
        TempData["Mensaje"] = "Recibimos tu apelación.";
        return RedirectToAction(nameof(Detalle), new { id = vm.NumeroTramite });
    }
}
