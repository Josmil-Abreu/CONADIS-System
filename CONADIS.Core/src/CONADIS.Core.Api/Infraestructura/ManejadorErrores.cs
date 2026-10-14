using CONADIS.Core.Negocio.Datos;
using CONADIS.Core.Negocio.Seguridad;
using CONADIS.Core.Negocio.Servicios;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Core.Api.Infraestructura;

/// <summary>
/// Convierte las excepciones de la lógica en respuestas HTTP con formato ProblemDetails (RFC 9457).
/// Así ningún controlador necesita try/catch, y el detalle técnico nunca sale de la API (RINV-10):
///   ReglaNegocioException          -> 422 con el mensaje del SP (pensado para el usuario)
///   AccesoDenegadoException        -> 403
///   BaseDatosNoDisponibleException -> 503 + Retry-After (Integración lo reintentará)
///   FirmaNoDisponibleException     -> 503 + Retry-After
///   cualquier otra                 -> 500 genérico; el detalle va al log con el id de correlación
/// </summary>
public sealed class ManejadorErrores(IProblemDetailsService problemas, ILogger<ManejadorErrores> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext contexto, Exception ex, CancellationToken ct)
    {
        var (estado, titulo, detalle) = ex switch
        {
            ReglaNegocioException r => (StatusCodes.Status422UnprocessableEntity, "Regla de negocio", r.Message),
            AccesoDenegadoException a => (StatusCodes.Status403Forbidden, "Acceso denegado", a.Message),
            BaseDatosNoDisponibleException => (StatusCodes.Status503ServiceUnavailable, "Servicio no disponible",
                "La base de datos del Core no está disponible. Intenta de nuevo en unos segundos."),
            FirmaNoDisponibleException f => (StatusCodes.Status503ServiceUnavailable, "Servicio no disponible", f.Message),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Petición no válida", "La petición no tiene el formato esperado."),
            _ => (StatusCodes.Status500InternalServerError, "Error interno",
                "Ocurrió un error inesperado. Consulta el log con el identificador de correlación.")
        };

        var correlacion = Correlacion.Obtener(contexto);
        if (estado >= 500 && estado != StatusCodes.Status503ServiceUnavailable)
            log.LogError(ex, "Error no controlado en {Metodo} {Ruta} (correlación {Correlacion}).",
                contexto.Request.Method, contexto.Request.Path, correlacion);

        contexto.Response.StatusCode = estado;
        if (estado == StatusCodes.Status503ServiceUnavailable) contexto.Response.Headers.RetryAfter = "30";

        var problema = new ProblemDetails { Status = estado, Title = titulo, Detail = detalle, Instance = contexto.Request.Path };
        if (ex is ReglaNegocioException regla) problema.Extensions["codigo"] = regla.Codigo;
        problema.Extensions["correlacion"] = correlacion;

        return await problemas.TryWriteAsync(new ProblemDetailsContext { HttpContext = contexto, ProblemDetails = problema });
    }
}