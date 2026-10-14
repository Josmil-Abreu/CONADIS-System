using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Externos;
using CONADIS.Integracion.Negocio.Servicios;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Integracion.Api.Infraestructura;

/// <summary>
/// Convierte las excepciones en respuestas ProblemDetails (RFC 9457), sin detalles técnicos:
///   ReglaNegocioException          -> 422 (mensaje pensado para el usuario)
///   AccesoDenegadoException        -> 403
///   ServicioNoDisponibleException  -> 503 + Retry-After (Core, JCE, DGII... no responden)
///   BaseDatosNoDisponibleException -> 503
///   cualquier otra                 -> 500 genérico (el detalle va al log con el id de correlación)
/// </summary>
public sealed class ManejadorErrores(IProblemDetailsService problemas, ILogger<ManejadorErrores> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext contexto, Exception ex, CancellationToken ct)
    {
        var (estado, titulo, detalle) = ex switch
        {
            ReglaNegocioException r => (422, "Regla de negocio", r.Message),
            AccesoDenegadoException a => (403, "Acceso denegado", a.Message),
            ServicioNoDisponibleException s => (503, "Servicio no disponible", s.Message),
            CoreNoDisponibleException => (503, "Servicio no disponible", "El sistema central no está disponible en este momento."),
            BaseDatosNoDisponibleException => (503, "Servicio no disponible", "La base de datos de Integración no está disponible."),
            BadHttpRequestException => (400, "Petición no válida", "La petición no tiene el formato esperado."),
            _ => (500, "Error interno", "Ocurrió un error inesperado. Consulte el log con el identificador de correlación.")
        };

        var correlacion = Correlacion.Obtener(contexto);
        if (estado == 500)
            log.LogError(ex, "Error no controlado en {Metodo} {Ruta} (correlación {Correlacion}).",
                contexto.Request.Method, contexto.Request.Path, correlacion);
        else if (estado == 503)
            log.LogWarning("{Ruta}: {Mensaje} (correlación {Correlacion}).", contexto.Request.Path, ex.Message, correlacion);

        contexto.Response.StatusCode = estado;
        if (estado == 503) contexto.Response.Headers.RetryAfter = "30";

        var problema = new ProblemDetails { Status = estado, Title = titulo, Detail = detalle, Instance = contexto.Request.Path };
        if (ex is ReglaNegocioException regla) problema.Extensions["codigo"] = regla.Codigo;
        if (ex is ServicioNoDisponibleException s2) problema.Extensions["servicio"] = s2.Servicio;
        problema.Extensions["correlacion"] = correlacion;

        return await problemas.TryWriteAsync(new ProblemDetailsContext { HttpContext = contexto, ProblemDetails = problema });
    }
}
