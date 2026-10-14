using System.Security.Cryptography;
using System.Text.Json;
using CONADIS.Integracion.Api.Seguridad;
using CONADIS.Integracion.Negocio.Modelos;
using CONADIS.Integracion.Negocio.Repositorios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CONADIS.Integracion.Api.Infraestructura;

/// <summary>
/// RIC-05: un POST marcado con [Idempotente] acepta el encabezado Idempotency-Key.
///   - Primera vez: se ejecuta y se guarda la respuesta.
///   - Repetido con el mismo cuerpo: se devuelve la respuesta guardada SIN volver a ejecutar
///     (el ciudadano que pulsa "Enviar" dos veces no crea dos solicitudes).
///   - Repetido mientras el primero aún se procesa: 409.
///   - Misma clave con otro cuerpo: 422.
/// Si la operación falla con un error del servidor (5xx), la clave se libera para poder reintentar.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotenteAttribute : Attribute, IFilterFactory
{
    public const string Encabezado = "Idempotency-Key";
    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider servicios)
        => new FiltroIdempotencia(servicios.GetRequiredService<RepositorioIdempotencia>(), servicios.GetRequiredService<ILogger<FiltroIdempotencia>>());
}

public sealed class FiltroIdempotencia(RepositorioIdempotencia repositorio, ILogger<FiltroIdempotencia> log) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext contexto, ResourceExecutionDelegate siguiente)
    {
        var http = contexto.HttpContext;
        var clave = http.Request.Headers[IdempotenteAttribute.Encabezado].ToString().Trim();
        if (string.IsNullOrEmpty(clave))
        {
            await siguiente();   // el encabezado es opcional
            return;
        }

        if (clave.Length > 100)
        {
            contexto.Result = Problema(400, "La Idempotency-Key no puede tener más de 100 caracteres.");
            return;
        }

        // Huella del cuerpo: misma clave con otro contenido = conflicto
        http.Request.EnableBuffering();
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(http.Request.Body, http.RequestAborted));
        http.Request.Body.Position = 0;

        var cliente = ManejadorClientes.Cliente(http);
        var ruta = $"{http.Request.Method} {http.Request.Path}";
        var inicio = await repositorio.IniciarAsync(cliente.ClienteId, clave, ruta, hash, http.RequestAborted);

        switch (inicio.Resultado)
        {
            case ResultadosIdempotencia.Repetida:
                log.LogInformation("Idempotency-Key repetida ({Cliente}, {Ruta}): se devuelve la respuesta guardada.", cliente.Nombre, ruta);
                http.Response.Headers["Idempotent-Replayed"] = "true";
                contexto.Result = new ContentResult
                {
                    StatusCode = inicio.CodigoHttp ?? 200, Content = inicio.Respuesta ?? string.Empty, ContentType = "application/json"
                };
                return;
            case ResultadosIdempotencia.EnProceso:
                contexto.Result = Problema(409, "Una petición con la misma Idempotency-Key todavía se está procesando.");
                return;
            case ResultadosIdempotencia.Conflicto:
                contexto.Result = Problema(422, "La Idempotency-Key ya se usó con otro contenido o en otra operación.");
                return;
        }

        ResourceExecutedContext ejecutado;
        try
        {
            ejecutado = await siguiente();
        }
        catch
        {
            await LiberarAsync(cliente.ClienteId, clave);
            throw;
        }

        var (estado, cuerpo) = Extraer(ejecutado.Result);
        if (ejecutado.Exception is not null && !ejecutado.ExceptionHandled || estado >= 500)
            await LiberarAsync(cliente.ClienteId, clave);
        else
            await repositorio.CompletarAsync(cliente.ClienteId, clave, estado, cuerpo, CancellationToken.None);
    }

    private async Task LiberarAsync(int clienteId, string clave)
    {
        try { await repositorio.LiberarAsync(clienteId, clave, CancellationToken.None); }
        catch (Exception ex) { log.LogWarning(ex, "No se pudo liberar la Idempotency-Key {Clave}.", clave); }
    }

    private static (int Estado, string? Cuerpo) Extraer(IActionResult? resultado) => resultado switch
    {
        ContentResult c => (c.StatusCode ?? 200, c.Content),
        ObjectResult o => (o.StatusCode ?? 200, o.Value is null ? null : JsonSerializer.Serialize(o.Value, o.Value.GetType(), JsonSerializerOptions.Web)),
        StatusCodeResult s => (s.StatusCode, null),
        _ => (500, null)
    };

    private static ObjectResult Problema(int estado, string detalle)
        => new(new ProblemDetails { Status = estado, Title = "Idempotency-Key", Detail = detalle }) { StatusCode = estado };
}
