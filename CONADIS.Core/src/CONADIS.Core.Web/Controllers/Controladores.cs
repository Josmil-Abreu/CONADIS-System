using System.Data;
using CONADIS.Core.Negocio.Datos;
using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Reportes;
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

public sealed record FormularioPresencial(string Tipo, string? Cedula, string? ActaNacimiento, string Nombres, string Apellidos,
    DateOnly? FechaNacimiento, int? Sexo, string? Telefono, string? Correo, string CanalNotificacion, string? Provincia, string? Municipio,
    string? Sector, string? Direccion, string? CedulaTutor, string? VinculoTutor, string? Establecimiento, string? NumeroCertificadoMedico,
    DateOnly? FechaCertificadoMedico, string? CodigoCie10);

[Authorize(Policy = "Expedientes")]
public sealed class ExpedientesController(ServicioExpedientes expedientes, ServicioCarnets carnets, ServicioCitas citas,
    ServicioCatalogos catalogos) : ControladorCore
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

    [HttpGet]
    public async Task<IActionResult> Detalle(int id, CancellationToken ct)
    {
        var e = await expedientes.ObtenerAsync(id, ct);
        if (e is null) return NotFound();
        if (Usuario.Rol == Roles.Evaluador && e.Cabecera.CentroId != Usuario.CentroId) return Forbid();
        if (Usuario.Rol is Roles.AnalistaRegistro or Roles.Administrador)
        {
            var centros = await catalogos.ListarCentrosAsync(true, ct);
            ViewBag.Centros = centros;
            var centro = e.Cabecera.CentroId ?? centros.FirstOrDefault()?.CentroId;
            ViewBag.Cupos = centro is { } c ? await citas.DisponibilidadAsync(c, DateOnly.FromDateTime(DateTime.Today.AddDays(1)), 15, ct) : [];
        }
        return View(e);
    }

    /// <summary>Asignar cita de valoración (analista de registro).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.AnalistaRegistro + "," + Roles.Administrador)]
    public async Task<IActionResult> AsignarCita(int id, int centroId, DateTime fechaHora, bool domiciliaria, CancellationToken ct)
    {
        await citas.AsignarAsync(Usuario, id, centroId, new DateTimeOffset(fechaHora), domiciliaria, ct);
        TempData["Mensaje"] = $"Cita asignada para el {fechaHora:dd/MM/yyyy HH:mm}. Se notifica al ciudadano.";
        return RedirectToAction(nameof(Detalle), new { id });
    }

    /// <summary>Anulación lógica de un carnet con motivo (supervisor de registro).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.SupervisorRegistro)]
    public async Task<IActionResult> AnularCarnet(int id, int carnetId, string motivo, CancellationToken ct)
    {
        await carnets.AnularAsync(Usuario, carnetId, motivo ?? "", ct);
        TempData["Mensaje"] = "Carnet anulado. Queda registrado en la auditoría.";
        return RedirectToAction(nameof(Detalle), new { id });
    }

    /// <summary>Personas cuya identidad no pudo validar la JCE: el analista la valida con el documento físico.</summary>
    [HttpGet]
    [Authorize(Roles = Roles.AnalistaRegistro + "," + Roles.SupervisorRegistro + "," + Roles.Administrador)]
    public async Task<IActionResult> Identidad(CancellationToken ct) => View(await expedientes.ListarIdentidadPendienteAsync(24, ct));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.AnalistaRegistro + "," + Roles.SupervisorRegistro + "," + Roles.Administrador)]
    public async Task<IActionResult> AprobarIdentidad(int personaId, string comentario, CancellationToken ct)
    {
        await expedientes.AprobarIdentidadManualAsync(Usuario, personaId, comentario ?? "", ct);
        TempData["Mensaje"] = "Identidad validada manualmente.";
        return RedirectToAction(nameof(Identidad));
    }

    /// <summary>Registro presencial en ventanilla (CU-01, canal Presencial).</summary>
    [HttpGet]
    [Authorize(Roles = Roles.AnalistaRegistro + "," + Roles.Administrador)]
    public IActionResult Presencial() => View(new FormularioPresencial("Certificacion", null, null, "", "", null, null, null, null, "Correo",
        null, null, null, null, null, null, null, null, null, null));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.AnalistaRegistro + "," + Roles.Administrador)]
    public async Task<IActionResult> Presencial(FormularioPresencial f, CancellationToken ct)
    {
        try
        {
            var r = await expedientes.RegistrarSolicitudAsync(new NuevaSolicitud
            {
                Canal = "Presencial", Tipo = f.Tipo, Cedula = f.Cedula, CedulaCuenta = f.Cedula, ActaNacimiento = f.ActaNacimiento,
                Nombres = f.Nombres ?? "", Apellidos = f.Apellidos ?? "", FechaNacimiento = f.FechaNacimiento, Sexo = f.Sexo,
                EstadoIdentidad = "Pendiente", Telefono = f.Telefono, Correo = f.Correo, CanalNotificacion = f.CanalNotificacion ?? "Correo",
                Provincia = f.Provincia, Municipio = f.Municipio, Sector = f.Sector, Direccion = f.Direccion,
                CedulaTutor = f.CedulaTutor, VinculoTutor = f.VinculoTutor, Establecimiento = f.Establecimiento,
                NumeroCertificadoMedico = f.NumeroCertificadoMedico, FechaCertificadoMedico = f.FechaCertificadoMedico, CodigoCie10 = f.CodigoCie10
            }, new ContextoAccion(Usuario.Username, Ip), ct);
            TempData["Mensaje"] = $"Solicitud {r.NumeroTramite} registrada (expediente {r.NumeroExpediente})" +
                (r.NumeroOrden is null ? "." : $". Orden de pago {r.NumeroOrden}: el ciudadano paga en Caja.") +
                " Valide la identidad con el documento físico en «Identidad pendiente».";
            return RedirectToAction(nameof(Index), new { texto = r.NumeroExpediente });
        }
        catch (ReglaNegocioException ex)
        {
            ViewBag.Error = ex.Message;
            return View(f);
        }
    }
}

// =====================================================================================================
// Valoración: agenda del centro, codificación CIF, WHODAS, firmas y consolidación
// =====================================================================================================

[Authorize(Policy = "Valoracion")]
public sealed class ValoracionController(ServicioCitas citas, ServicioValoracion valoracion, ServicioCatalogos catalogos) : ControladorCore
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

    /// <summary>Codificación CIF por disciplina, WHODAS 2.0, firma de la disciplina y consolidación.</summary>
    [HttpGet]
    public async Task<IActionResult> Evaluacion(int id, string? cif, CancellationToken ct)
    {
        var e = await valoracion.ObtenerAsync(id, ct);
        if (e is null) return NotFound();
        ViewBag.Cif = cif;
        ViewBag.Codigos = string.IsNullOrWhiteSpace(cif) ? new List<CodigoCif>() : await catalogos.BuscarCodigosCifAsync(cif, null, ct);
        return View(e);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Codificar(int id, string codigo, int calificador1, int? calificador2, bool esFacilitador,
        string? observacion, CancellationToken ct)
    {
        await valoracion.GuardarCodificacionAsync(Usuario, id, new NuevaCodificacion(codigo ?? "", calificador1, calificador2, esFacilitador, observacion), ct);
        TempData["Mensaje"] = $"Código {codigo} registrado.";
        return RedirectToAction(nameof(Evaluacion), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Quitar(int id, string codigo, CancellationToken ct)
    {
        await valoracion.QuitarCodificacionAsync(Usuario, id, codigo, ct);
        TempData["Mensaje"] = $"Código {codigo} quitado.";
        return RedirectToAction(nameof(Evaluacion), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Whodas(int id, string version, decimal cognicion, decimal movilidad, decimal autocuidado,
        decimal relaciones, decimal actividades, decimal participacion, string? observaciones, CancellationToken ct)
    {
        byte[] rv;
        try { rv = Convert.FromBase64String(version ?? ""); } catch (FormatException) { rv = []; }
        var r = await valoracion.RegistrarWhodasAsync(Usuario, id, rv,
            new PuntajesWhodas(cognicion, movilidad, autocuidado, relaciones, actividades, participacion), observaciones, ct);
        TempData["Mensaje"] = $"WHODAS guardado: puntaje {r.PuntajeWhodas:N1}, grado propuesto {r.GradoPropuesto ?? "sin discapacidad"} (baremo {r.VersionBaremo}).";
        return RedirectToAction(nameof(Evaluacion), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Firmar(int id, CancellationToken ct)
    {
        await valoracion.FirmarAsync(Usuario, id, ct);
        TempData["Mensaje"] = "Disciplina firmada.";
        return RedirectToAction(nameof(Evaluacion), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Consolidar(int id, string tipoPropuesto, CancellationToken ct)
    {
        await valoracion.ConsolidarAsync(Usuario, id, tipoPropuesto, ct);
        TempData["Mensaje"] = "Evaluación consolidada: pasa al comité para el dictamen.";
        return RedirectToAction(nameof(Evaluacion), new { id });
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
    public async Task<IActionResult> Votar(int evaluacionId, string voto, string? motivo, string? grado, string? tipo,
        bool permanente, DateOnly? fechaVencimiento, CancellationToken ct)
    {
        var r = await dictamen.VotarAsync(Usuario, new NuevoVoto(evaluacionId, voto,
            string.IsNullOrEmpty(grado) ? null : grado, string.IsNullOrEmpty(tipo) ? null : tipo, motivo, permanente, fechaVencimiento), ct);
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
    /// <summary>Clave → (título, archivo RDLC).</summary>
    public static readonly IReadOnlyDictionary<string, (string Titulo, string Rdlc)> Disponibles = new Dictionary<string, (string, string)>
    {
        ["estado"] = ("Expedientes por estado", "rptExpedientesPorEstado"),
        ["estadistica"] = ("Estadística anonimizada", "rptEstadisticaAnonimizada"),
        ["ingresos"] = ("Ingresos por tasas", "rptIngresos"),
        ["carnets"] = ("Carnets emitidos", "rptCarnetsEmitidos"),
        ["indicadores"] = ("Indicadores de tiempo de trámite", "rptIndicadoresTramite"),
        ["transacciones"] = ("Transacciones registradas (bitácora)", "rptTransacciones")
    };

    /// <summary>Vista previa en pantalla (tabla) del reporte elegido.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(string? reporte, DateOnly? desde, DateOnly? hasta, CancellationToken ct)
    {
        var (d, h) = Rango(desde, hasta);
        ViewBag.Reporte = reporte;
        ViewBag.Desde = d;
        ViewBag.Hasta = h;
        return View(reporte is null ? null : await DatosAsync(reporte, d, h, ct));
    }

    /// <summary>GET /Reportes/Descargar?reporte=ingresos&amp;desde=...&amp;formato=Pdf|Excel → archivo RDLC.</summary>
    [HttpGet]
    public async Task<IActionResult> Descargar(string reporte, DateOnly? desde, DateOnly? hasta, FormatoReporte formato, CancellationToken ct)
    {
        if (!Disponibles.TryGetValue(reporte, out var info)) return NotFound();
        var (d, h) = Rango(desde, hasta);
        var datos = await DatosAsync(reporte, d, h, ct);
        var a = await reportes.ArchivoAsync(Usuario, info.Rdlc, datos!, formato, $"Del {d:dd/MM/yyyy} al {h:dd/MM/yyyy}", Ip, ct);
        return File(a.Contenido, a.TipoContenido, a.NombreArchivo);
    }

    private async Task<DataTable?> DatosAsync(string reporte, DateOnly d, DateOnly h, CancellationToken ct) => reporte switch
    {
        "estado" => await reportes.ExpedientesPorEstadoAsync(Usuario, d, h, ct: ct),
        "estadistica" => await reportes.EstadisticaAnonimizadaAsync(Usuario, d, h, ct),
        "ingresos" => await reportes.IngresosAsync(Usuario, d, h, ct),
        "carnets" => await reportes.CarnetsEmitidosAsync(Usuario, d, h, ct),
        "indicadores" => await reportes.IndicadoresTramiteAsync(Usuario, d, h, ct),
        "transacciones" => await reportes.TransaccionesAsync(Usuario, d, h, ct: ct),
        _ => null
    };

    internal static (DateOnly, DateOnly) Rango(DateOnly? desde, DateOnly? hasta)
        => (desde ?? DateOnly.FromDateTime(DateTime.Today.AddDays(-30)), hasta ?? DateOnly.FromDateTime(DateTime.Today));
}

// =====================================================================================================
// Documentos: comprobante de cada transacción y documentos de las transacciones principales (todo el personal)
// =====================================================================================================

[Authorize(Policy = "Documentos")]
public sealed class DocumentosController(ServicioReportes reportes) : ControladorCore
{
    /// <summary>"Mis transacciones" (el auditor y el administrador ven las de todos y pueden filtrar por usuario).</summary>
    [HttpGet]
    public async Task<IActionResult> Index(DateOnly? desde, DateOnly? hasta, string? usuario, CancellationToken ct)
    {
        var (d, h) = ReportesController.Rango(desde, hasta);
        ViewBag.Desde = d;
        ViewBag.Hasta = h;
        ViewBag.UsuarioFiltro = usuario;
        return View(await reportes.TransaccionesAsync(Usuario, d, h, usuario: usuario, ct: ct));
    }

    /// <summary>
    /// "Mi actividad (PDF)": todo lo que hizo el usuario de la sesión (últimos 30 días por defecto).
    /// Está en el menú superior de todas las pantallas, para cualquier rol.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> MiActividad(DateOnly? desde, DateOnly? hasta, FormatoReporte formato, CancellationToken ct)
    {
        var (d, h) = ReportesController.Rango(desde, hasta);
        var datos = await reportes.TransaccionesAsync(Usuario, d, h, usuario: Usuario.Username, ct: ct);
        return Archivo(await reportes.ArchivoAsync(Usuario, "rptTransacciones", datos, formato,
            $"Actividad de {Usuario.NombreCompleto} ({Usuario.Username}) del {d:dd/MM/yyyy} al {h:dd/MM/yyyy}", Ip, ct));
    }

    /// <summary>Comprobante de una transacción (cualquier operación registrada en la bitácora).</summary>
    [HttpGet]
    public async Task<IActionResult> Comprobante(long id, FormatoReporte formato, CancellationToken ct)
        => Archivo(await reportes.ArchivoAsync(Usuario, "rptComprobanteTransaccion",
            await reportes.ComprobanteTransaccionAsync(Usuario, id, ct), formato, $"Transacción N.º {id}", Ip, ct));

    [HttpGet]
    public async Task<IActionResult> Acuse(string id, FormatoReporte formato, CancellationToken ct)
        => Archivo(await reportes.ArchivoAsync(Usuario, "rptAcuseSolicitud", await reportes.AcuseSolicitudAsync(Usuario, id, ct), formato, $"Trámite {id}", Ip, ct));

    [HttpGet]
    public async Task<IActionResult> Carnet(int id, FormatoReporte formato, CancellationToken ct)
        => Archivo(await reportes.ArchivoAsync(Usuario, "rptCarnetEmitido", await reportes.CarnetEmitidoAsync(Usuario, id, ct), formato, null, Ip, ct));

    [HttpGet]
    public async Task<IActionResult> Recibo(string id, FormatoReporte formato, CancellationToken ct)
        => Archivo(await reportes.ArchivoAsync(Usuario, "rptReciboPago", await reportes.ReciboPagoAsync(Usuario, id, ct), formato, $"Orden {id}", Ip, ct));

    [HttpGet]
    public async Task<IActionResult> Valoracion(int id, FormatoReporte formato, CancellationToken ct)
        => Archivo(await reportes.ArchivoAsync(Usuario, "rptInformeValoracion", await reportes.InformeValoracionAsync(Usuario, id, ct), formato,
            $"Evaluación N.º {id}", Ip, ct));

    /// <summary>PDF se muestra en el navegador (se puede imprimir); Excel se descarga.</summary>
    private FileContentResult Archivo(ArchivoReporte a)
        => a.TipoContenido == "application/pdf" ? File(a.Contenido, a.TipoContenido) : File(a.Contenido, a.TipoContenido, a.NombreArchivo);
}

// =====================================================================================================
// Integración: sus reportes RDLC se piden por la API de Integración (auditor y administrador)
// =====================================================================================================

[Authorize(Policy = "Integracion")]
public sealed class IntegracionController(ClienteReportesIntegracion integracion) : ControladorCore
{
    public static readonly IReadOnlyDictionary<string, string> Disponibles = new Dictionary<string, string>
    {
        ["transacciones"] = "Transacciones de interoperabilidad (bitácora)",
        ["pagos"] = "Pagos recibidos y aplicados",
        ["servicios"] = "Disponibilidad de servicios externos"
    };

    [HttpGet]
    public IActionResult Index()
    {
        var (d, h) = ReportesController.Rango(null, null);
        ViewBag.Desde = d;
        ViewBag.Hasta = h;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Descargar(string reporte, DateOnly? desde, DateOnly? hasta, FormatoReporte formato, CancellationToken ct)
    {
        var (d, h) = ReportesController.Rango(desde, hasta);
        return Archivo(await integracion.PeriodoAsync(reporte, d, h, formato.ToString(), Usuario.Username, ct));
    }

    [HttpGet]
    public async Task<IActionResult> Comprobante(long id, FormatoReporte formato, CancellationToken ct)
        => Archivo(await integracion.ComprobanteAsync(id, formato.ToString(), Usuario.Username, ct));

    private FileContentResult Archivo(ArchivoRemoto a)
        => a.TipoContenido == "application/pdf" ? File(a.Contenido, a.TipoContenido) : File(a.Contenido, a.TipoContenido, a.NombreArchivo);
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
// Administración: usuarios del personal, parámetros de negocio y tasas
// =====================================================================================================

public sealed record FormularioUsuario(int? UsuarioId, string Username, string? Contrasena, string NombreCompleto, string Correo,
    int RolId, string? Disciplina, int? CentroId, bool Activo);

[Authorize(Policy = "Administracion")]
public sealed class AdministracionController(ServicioUsuarios usuarios, ServicioCatalogos catalogos) : ControladorCore
{
    private ContextoAccion Accion => new(Usuario.Username, Ip);

    [HttpGet]
    public async Task<IActionResult> Index(string? texto, CancellationToken ct)
    {
        ViewBag.Texto = texto;
        return View(await usuarios.ListarAsync(texto, ct));
    }

    [HttpGet]
    public async Task<IActionResult> Editar(int? id, CancellationToken ct)
    {
        var f = new FormularioUsuario(null, "", null, "", "", 0, null, null, true);
        if (id is { } uid)
        {
            var u = await usuarios.ObtenerAsync(uid, ct);
            if (u is null) return NotFound();
            f = new FormularioUsuario(u.UsuarioId, u.Username, null, u.NombreCompleto, u.Correo, u.RolId, u.Disciplina, u.CentroId, u.Activo);
            ViewBag.Bloqueado = u.BloqueadoHasta > DateTimeOffset.Now;
        }
        await CargarListasAsync(ct);
        return View(f);
    }

    [HttpPost]
    public async Task<IActionResult> Editar(FormularioUsuario f, CancellationToken ct)
    {
        try
        {
            var id = await usuarios.GuardarAsync(new DatosUsuario(f.UsuarioId, f.Username ?? "", f.Contrasena, f.NombreCompleto ?? "",
                f.Correo ?? "", f.RolId, string.IsNullOrEmpty(f.Disciplina) ? null : f.Disciplina, f.CentroId, f.Activo), Accion, ct);
            TempData["Mensaje"] = f.UsuarioId is null ? $"Usuario {f.Username} creado." : $"Usuario {f.Username} actualizado.";
            return RedirectToAction(nameof(Editar), new { id });
        }
        catch (ReglaNegocioException ex)
        {
            ViewBag.Error = ex.Message;
            await CargarListasAsync(ct);
            return View(f with { Contrasena = null });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Restablecer(int id, string contrasena, CancellationToken ct)
    {
        await usuarios.CambiarContrasenaAsync(id, contrasena ?? "", Accion, ct);
        TempData["Mensaje"] = "Contraseña restablecida. Entréguela al usuario por un canal seguro.";
        return RedirectToAction(nameof(Editar), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Desbloquear(int id, CancellationToken ct)
    {
        await usuarios.DesbloquearAsync(id, Accion, ct);
        TempData["Mensaje"] = "Usuario desbloqueado.";
        return RedirectToAction(nameof(Editar), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Parametros(CancellationToken ct) => View(await catalogos.ListarParametrosAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Parametros(string clave, string valor, CancellationToken ct)
    {
        await catalogos.ActualizarParametroAsync(clave, valor ?? "", Accion, ct);
        TempData["Mensaje"] = $"Parámetro {clave} actualizado.";
        return RedirectToAction(nameof(Parametros));
    }

    [HttpGet]
    public async Task<IActionResult> Tasas(CancellationToken ct) => View(await catalogos.ListarTasasAsync(false, ct));

    [HttpPost]
    public async Task<IActionResult> Tasas(int tasaId, string codigo, string descripcion, decimal monto, decimal itbisPorcentaje,
        string resolucion, bool activa, CancellationToken ct)
    {
        await catalogos.GuardarTasaAsync(new Tasa(tasaId, codigo ?? "", descripcion ?? "", monto, itbisPorcentaje, resolucion ?? "", activa), Accion, ct);
        TempData["Mensaje"] = $"Tasa {codigo} guardada. Caja la recibirá en su próxima sincronización.";
        return RedirectToAction(nameof(Tasas));
    }

    private async Task CargarListasAsync(CancellationToken ct)
    {
        ViewBag.Roles = await usuarios.ListarRolesAsync(ct);
        ViewBag.Centros = await catalogos.ListarCentrosAsync(true, ct);
    }
}
