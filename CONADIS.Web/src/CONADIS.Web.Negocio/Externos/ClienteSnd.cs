using System.Net;
using System.Text;
using System.Text.Json;
using CONADIS.Web.Negocio.Datos;

namespace CONADIS.Web.Negocio.Externos;

/// <summary>Integración (el SND) no respondió o no está disponible. El portal muestra un mensaje amable.</summary>
public sealed class SndNoDisponibleException(string mensaje, Exception? interna = null) : Exception(mensaje, interna);

/// <summary>Quién hace la petición desde el portal (para la auditoría de punta a punta).</summary>
public sealed record ContextoPortal(string? Cedula = null, string? Ip = null, string? CorrelacionId = null);

/// <summary>
/// Cliente HTTP del módulo de Integración (api/v1/portal/...). El portal NUNCA habla directamente con el Core:
/// todo pasa por Integración, que valida con la JCE/MSP y aplica reintentos y cortacircuitos.
/// Traduce las respuestas:
///   2xx -> objeto;  404 -> null;  400/403/409/422 -> ReglaNegocioException con el mensaje para el ciudadano;
///   401, 5xx o sin conexión -> SndNoDisponibleException.
/// </summary>
public sealed class ClienteSnd(HttpClient http)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetAsync<T>(string ruta, ContextoPortal contexto, CancellationToken ct = default)
    {
        var (estado, cuerpo) = await EnviarAsync(HttpMethod.Get, ruta, null, contexto, null, ct);
        return estado == HttpStatusCode.NotFound ? default : Leer<T>(cuerpo);
    }

    public async Task<T> PostAsync<T>(string ruta, object cuerpo, ContextoPortal contexto, string? claveIdempotencia = null,
        CancellationToken ct = default)
    {
        var (estado, respuesta) = await EnviarAsync(HttpMethod.Post, ruta, cuerpo, contexto, claveIdempotencia, ct);
        if (estado == HttpStatusCode.NotFound) throw new ReglaNegocioException(0, "No se encontró lo solicitado.");
        return Leer<T>(respuesta) ?? throw new SndNoDisponibleException("Integración devolvió una respuesta vacía.");
    }

    public async Task EnviarSinRespuestaAsync(HttpMethod metodo, string ruta, object cuerpo, ContextoPortal contexto,
        string? claveIdempotencia = null, CancellationToken ct = default)
    {
        var (estado, _) = await EnviarAsync(metodo, ruta, cuerpo, contexto, claveIdempotencia, ct);
        if (estado == HttpStatusCode.NotFound) throw new ReglaNegocioException(0, "No se encontró el trámite.");
    }

    /// <summary>
    /// Reenvío desde la bandeja de salida: cuerpo JSON ya guardado. Devuelve la respuesta de Integración.
    /// Lanza SndNoDisponibleException (reintentar) o ReglaNegocioException (rechazo definitivo, incluido 404).
    /// </summary>
    public async Task<string> ReenviarAsync(string metodo, string ruta, string? json, ContextoPortal contexto, string? claveIdempotencia,
        CancellationToken ct = default)
    {
        var cuerpo = string.IsNullOrWhiteSpace(json) || metodo == "GET" ? null : System.Text.Json.Nodes.JsonNode.Parse(json);
        var (estado, respuesta) = await EnviarAsync(new HttpMethod(metodo), ruta, cuerpo, contexto, claveIdempotencia, ct);
        if (estado == HttpStatusCode.NotFound) throw new ReglaNegocioException(404, "El trámite o el recurso ya no existe.");
        return respuesta;
    }

    private async Task<(HttpStatusCode Estado, string Cuerpo)> EnviarAsync(HttpMethod metodo, string ruta, object? cuerpo,
        ContextoPortal contexto, string? claveIdempotencia, CancellationToken ct)
    {
        using var peticion = new HttpRequestMessage(metodo, ruta.TrimStart('/'));
        if (cuerpo is not null)
            peticion.Content = new StringContent(JsonSerializer.Serialize(cuerpo, Json), Encoding.UTF8, "application/json");
        if (!string.IsNullOrWhiteSpace(contexto.Cedula)) peticion.Headers.TryAddWithoutValidation("X-Usuario-Final", $"ciudadano:{contexto.Cedula}");
        if (!string.IsNullOrWhiteSpace(contexto.Ip)) peticion.Headers.TryAddWithoutValidation("X-Ip-Origen", contexto.Ip);
        if (!string.IsNullOrWhiteSpace(contexto.CorrelacionId)) peticion.Headers.TryAddWithoutValidation("X-Correlacion-Id", contexto.CorrelacionId);
        if (!string.IsNullOrWhiteSpace(claveIdempotencia)) peticion.Headers.TryAddWithoutValidation("Idempotency-Key", claveIdempotencia);

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await http.SendAsync(peticion, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new SndNoDisponibleException("El servicio no está disponible en este momento. Intente de nuevo en unos minutos.", ex);
        }

        using (respuesta)
        {
            var texto = await respuesta.Content.ReadAsStringAsync(ct);
            var codigo = (int)respuesta.StatusCode;

            if (respuesta.IsSuccessStatusCode || respuesta.StatusCode == HttpStatusCode.NotFound) return (respuesta.StatusCode, texto);
            if (respuesta.StatusCode == HttpStatusCode.Unauthorized)
                throw new SndNoDisponibleException("El portal no está autorizado ante Integración (revise Integracion:ApiKey).");
            if (codigo >= 500)
                throw new SndNoDisponibleException(Detalle(texto) ?? "El servicio no está disponible en este momento. Intente de nuevo en unos minutos.");

            // 400, 403, 409, 422...: mensaje pensado para el ciudadano
            throw new ReglaNegocioException(codigo, Detalle(texto) ?? "No se pudo completar la operación.");
        }
    }

    private static T? Leer<T>(string cuerpo)
        => string.IsNullOrWhiteSpace(cuerpo) ? default : JsonSerializer.Deserialize<T>(cuerpo, Json);

    /// <summary>Saca el "detail" de un ProblemDetails (o los errores de validación de un 400).</summary>
    internal static string? Detalle(string cuerpo)
    {
        try
        {
            using var doc = JsonDocument.Parse(cuerpo);
            var raiz = doc.RootElement;
            if (raiz.TryGetProperty("errors", out var errores) && errores.ValueKind == JsonValueKind.Object)
            {
                var mensajes = errores.EnumerateObject().SelectMany(e => e.Value.EnumerateArray().Select(v => v.GetString())).Where(m => m is not null);
                var lista = string.Join(" ", mensajes);
                if (lista.Length > 0) return lista;
            }
            return raiz.TryGetProperty("detail", out var d) ? d.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
