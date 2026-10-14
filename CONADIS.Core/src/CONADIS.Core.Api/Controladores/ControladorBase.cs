using CONADIS.Core.Negocio.Modelos;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Core.Api.Controladores;

/// <summary>
/// Base de todos los controladores. Todos exigen API key (política por defecto en Program.cs)
/// y responden JSON.
/// </summary>
[ApiController]
[Produces("application/json")]
public abstract class ControladorBase : ControllerBase
{
    /// <summary>Encabezado con la persona real detrás de la llamada (ej.: "ciudadano:00112345678", "cajero:jperez").</summary>
    public const string EncabezadoUsuarioFinal = "X-Usuario-Final";

    /// <summary>Encabezado con la IP del navegador o la caja que originó la operación.</summary>
    public const string EncabezadoIpOrigen = "X-Ip-Origen";

    /// <summary>
    /// Quién hace la operación, para la bitácora de auditoría: el sistema cliente autenticado
    /// más la persona que Integración indique. Ej.: "Integracion:ciudadano:00112345678".
    /// </summary>
    protected ContextoAccion Contexto()
    {
        var cliente = User.Identity?.Name ?? "desconocido";
        var final = Request.Headers[EncabezadoUsuarioFinal].ToString().Trim();
        var usuario = string.IsNullOrEmpty(final) ? cliente : $"{cliente}:{final}";
        if (usuario.Length > 100) usuario = usuario[..100];

        var ip = Request.Headers[EncabezadoIpOrigen].ToString().Trim();
        if (string.IsNullOrEmpty(ip)) ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        if (ip.Length > 45) ip = ip[..45];

        return new ContextoAccion(usuario, ip.Length == 0 ? null : ip);
    }
}