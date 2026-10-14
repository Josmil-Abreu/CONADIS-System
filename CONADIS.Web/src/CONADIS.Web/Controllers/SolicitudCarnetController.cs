using CONADIS.Web.Extensions;
using CONADIS.Web.Infraestructura;
using CONADIS.Web.Negocio.Datos;
using CONADIS.Web.Negocio.Externos;
using CONADIS.Web.Negocio.Modelos;
using CONADIS.Web.Negocio.Servicios;
using CONADIS.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Web.Controllers;

/// <summary>
/// Asistente de 5 pasos para solicitar la certificación y el carnet PCD.
/// Patrón por paso: GET muestra la pantalla, POST valida, guarda el borrador y redirige al siguiente.
/// </summary>
[Authorize]
public class SolicitudCarnetController(ServicioPortal portal, ServicioBorradores borradores, ILogger<SolicitudCarnetController> logger)
    : ControladorPortal
{
    private const string ClaveBorrador = "SolicitudCarnet.Borrador";
    private readonly ILogger<SolicitudCarnetController> _logger = logger;

    // ===================================================================
    // Inicio del asistente
    // ===================================================================

    // GET /SolicitudCarnet  → retoma el borrador en el paso pendiente
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        // Si la sesión no tiene borrador, se recupera el guardado en la base ("Guardar y salir").
        if (HttpContext.Session.GetString(ClaveBorrador) is null
            && await borradores.ObtenerAsync(Cuenta.CuentaId, TiposBorrador.Certificacion, ct) is { } guardado)
        {
            var recuperado = System.Text.Json.JsonSerializer.Deserialize<SolicitudBorrador>(guardado.Datos);
            if (recuperado is not null) GuardarBorrador(recuperado);
        }

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
    public async Task<IActionResult> GuardarYSalir(CancellationToken ct)
    {
        var borrador = ObtenerBorrador();
        await borradores.GuardarAsync(Cuenta.CuentaId, TiposBorrador.Certificacion, borrador.PasoPendiente, borrador, ct);
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
            var jce = await portal.IdentidadAsync(cedula, Ip, ct);
            if (jce?.Estado == "Invalida")
            {
                ModelState.AddModelError(nameof(vm.Cedula), $"La JCE no valida esta cédula: {jce.Motivo}");
                return View(vm);
            }

            vm.IdentidadValidada = jce?.Estado == "Validada";
            vm.Nombres = jce?.Nombres ?? vm.Nombres;
            vm.Apellidos = jce?.Apellidos ?? vm.Apellidos;
            if (!vm.IdentidadValidada)
                TempData["Aviso"] = "No pudimos validar tu identidad en este momento. Puedes continuar; la validaremos automáticamente.";
        }
        catch (ReglaNegocioException ex)
        {
            ModelState.AddModelError(nameof(vm.Cedula), ex.Message);
            return View(vm);
        }
        catch (SndNoDisponibleException ex)
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
            var r = await portal.IdentidadAsync(cedula.Replace("-", ""), Ip, ct);
            if (r is null || r.Estado == "Pendiente") return Json(new { valida = false, servicioNoDisponible = true });
            return Json(new
            {
                valida = r.Estado == "Validada",
                nombres = r.Nombres,
                apellidos = r.Apellidos,
                fechaNacimiento = (string?)null,
                motivo = r.Motivo
            });
        }
        catch (ReglaNegocioException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
        catch (SndNoDisponibleException ex)
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
    public async Task<IActionResult> Diagnostico(DiagnosticoVM vm)
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

        // Solo se guardan los nombres: el original del certificado se presenta en la cita de valoración.
        if (vm.CertificadoMedico is not null)
            vm.NombreArchivoCertificado = vm.CertificadoMedico.FileName;
        vm.NombresOtrosEstudios = (borrador.Diagnostico?.NombresOtrosEstudios ?? new())
            .Concat(vm.OtrosEstudios.Select(f => f.FileName))
            .Distinct()
            .ToList();

        // RF-INT-02: Integración valida el diagnóstico con Salud Pública al enviar la solicitud
        // (si el MSP no responde, la solicitud entra igual y queda pendiente de validar).
        vm.ValidadoPorMsp = null;
        await Task.CompletedTask;

        borrador.Diagnostico = vm;
        GuardarBorrador(borrador);

        return RedirectToAction(nameof(CitaApoyos));
    }

    // GET /SolicitudCarnet/BuscarCentrosSalud?q=hos  → autocompletado (mín. 3 letras)
    [HttpGet]
    public IActionResult BuscarCentrosSalud(string q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 3) return Json(Array.Empty<object>());

        // Catálogo de ejemplo de centros de salud (el Core no tiene este catálogo; se escribe libre si no aparece).
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
    public async Task<IActionResult> CitaApoyos(CancellationToken ct)
    {
        var borrador = ObtenerBorrador();
        if (borrador.Diagnostico is null) return IrAlPaso(borrador.PasoPendiente);

        ViewBag.Centros = await NombresCentrosAsync(ct);

        return View(borrador.CitaApoyos ?? new CitaApoyosVM());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CitaApoyos(CitaApoyosVM vm, CancellationToken ct)
    {
        if (vm.FechaCita is not null && vm.FechaCita <= DateTime.Now)
            ModelState.AddModelError(nameof(vm.FechaCita), "La cita debe ser en una fecha futura.");

        if (!ModelState.IsValid)
        {
            ViewBag.Centros = await NombresCentrosAsync(ct);
            return View(vm);
        }

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
        var d = borrador.Diagnostico!;
        var cita = borrador.CitaApoyos!;

        var centros = await portal.CentrosAsync(ct);
        var centro = centros.FirstOrDefault(x => x.Nombre.Equals(cita.CentroValoracion, StringComparison.OrdinalIgnoreCase));
        var apoyos = string.Join(", ", new[]
        {
            cita.InterpreteLenguaSenas ? "Intérprete de lengua de señas" : null,
            cita.AccesoSillaRuedas ? "Acceso para silla de ruedas" : null,
            cita.Acompanante ? "Acompañante" : null,
            cita.OtrosApoyos
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        var solicitud = new SolicitudPortal
        {
            Tipo = id.TipoSolicitud switch { TipoSolicitud.Renovacion => "Renovacion", TipoSolicitud.Duplicado => "Duplicado", _ => "Certificacion" },
            Cedula = id.Cedula,
            Nombres = id.Nombres ?? Cuenta.NombreCompleto,
            Apellidos = id.Apellidos ?? "-",
            FechaNacimiento = id.FechaNacimiento,
            Sexo = id.Sexo,
            Telefono = c.Telefono,
            Correo = c.Email,
            CanalNotificacion = c.AvisosEmail ? "Correo" : "SMS",
            Provincia = c.Provincia,
            Municipio = c.Municipio,
            Sector = c.Sector,
            Direccion = c.DireccionDetalle,
            CedulaTutor = id.ParaQuien == ParaQuien.Representado ? Cuenta.Cedula : null,
            VinculoTutor = id.ParaQuien == ParaQuien.Representado ? "Tutor" : null,
            Establecimiento = d.CentroSalud,
            NumeroCertificadoMedico = d.NumeroCertificado,
            FechaCertificadoMedico = d.FechaCertificado,
            CodigoCie10 = d.CodigoCie10,
            CentroPreferidoId = centro?.CentroId,
            FechaCitaPreferida = cita.FechaCita is { } f ? new DateTimeOffset(f) : null,
            Apoyos = string.IsNullOrWhiteSpace(apoyos) ? null : apoyos,
            MotivoDuplicado = id.TipoSolicitud == TipoSolicitud.Duplicado ? "Solicitado desde el asistente" : null,
            ConsentimientoDatos = vm.AutorizaDatos
        };

        SolicitudRegistrada resultado;
        try
        {
            // La clave de idempotencia es la del borrador: pulsar "Enviar" dos veces no crea dos solicitudes.
            resultado = await portal.EnviarSolicitudAsync(Cuenta, solicitud,
                ClaveIdempotencia("solicitud", $"{id.Cedula}-{borrador.UltimoGuardado:yyyyMMddHHmmss}"), Ip, ct);
        }
        catch (ReglaNegocioException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(nameof(Revision), vm);
        }

        // Datos para la pantalla de confirmación antes de borrar el borrador
        TempData["CitaFecha"] = borrador.CitaApoyos!.FechaCita?.ToString("o");
        TempData["CitaCentro"] = borrador.CitaApoyos.CentroValoracion;

        HttpContext.Session.Remove(ClaveBorrador);
        await borradores.EliminarAsync(Cuenta.CuentaId, TiposBorrador.Certificacion, ct);

        if (resultado.NumeroOrden is not null)
            TempData["Aviso"] = $"Tu solicitud tiene un pago pendiente (orden {resultado.NumeroOrden}).";
        return RedirectToAction(nameof(Confirmacion), new { numero = resultado.NumeroTramite });
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

    private async Task<List<string>> NombresCentrosAsync(CancellationToken ct)
    {
        try { return (await portal.CentrosAsync(ct)).Where(x => x.Activo).Select(x => x.Nombre).ToList(); }
        catch (SndNoDisponibleException) { return []; }
    }

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
