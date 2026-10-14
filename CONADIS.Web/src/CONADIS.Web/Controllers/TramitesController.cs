using CONADIS.Web.Infraestructura;
using CONADIS.Web.Models.ViewModels;
using CONADIS.Web.Negocio.Datos;
using CONADIS.Web.Negocio.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Web.Controllers;

/// <summary>Panel "Mis trámites", detalle de un trámite y consulta sin cuenta (RF-WEB-02).</summary>
[Authorize]
public class TramitesController(ServicioPortal portal) : ControladorPortal
{
    // GET /Tramites?estado=EnEvaluacion  → panel "Mis trámites"
    [HttpGet]
    public async Task<IActionResult> Index(string? estado, CancellationToken ct)
    {
        var tramites = await portal.MisTramitesAsync(Cuenta, Ip, ct);
        var carnet = await portal.MiCarnetAsync(Cuenta, Ip, ct);
        var avisos = await portal.AvisosAsync(Cuenta, 5, ct);

        var vm = new MisTramitesVM
        {
            FiltroEstado = estado,
            Tramites = tramites
                .Where(t => string.IsNullOrEmpty(estado) || t.Estado == estado)
                .Select(t => new TramiteFilaVM
                {
                    Numero = t.Numero,
                    Tipo = t.Tipo,
                    Persona = t.Persona,
                    Estado = t.Estado,
                    Actualizado = t.Actualizado.LocalDateTime
                })
                .ToList(),
            EstadoCarnet = carnet?.Estado ?? "Aún no emitido",
            AccionesPendientes = tramites.Count(t => t.Estado == "Rechazado"),
            AvisosRecientes = avisos.Select(a => $"{a.Fecha.LocalDateTime:dd/MM} · {a.Asunto}").ToList()
        };

        // Próxima cita: la del trámite más reciente que esté en evaluación
        var enEvaluacion = tramites.Where(t => t.Estado is "Solicitado" or "EnEvaluacion").OrderByDescending(t => t.Actualizado).FirstOrDefault();
        if (enEvaluacion is not null && await portal.TramiteAsync(Cuenta, enEvaluacion.Numero, Ip, ct) is { FechaCita: { } cita } detalle
            && cita > DateTimeOffset.Now)
        {
            vm.ProximaCita = cita.LocalDateTime;
            vm.CentroProximaCita = detalle.CentroValoracion;
        }

        return View(vm);
    }

    // GET /Tramites/Detalle/TRA-2026-12345
    [HttpGet]
    public async Task<IActionResult> Detalle(string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return RedirectToAction(nameof(Index));

        // La consulta usa la cédula de la sesión: un trámite ajeno devuelve null (404).
        var t = await portal.TramiteAsync(Cuenta, id, Ip, ct);
        return t is null ? NotFound() : View(new DetalleTramiteVM { Expediente = ExpedienteVista.Desde(t) });
    }

    // GET /Tramites/Estado/TRA-2026-12345  → JSON para que la página "se actualice sola"
    [HttpGet]
    public async Task<IActionResult> Estado(string id, CancellationToken ct)
    {
        var t = await portal.TramiteAsync(Cuenta, id, Ip, ct);
        return t is null ? NotFound() : Json(new { estado = t.Estado, carnet = t.NumeroCarnet });
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

        // Exige número + cédula del titular. Mensaje genérico: no revela si el número existe.
        var t = await portal.ConsultarPublicoAsync(vm.Cedula, vm.NumeroTramite, Ip, ct);
        if (t is null)
        {
            ModelState.AddModelError(string.Empty, "No encontramos un trámite con esos datos.");
            return View(vm);
        }

        vm.Resultado = ExpedienteVista.Desde(t);
        return View(vm);
    }

    // GET /Tramites/CambiarCita/TRA-2026-12345
    [HttpGet]
    public IActionResult CambiarCita(string id) => View(new CambiarCitaVM { NumeroTramite = id });

    // POST /Tramites/CambiarCita
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarCita(CambiarCitaVM vm, CancellationToken ct)
    {
        if (vm.NuevaFecha is not null && vm.NuevaFecha <= DateTime.Now)
            ModelState.AddModelError(nameof(vm.NuevaFecha), "Elige una fecha futura.");
        if (!ModelState.IsValid) return View(vm);

        try
        {
            await portal.ReprogramarCitaAsync(Cuenta, vm.NumeroTramite, new DateTimeOffset(vm.NuevaFecha!.Value), Ip, ct);
        }
        catch (ReglaNegocioException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);   // p. ej. no hay cupo ese día
            return View(vm);
        }

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

        // Pendiente: la API del Core aún no recibe adjuntos. Se informa al ciudadano que lo entregue en la cita.
        TempData["Aviso"] = $"Recibimos {archivo.FileName}. Lleva también el original a tu cita de valoración.";
        return RedirectToAction(nameof(Detalle), new { id });
    }

    // GET /Tramites/DescargarAcuse/TRA-2026-12345
    [HttpGet]
    public async Task<IActionResult> DescargarAcuse(string id, CancellationToken ct)
    {
        var t = await portal.TramiteAsync(Cuenta, id, Ip, ct);
        if (t is null) return NotFound();

        var texto = $"""
            CONSEJO NACIONAL DE DISCAPACIDAD (CONADIS)
            ACUSE DE RECIBO

            Trámite:     {t.Numero}
            Expediente:  {t.NumeroExpediente ?? "—"}
            Titular:     {t.NombreCompleto}
            Recibido el: {t.FechaRegistro.LocalDateTime:dd/MM/yyyy HH:mm}
            Estado:      {t.Estado}
            """;
        return File(System.Text.Encoding.UTF8.GetBytes(texto), "text/plain", $"acuse-{t.Numero}.txt");
    }

    // GET /Tramites/Apelar/TRA-2026-12345
    [HttpGet]
    public async Task<IActionResult> Apelar(string id, CancellationToken ct)
    {
        var t = await portal.TramiteAsync(Cuenta, id, Ip, ct);
        if (t is null) return NotFound();
        if (t.Estado != "Rechazado") return RedirectToAction(nameof(Detalle), new { id });

        return View(new ApelacionVM { NumeroTramite = id });
    }

    // POST /Tramites/Apelar
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apelar(ApelacionVM vm, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(vm);

        try
        {
            // El Core valida el plazo de 30 días desde el dictamen.
            await portal.ApelarAsync(Cuenta, vm.NumeroTramite, vm.Motivo, Ip, ct);
        }
        catch (ReglaNegocioException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(vm);
        }

        TempData["Mensaje"] = "Recibimos tu apelación.";
        return RedirectToAction(nameof(Detalle), new { id = vm.NumeroTramite });
    }
}
