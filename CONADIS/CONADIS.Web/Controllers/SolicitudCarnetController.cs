using CONADIS.Application.Dtos;
using CONADIS.Application.Interfaces;
using CONADIS.Web.Extensions;
using CONADIS.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Web.Controllers;

/// <summary>
/// Asistente de 5 pasos para solicitar la certificación y el carnet PCD.
/// Patrón por paso: GET muestra la pantalla, POST valida, guarda el borrador y redirige al siguiente.
/// </summary>
[Authorize]
public class SolicitudCarnetController : Controller
{
    private const string ClaveBorrador = "SolicitudCarnet.Borrador";

    private readonly ISolicitudExpedienteService _solicitudes;
    private readonly IJceIdentityService _jce;
    private readonly IMspDiagnosticService _msp;
    private readonly ILogger<SolicitudCarnetController> _logger;

    public SolicitudCarnetController(
        ISolicitudExpedienteService solicitudes,
        IJceIdentityService jce,
        IMspDiagnosticService msp,
        ILogger<SolicitudCarnetController> logger)
    {
        _solicitudes = solicitudes;
        _jce = jce;
        _msp = msp;
        _logger = logger;
    }

    // ===================================================================
    // Inicio del asistente
    // ===================================================================

    // GET /SolicitudCarnet  → retoma el borrador en el paso pendiente
    [HttpGet]
    public IActionResult Index()
    {
        var borrador = ObtenerBorrador();
        return IrAlPaso(borrador.PasoPendiente);
    }

    // GET /SolicitudCarnet/Nueva  → descarta cualquier borrador y empieza de cero
    [HttpGet]
    public IActionResult Nueva()
    {
        GuardarBorrador(new SolicitudBorrador());
        return RedirectToAction(nameof(Identificacion));
    }

    // POST /SolicitudCarnet/GuardarYSalir  → botón "Guardar y salir"
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult GuardarYSalir()
    {
        TempData["Mensaje"] = "Guardamos tu avance. Puedes continuar cuando quieras.";
        return RedirectToAction("Index", "Tramites");
    }

    // ===================================================================
    // Paso 1: Identificación
    // ===================================================================

    [HttpGet]
    public IActionResult Identificacion()
    {
        var borrador = ObtenerBorrador();
        return View(borrador.Identificacion ?? new IdentificacionVM());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Identificacion(IdentificacionVM vm, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(vm);

        // Se vuelve a validar en el servidor: no confiamos en lo que venga del navegador.
        var cedula = vm.Cedula.Replace("-", "");
        try
        {
            var jce = await _jce.ValidarCedulaAsync(cedula, ct);
            if (!jce.EsValida)
            {
                ModelState.AddModelError(nameof(vm.Cedula), "La cédula no fue encontrada en la JCE.");
                return View(vm);
            }

            vm.IdentidadValidada = true;
            vm.Nombres = jce.Nombres;
            vm.Apellidos = jce.Apellidos;
            vm.FechaNacimiento = jce.FechaNacimiento;
        }
        catch (Exception ex)
        {
            // RF-INT-01: si la JCE no responde, se permite continuar y se valida luego.
            _logger.LogWarning(ex, "JCE no disponible al validar {Cedula}", cedula);
            vm.IdentidadValidada = false;
            TempData["Aviso"] = "No pudimos validar tu identidad en este momento. Puedes continuar; " +
                                "la validaremos automáticamente y te avisaremos.";
        }

        vm.Cedula = cedula;
        var borrador = ObtenerBorrador();
        borrador.Identificacion = vm;
        GuardarBorrador(borrador);

        return RedirectToAction(nameof(Contacto));
    }

    // POST /SolicitudCarnet/ValidarJce  → botón "Validar con la JCE" (AJAX, devuelve JSON)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ValidarJce(string cedula, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cedula))
            return BadRequest(new { mensaje = "Escribe la cédula." });

        try
        {
            var r = await _jce.ValidarCedulaAsync(cedula.Replace("-", ""), ct);
            return Json(new
            {
                valida = r.EsValida,
                nombres = r.Nombres,
                apellidos = r.Apellidos,
                fechaNacimiento = r.FechaNacimiento.ToString("dd/MM/yyyy")
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JCE no disponible");
            return Json(new { valida = false, servicioNoDisponible = true });
        }
    }

    // ===================================================================
    // Paso 2: Contacto
    // ===================================================================

    [HttpGet]
    public IActionResult Contacto()
    {
        var borrador = ObtenerBorrador();
        if (borrador.Identificacion is null) return IrAlPaso(borrador.PasoPendiente);

        return View(borrador.Contacto ?? new ContactoVM());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Contacto(ContactoVM vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var borrador = ObtenerBorrador();
        borrador.Contacto = vm;
        GuardarBorrador(borrador);

        return RedirectToAction(nameof(Diagnostico));
    }

    // ===================================================================
    // Paso 3: Diagnóstico y documentos
    // ===================================================================

    private static readonly string[] ExtensionesPermitidas = [".pdf", ".jpg", ".jpeg", ".png"];
    private const long TamanoMaximo = 10 * 1024 * 1024; // 10 MB

    [HttpGet]
    public IActionResult Diagnostico()
    {
        var borrador = ObtenerBorrador();
        if (borrador.Contacto is null) return IrAlPaso(borrador.PasoPendiente);

        return View(borrador.Diagnostico ?? new DiagnosticoVM());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(60 * 1024 * 1024)]
    public async Task<IActionResult> Diagnostico(DiagnosticoVM vm, CancellationToken ct)
    {
        var borrador = ObtenerBorrador();
        if (borrador.Identificacion is null) return IrAlPaso(1);

        // Certificado obligatorio: o se sube ahora o ya estaba subido en el borrador
        vm.NombreArchivoCertificado ??= borrador.Diagnostico?.NombreArchivoCertificado;
        if (vm.CertificadoMedico is null && vm.NombreArchivoCertificado is null)
            ModelState.AddModelError(nameof(vm.CertificadoMedico), "Sube el certificado médico.");

        if (vm.CertificadoMedico is not null) ValidarArchivo(vm.CertificadoMedico, nameof(vm.CertificadoMedico));
        foreach (var f in vm.OtrosEstudios) ValidarArchivo(f, nameof(vm.OtrosEstudios));

        if (!ModelState.IsValid) return View(vm);

        // TODO: guardar físicamente los archivos con un IAlmacenamientoArchivos (capa Infrastructure).
        if (vm.CertificadoMedico is not null)
            vm.NombreArchivoCertificado = vm.CertificadoMedico.FileName;
        vm.NombresOtrosEstudios = (borrador.Diagnostico?.NombresOtrosEstudios ?? new())
            .Concat(vm.OtrosEstudios.Select(f => f.FileName))
            .Distinct()
            .ToList();

        // RF-INT-02: validar con Salud Pública; si no responde queda "pendiente" (null).
        if (!string.IsNullOrWhiteSpace(vm.CodigoCie10))
        {
            try
            {
                var msp = await _msp.ValidarDiagnosticoAsync(
                    borrador.Identificacion.Cedula, vm.CodigoCie10, ct);
                vm.ValidadoPorMsp = msp.EsValido;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MSP no disponible");
                vm.ValidadoPorMsp = null;
            }
        }

        borrador.Diagnostico = vm;
        GuardarBorrador(borrador);

        return RedirectToAction(nameof(CitaApoyos));
    }

    // GET /SolicitudCarnet/BuscarCentrosSalud?q=hos  → autocompletado (mín. 3 letras)
    [HttpGet]
    public IActionResult BuscarCentrosSalud(string q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 3) return Json(Array.Empty<object>());

        // TODO: reemplazar por un catálogo real (ICatalogoService.BuscarCentrosSaludAsync)
        var catalogo = new[]
        {
            new { nombre = "Hospital Dr. Darío Contreras", provincia = "Santo Domingo" },
            new { nombre = "Hospital Dr. Luis E. Aybar", provincia = "Distrito Nacional" },
            new { nombre = "Hospital Regional José María Cabral y Báez", provincia = "Santiago" }
        };

        return Json(catalogo.Where(c => c.nombre.Contains(q, StringComparison.OrdinalIgnoreCase)));
    }

    // POST /SolicitudCarnet/QuitarArchivo  → botón "Quitar" (AJAX)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult QuitarArchivo(string nombre)
    {
        var borrador = ObtenerBorrador();
        if (borrador.Diagnostico is null) return NotFound();

        if (borrador.Diagnostico.NombreArchivoCertificado == nombre)
            borrador.Diagnostico.NombreArchivoCertificado = null;
        borrador.Diagnostico.NombresOtrosEstudios.Remove(nombre);

        GuardarBorrador(borrador);
        return Ok();
    }

    // ===================================================================
    // Paso 4: Cita y apoyos
    // ===================================================================

    [HttpGet]
    public IActionResult CitaApoyos()
    {
        var borrador = ObtenerBorrador();
        if (borrador.Diagnostico is null) return IrAlPaso(borrador.PasoPendiente);

        return View(borrador.CitaApoyos ?? new CitaApoyosVM());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CitaApoyos(CitaApoyosVM vm)
    {
        if (vm.FechaCita is not null && vm.FechaCita <= DateTime.Now)
            ModelState.AddModelError(nameof(vm.FechaCita), "La cita debe ser en una fecha futura.");

        if (!ModelState.IsValid) return View(vm);

        var borrador = ObtenerBorrador();
        borrador.CitaApoyos = vm;
        GuardarBorrador(borrador);

        return RedirectToAction(nameof(Revision));
    }

    // ===================================================================
    // Paso 5: Revisión y envío
    // ===================================================================

    [HttpGet]
    public IActionResult Revision()
    {
        var borrador = ObtenerBorrador();
        if (borrador.PasoPendiente < 5) return IrAlPaso(borrador.PasoPendiente);

        return View(new RevisionVM { Borrador = borrador });
    }

    // POST /SolicitudCarnet/Enviar  → botón "Enviar solicitud"
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Enviar(RevisionVM vm, CancellationToken ct)
    {
        var borrador = ObtenerBorrador();
        if (borrador.PasoPendiente < 5) return IrAlPaso(borrador.PasoPendiente);

        vm.Borrador = borrador;
        if (!ModelState.IsValid) return View(nameof(Revision), vm);

        var id = borrador.Identificacion!;
        var c = borrador.Contacto!;

        var dto = new RegistrarSolicitudDto(
            Cedula: id.Cedula,
            Nombres: id.Nombres ?? string.Empty,
            Apellidos: id.Apellidos ?? string.Empty,
            FechaNacimiento: id.FechaNacimiento ?? default,
            Sexo: id.Sexo,
            Telefono: c.Telefono,
            Email: c.Email,
            Provincia: c.Provincia,
            Municipio: c.Municipio,
            Sector: c.Sector,
            DireccionDetalle: c.DireccionDetalle);

        var resultado = await _solicitudes.RegistrarSolicitudAsync(dto, ct);
        if (resultado.IsFailure)
        {
            ModelState.AddModelError(string.Empty, resultado.Error);
            return View(nameof(Revision), vm);
        }

        // Datos para la pantalla de confirmación antes de borrar el borrador
        TempData["CitaFecha"] = borrador.CitaApoyos!.FechaCita?.ToString("o");
        TempData["CitaCentro"] = borrador.CitaApoyos.CentroValoracion;

        HttpContext.Session.Remove(ClaveBorrador);

        return RedirectToAction(nameof(Confirmacion), new { numero = resultado.Value.NumeroExpediente });
    }

    // GET /SolicitudCarnet/Confirmacion?numero=EXP-2026-12345  → "Recibimos tu solicitud"
    [HttpGet]
    public IActionResult Confirmacion(string numero)
    {
        if (string.IsNullOrWhiteSpace(numero)) return RedirectToAction("Index", "Tramites");

        var vm = new ConfirmacionVM
        {
            NumeroTramite = numero,
            CentroValoracion = TempData["CitaCentro"] as string,
            FechaCita = DateTime.TryParse(TempData["CitaFecha"] as string, out var f) ? f : null
        };
        return View(vm);
    }

    // ===================================================================
    // Auxiliares privados
    // ===================================================================

    private SolicitudBorrador ObtenerBorrador()
        => HttpContext.Session.GetObject<SolicitudBorrador>(ClaveBorrador) ?? new SolicitudBorrador();

    private void GuardarBorrador(SolicitudBorrador borrador)
    {
        borrador.UltimoGuardado = DateTime.Now;
        HttpContext.Session.SetObject(ClaveBorrador, borrador);
    }

    private IActionResult IrAlPaso(int paso) => paso switch
    {
        1 => RedirectToAction(nameof(Identificacion)),
        2 => RedirectToAction(nameof(Contacto)),
        3 => RedirectToAction(nameof(Diagnostico)),
        4 => RedirectToAction(nameof(CitaApoyos)),
        _ => RedirectToAction(nameof(Revision))
    };

    private void ValidarArchivo(IFormFile archivo, string campo)
    {
        var ext = Path.GetExtension(archivo.FileName).ToLowerInvariant();
        if (!ExtensionesPermitidas.Contains(ext))
            ModelState.AddModelError(campo, $"{archivo.FileName}: solo se permiten PDF, JPG o PNG.");
        if (archivo.Length > TamanoMaximo)
            ModelState.AddModelError(campo, $"{archivo.FileName}: supera los 10 MB.");
    }
}
