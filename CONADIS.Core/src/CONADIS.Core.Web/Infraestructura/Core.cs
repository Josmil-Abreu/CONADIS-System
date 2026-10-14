using System.Security.Claims;
using System.Text.Json;
using CONADIS.Core.Negocio.Datos;
using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Seguridad;
using CONADIS.Core.Negocio.Servicios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace CONADIS.Core.Web.Infraestructura;

/// <summary>Una opción del menú: área (= nombre de la política), texto, controlador y roles que la ven.</summary>
public sealed record OpcionMenu(string Area, string Texto, string Controlador, string Descripcion, string[] Roles);

/// <summary>
/// Menú por rol (RNF-SEG-05). Lo usan el layout (qué se muestra) y Program.cs (qué se permite):
/// si un rol no ve una opción, tampoco puede entrar escribiendo la URL.
/// </summary>
public static class Menu
{
    public static readonly IReadOnlyList<OpcionMenu> Opciones =
    [
        new("Expedientes", "Expedientes", "Expedientes", "Buscar expedientes y ver el Expediente Único.",
            [Roles.AnalistaRegistro, Roles.SupervisorRegistro, Roles.Administrador, Roles.Comite, Roles.Auditor, Roles.Evaluador]),
        new("Valoracion", "Mi agenda de valoración", "Valoracion", "Citas del día en su centro y valoraciones CIF.",
            [Roles.Evaluador]),
        new("Comite", "Comité", "Comite", "Dictámenes pendientes de voto y apelaciones.",
            [Roles.Comite]),
        new("Reportes", "Reportes", "Reportes", "Reportes institucionales por rango de fechas.",
            [Roles.Administrador, Roles.Auditor, Roles.AnalistaRegistro, Roles.SupervisorRegistro]),
        new("Auditoria", "Auditoría", "Auditoria", "Bitácora de acciones y verificación de integridad.",
            [Roles.Auditor, Roles.Administrador]),
        new("Logs", "Logs del sistema", "LogsSistema", "Registros técnicos (Log4Net): errores, advertencias e información de los módulos.",
            [Roles.Auditor, Roles.Administrador]),
        new("Administracion", "Administración", "Administracion", "Usuarios, parámetros y tasas.",
            [Roles.Administrador]),
        new("Integracion", "Integración", "Integracion", "Transacciones de interoperabilidad, pagos recibidos y disponibilidad de servicios.",
            [Roles.Auditor, Roles.Administrador]),
        new("Documentos", "Mis transacciones", "Documentos", "Todo lo que usted registró, con el comprobante de cada transacción.",
            [Roles.AnalistaRegistro, Roles.SupervisorRegistro, Roles.Evaluador, Roles.Comite, Roles.Auditor, Roles.Administrador])
    ];

    public static IEnumerable<(string Area, string[] Roles)> Permisos => Opciones.Select(o => (o.Area, o.Roles));

    public static IEnumerable<OpcionMenu> Para(ClaimsPrincipal usuario) => Opciones.Where(o => o.Roles.Any(usuario.IsInRole));

    /// <summary>Nombre del rol para mostrar.</summary>
    public static string TextoRol(string rol) => rol switch
    {
        Roles.AnalistaRegistro => "Analista de registro",
        Roles.SupervisorRegistro => "Supervisor de registro",
        Roles.Evaluador => "Evaluador",
        Roles.Comite => "Miembro del comité",
        Roles.Auditor => "Auditor",
        Roles.Administrador => "Administrador",
        _ => rol
    };
}

/// <summary>El usuario del personal viaja en la cookie (claim "usuario" con el UsuarioSesion en JSON).</summary>
public static class SesionPersonal
{
    public const string ClaimUsuario = "usuario";
    public const string ClaimInstancia = "instancia";

    /// <summary>
    /// Identificador de esta ejecución del sistema interno. Cada cookie lleva el de la ejecución que la emitió:
    /// al reiniciar el Core Web, las sesiones anteriores dejan de valer (aunque el navegador restaure la cookie).
    /// </summary>
    public static readonly string Instancia = Guid.NewGuid().ToString("N");

    public static ClaimsPrincipal Principal(UsuarioSesion u, string esquema) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, u.UsuarioId.ToString()),
        new Claim(ClaimTypes.Name, u.NombreCompleto),
        new Claim(ClaimTypes.Role, u.Rol),
        new Claim(ClaimUsuario, JsonSerializer.Serialize(u)),
        new Claim(ClaimInstancia, Instancia)
    ], esquema));

    public static UsuarioSesion? Usuario(this ClaimsPrincipal p)
        => p.FindFirstValue(ClaimUsuario) is { Length: > 0 } json ? JsonSerializer.Deserialize<UsuarioSesion>(json) : null;
}

/// <summary>Base de los controladores internos: usuario de la sesión e IP.</summary>
public abstract class ControladorCore : Controller
{
    protected UsuarioSesion Usuario => User.Usuario()
        ?? throw new InvalidOperationException("La sesión no tiene los datos del usuario. Inicie sesión de nuevo.");

    protected string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();
}

/// <summary>
/// Errores esperados → mensaje en la pantalla anterior (TempData["Error"]) en vez de una página de error:
/// reglas de negocio de los SP, rol sin permiso (defensa en profundidad de los servicios) y base de datos caída.
/// </summary>
public sealed class FiltroErroresCore(ITempDataDictionaryFactory tempData, ILogger<FiltroErroresCore> log) : IExceptionFilter
{
    public void OnException(ExceptionContext contexto)
    {
        var mensaje = contexto.Exception switch
        {
            ReglaNegocioException r => r.Message,
            AccesoDenegadoException a => a.Message,
            FirmaNoDisponibleException f => f.Message,
            BaseDatosNoDisponibleException => "La base de datos no está disponible en este momento. Intente de nuevo en unos minutos.",
            _ => null
        };
        if (mensaje is null) return;

        log.LogWarning("Error controlado en {Ruta}: {Mensaje}", contexto.HttpContext.Request.Path, contexto.Exception.Message);
        tempData.GetTempData(contexto.HttpContext)["Error"] = mensaje;

        var anterior = contexto.HttpContext.Request.Headers.Referer.ToString();
        contexto.Result = new RedirectResult(Uri.TryCreate(anterior, UriKind.Absolute, out var u)
                                             && u.Host == contexto.HttpContext.Request.Host.Host ? u.PathAndQuery : "/");
        contexto.ExceptionHandled = true;
    }
}
