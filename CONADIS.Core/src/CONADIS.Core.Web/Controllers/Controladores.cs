using System.Data;
using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Servicios;
using CONADIS.Core.Web.Infraestructura;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Core.Web.Controllers;

// =====================================================================================================
// Cuenta: login del personal (directo o con el pase de CONADIS.Inicio)
// =====================================================================================================

[AllowAnonymous]
public sealed class CuentaController(ServicioAutenticacion autenticacion, IConfiguration config) : ControladorCore
{
    [HttpGet]
    public IActionResult IniciarSesion(string? returnUrl = null)
        => User.Identity?.IsAuthenticated == true ? RedirectToAction("Index", "Inicio") : View(model: returnUrl);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IniciarSesion(string usuario, string contrasena, string? returnUrl, CancellationToken ct)
    {
        var r = await autenticacion.IniciarSesionAsync(usuario, contrasena, Ip, ct);
        if (!r.Exito || r.Usuario is null)
        {
            ViewBag.Error = r.Mensaje;
            ViewBag.Usuario = usuario;
            return View(model: returnUrl);
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            SesionPersonal.Principal(r.Usuario, CookieAuthenticationDefaults.AuthenticationScheme));
        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction("Index", "Inicio");
    }

    /// <summary>GET /Cuenta/Entrar?pase=...  → lo abre CONADIS.Inicio después del login único.</summary>
    [HttpGet]
    public async Task<IActionResult> Entrar(string? pase, CancellationToken ct)
    {
        var u = await autenticacion.IniciarConPaseAsync(pase, config["Inicio:ClaveTraspaso"] ?? "", Ip, ct);
        if (u is null)
        {
            TempData["Error"] = "El acceso desde CONADIS Inicio venció o no es válido. Inicie sesión aquí.";
            return RedirectToAction(nameof(IniciarSesion));
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            SesionPersonal.Principal(u, CookieAuthenticationDefaults.AuthenticationScheme));
        return RedirectToAction("Index", "Inicio");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CerrarSesion()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(IniciarSesion));
    }

    [HttpGet]
    public IActionResult AccesoDenegado() => View();
}

// =====================================================================================================
// Inicio: panel con las opciones del rol
// =====================================================================================================

[Authorize]
public sealed class InicioController : ControladorCore
{
    [HttpGet]
    public IActionResult Index() => View(Menu.Para(User).ToList());

    [AllowAnonymous]
    public IActionResult Error() => View();
}

// =====================================================================================================
// Expedientes: búsqueda y Expediente Único (analistas, supervisores, comité, evaluadores, auditores)
// =====================================================================================================

[Authorize(Policy = "Expedientes")]
public sealed class ExpedientesController(ServicioExpedientes expedientes, ServicioCarnets carnets) : ControladorCore
{
    [HttpGet]
    public async Task<IActionResult> Index(string? texto, string? estado, CancellationToken ct)
    {
        ViewBag.Texto = texto;
        ViewBag.Estado = estado;
        // Los evaluadores solo ven su centro (lo aplica el servicio).
        var lista = string.IsNullOrWhiteSpace(texto) && string.IsNullOrWhiteSpace(estado)
            ? []
            : await expedientes.BuscarAsync(Usuario, texto, estado, ct);
        return View(lista);
    }

    [HttpGet]
    public async Task<IActionResult> Detalle(int id, CancellationToken ct)
    {
        var e = await expedientes.ObtenerAsync(id, ct);
        if (e is null) return NotFound();
        if (Usuario.Rol == Roles.Evaluador && e.Cabecera.CentroId != Usuario.CentroId) return Forbid();
        return View(e);
    }

    /// <summary>Emitir carnet de un expediente aprobado (RF: el analista lo emite; la firma la pone Integración).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.AnalistaRegistro + "," + Roles.Administrador)]
    public async Task<IActionResult> EmitirCarnet(int id, CancellationToken ct)
    {
        var c = await carnets.EmitirAsync(Usuario, id, ct);
        TempData["Mensaje"] = $"Carnet {c.NumeroCarnet} emitido (certificado {c.NumeroCertificado}).";
        return RedirectToAction(nameof(Detalle), new { id });
    }
}

// =====================================================================================================
// Valoración: agenda del centro del evaluador (la codificación CIF y el WHODAS son pantallas pendientes)
// =====================================================================================================

[Authorize(Policy = "Valoracion")]
public sealed class ValoracionController(ServicioCitas citas, ServicioValoracion valoracion) : ControladorCore
{
    [HttpGet]
    public async Task<IActionResult> Index(DateOnly? fecha, CancellationToken ct)
    {
        var dia = fecha ?? DateOnly.FromDateTime(DateTime.Today);
        ViewBag.Fecha = dia;
        if (Usuario.CentroId is not { } centro)
        {
            TempData["Error"] = "Su usuario no tiene un centro de valoración asignado. Avise al administrador.";
            return View(new List<CitaAgenda>());
        }
        return View(await citas.AgendaAsync(Usuario, centro, dia, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Iniciar(int expedienteId, CancellationToken ct)
    {
        var evaluacionId = await valoracion.IniciarAsync(Usuario, expedienteId, ct);
        return RedirectToAction(nameof(Evaluacion), new { id = evaluacionId });
    }

    /// <summary>
    /// PANTALLA PENDIENTE: aquí van la codificación CIF (GuardarCodificacionAsync / QuitarCodificacionAsync),
    /// el WHODAS (RegistrarWhodasAsync con RowVersion), la firma de la disciplina (FirmarAsync) y la consolidación.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Evaluacion(int id, CancellationToken ct)
    {
        var e = await valoracion.ObtenerAsync(id, ct);
        return e is null ? NotFound() : View(e);
    }
}

// =====================================================================================================
// Comité: votar dictámenes y resolver apelaciones
// =====================================================================================================

[Authorize(Policy = "Comite")]
public sealed class ComiteController(ServicioDictamen dictamen) : ControladorCore
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewBag.Apelaciones = await dictamen.ApelacionesPendientesAsync(ct);
        return View(await dictamen.PendientesAsync(ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Votar(int evaluacionId, string voto, string? motivo, CancellationToken ct)
    {
        var r = await dictamen.VotarAsync(Usuario, new NuevoVoto(evaluacionId, voto, Motivo: motivo), ct);
        TempData["Mensaje"] = r.Resultado switch
        {
            "Aprobado" => "Voto registrado. El dictamen quedó APROBADO.",
            "Rechazado" => "Voto registrado. El dictamen quedó RECHAZADO.",
            _ => $"Voto registrado. Faltan votos: {r.VotosCoincidentes} de {r.Quorum} necesarios."
        };
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolverApelacion(int apelacionId, bool admitir, string resolucion, CancellationToken ct)
    {
        await dictamen.ResolverApelacionAsync(Usuario, apelacionId, admitir, resolucion, ct);
        TempData["Mensaje"] = admitir ? "Apelación admitida." : "Apelación rechazada.";
        return RedirectToAction(nameof(Index));
    }
}

// =====================================================================================================
// Reportes: cualquier reporte del Core como tabla (la versión RDLC / PDF se diseña con las interfaces)
// =====================================================================================================

[Authorize(Policy = "Reportes")]
public sealed class ReportesController(ServicioReportes reportes) : ControladorCore
{
    public static readonly IReadOnlyDictionary<string, string> Disponibles = new Dictionary<string, string>
    {
        ["estado"] = "Expedientes por estado",
        ["estadistica"] = "Estadística anonimizada",
        ["ingresos"] = "Ingresos por tasas",
        ["carnets"] = "Carnets emitidos",
        ["indicadores"] = "Indicadores de tiempo de trámite"
    };

    [HttpGet]
    public async Task<IActionResult> Index(string? reporte, DateOnly? desde, DateOnly? hasta, CancellationToken ct)
    {
        var d = desde ?? DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
        var h = hasta ?? DateOnly.FromDateTime(DateTime.Today);
        ViewBag.Reporte = reporte;
        ViewBag.Desde = d;
        ViewBag.Hasta = h;

        DataTable? tabla = reporte switch
        {
            "estado" => await reportes.ExpedientesPorEstadoAsync(Usuario, d, h, ct: ct),
            "estadistica" => await reportes.EstadisticaAnonimizadaAsync(Usuario, d, h, ct),
            "ingresos" => await reportes.IngresosAsync(Usuario, d, h, ct),
            "carnets" => await reportes.CarnetsEmitidosAsync(Usuario, d, h, ct),
            "indicadores" => await reportes.IndicadoresTramiteAsync(Usuario, d, h, ct),
            _ => null
        };
        return View(tabla);
    }
}

// =====================================================================================================
// Auditoría (auditor y administrador): bitácora y verificación de la cadena de hashes
// =====================================================================================================

[Authorize(Policy = "Auditoria")]
public sealed class AuditoriaController(ServicioAuditoria auditoria) : ControladorCore
{
    [HttpGet]
    public async Task<IActionResult> Index(DateOnly? desde, DateOnly? hasta, string? usuario, bool verificar, CancellationToken ct)
    {
        var d = desde ?? DateOnly.FromDateTime(DateTime.Today.AddDays(-7));
        var h = hasta ?? DateOnly.FromDateTime(DateTime.Today);
        ViewBag.Desde = d;
        ViewBag.Hasta = h;
        ViewBag.UsuarioFiltro = usuario;
        if (verificar) ViewBag.Problemas = await auditoria.VerificarCadenaAsync(Usuario, ct);

        var eventos = await auditoria.ListarEventosAsync(Usuario,
            new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue)), new DateTimeOffset(h.ToDateTime(TimeOnly.MaxValue)), usuario, ct: ct);
        return View(eventos);
    }
}

// =====================================================================================================
// Administración: usuarios del personal (crear / editar son pantallas pendientes: ServicioUsuarios.GuardarAsync)
// =====================================================================================================

[Authorize(Policy = "Administracion")]
public sealed class AdministracionController(ServicioUsuarios usuarios) : ControladorCore
{
    [HttpGet]
    public async Task<IActionResult> Index(string? texto, CancellationToken ct)
    {
        ViewBag.Texto = texto;
        return View(await usuarios.ListarAsync(texto, ct));
    }
}
