using System.Security.Claims;
using System.Text.Json;
using CONADIS.Web.Negocio.Datos;
using CONADIS.Web.Negocio.Externos;
using CONADIS.Web.Negocio.Modelos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace CONADIS.Web.Infraestructura;

/// <summary>
/// La cuenta del ciudadano viaja en la cookie de autenticación (claim "cuenta" con la CuentaSesion en JSON).
/// Los servicios del portal reciben siempre esta cuenta, nunca una cédula escrita en la URL.
/// </summary>
public static class SesionCiudadano
{
    public const string ClaimCuenta = "cuenta";

    public static IEnumerable<Claim> Claims(CuentaSesion c) =>
    [
        new(ClaimTypes.NameIdentifier, c.CuentaId.ToString()),
        new(ClaimTypes.Name, c.NombreCompleto),
        new(ClaimTypes.Email, c.Correo),
        new(ClaimTypes.Role, "Ciudadano"),
        new(Extensions.ClaimsExtensions.ClaimCedula, c.Cedula),
        new(ClaimCuenta, JsonSerializer.Serialize(c))
    ];

    public static CuentaSesion? Cuenta(this ClaimsPrincipal usuario)
    {
        var json = usuario.FindFirstValue(ClaimCuenta);
        return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<CuentaSesion>(json);
    }
}

/// <summary>Base de los controladores del portal: cuenta de la sesión e IP del ciudadano.</summary>
public abstract class ControladorPortal : Controller
{
    /// <summary>Cuenta del ciudadano autenticado (los controladores con [Authorize] siempre la tienen).</summary>
    protected CuentaSesion Cuenta => User.Cuenta()
        ?? throw new InvalidOperationException("La sesión no tiene los datos de la cuenta. Inicie sesión de nuevo.");

    protected string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>Clave de idempotencia estable por formulario (evita dobles envíos al recargar o pulsar dos veces).</summary>
    protected string ClaveIdempotencia(string operacion, string referencia) => $"{operacion}-{Cuenta.CuentaId}-{referencia}";
}

/// <summary>
/// Errores esperados que no deben mostrar una página de error:
///  - ReglaNegocioException (validación del Core, de Integración o del portal) → mensaje en la misma pantalla.
///  - SndNoDisponibleException / BaseDatosNoDisponibleException → aviso de servicio no disponible.
/// Se vuelve a la página anterior con el mensaje en TempData["Error"] (lo muestra _Mensajes.cshtml).
/// </summary>
public sealed class FiltroErroresPortal(ITempDataDictionaryFactory tempData, ILogger<FiltroErroresPortal> log) : IExceptionFilter
{
    public void OnException(ExceptionContext contexto)
    {
        var mensaje = contexto.Exception switch
        {
            ReglaNegocioException r => r.Message,
            SndNoDisponibleException or BaseDatosNoDisponibleException =>
                "El servicio no está disponible en este momento. Intente de nuevo en unos minutos.",
            _ => null
        };
        if (mensaje is null) return;   // error inesperado: lo maneja UseExceptionHandler

        log.LogWarning("Error controlado en {Ruta}: {Mensaje}", contexto.HttpContext.Request.Path, contexto.Exception.Message);
        tempData.GetTempData(contexto.HttpContext)["Error"] = mensaje;

        var anterior = contexto.HttpContext.Request.Headers.Referer.ToString();
        contexto.Result = new RedirectResult(Uri.TryCreate(anterior, UriKind.Absolute, out var u)
                                             && u.Host == contexto.HttpContext.Request.Host.Host ? u.PathAndQuery : "/");
        contexto.ExceptionHandled = true;
    }
}
