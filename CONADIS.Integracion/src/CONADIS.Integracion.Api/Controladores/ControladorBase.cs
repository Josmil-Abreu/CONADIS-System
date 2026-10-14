using CONADIS.Integracion.Api.Infraestructura;
using CONADIS.Integracion.Api.Seguridad;
using CONADIS.Integracion.Negocio.Externos;
using CONADIS.Integracion.Negocio.Modelos;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Integracion.Api.Controladores;

/// <summary>Base de los controladores: contexto de la llamada y reenvío de respuestas del Core.</summary>
[ApiController]
[Produces("application/json")]
public abstract class ControladorBase : ControllerBase
{
    /// <summary>Persona real detrás de la llamada (ej.: "ciudadano:00112345678", "cajero:jperez").</summary>
    public const string EncabezadoUsuarioFinal = "X-Usuario-Final";

    /// <summary>IP del navegador o de la caja que originó la operación.</summary>
    public const string EncabezadoIpOrigen = "X-Ip-Origen";

    protected ClienteAutenticado Cliente => ManejadorClientes.Cliente(HttpContext);

    /// <summary>Quién llama, para la bitácora y para que el Core audite a la persona real.</summary>
    protected ContextoLlamada Contexto(string? proposito = null)
    {
        var usuario = Request.Headers[EncabezadoUsuarioFinal].ToString().Trim();
        var ip = Request.Headers[EncabezadoIpOrigen].ToString().Trim();
        if (string.IsNullOrEmpty(ip)) ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        return new ContextoLlamada(
            User.Identity?.Name ?? "desconocido",
            usuario.Length == 0 ? null : usuario[..Math.Min(usuario.Length, 80)],
            proposito,
            Correlacion.Obtener(HttpContext),
            ip.Length == 0 ? null : ip[..Math.Min(ip.Length, 45)]);
    }

    /// <summary>Devuelve al canal la respuesta del Core tal cual (código, cuerpo y tipo de contenido).</summary>
    protected static IActionResult Reenviar(RespuestaCore r)
        => r.Estado == StatusCodes.Status204NoContent
            ? new StatusCodeResult(204)
            : new ContentResult { StatusCode = r.Estado, Content = r.Cuerpo, ContentType = r.TipoContenido };
}
