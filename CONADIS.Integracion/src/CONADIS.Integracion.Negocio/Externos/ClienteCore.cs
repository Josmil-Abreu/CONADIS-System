using System.Net;
using System.Text;
using System.Text.Json;
using CONADIS.Integracion.Negocio.Modelos;

namespace CONADIS.Integracion.Negocio.Externos;

/// <summary>Respuesta HTTP del Core tal cual (Integración puede reenviarla al canal sin transformarla).</summary>
public sealed record RespuestaCore(int Estado, string Cuerpo, string TipoContenido)
{
    public bool EsExito => Estado is >= 200 and < 300;
    public T? Leer<T>() => string.IsNullOrWhiteSpace(Cuerpo) ? default : JsonSerializer.Deserialize<T>(Cuerpo, ClienteCore.Json);
}

/// <summary>El Core no respondió o respondió con un error del servidor (5xx).</summary>
public sealed class CoreNoDisponibleException(string mensaje, Exception? interna = null) : Exception(mensaje, interna);

/// <summary>
/// Cliente HTTP de la API REST del Core (Paso 6). Integración se identifica con su API key
/// (configurada en el HttpClient) y le pasa al Core quién es la persona real detrás de cada llamada.
/// Las respuestas 4xx se devuelven normalmente (son respuestas válidas del Core: 404, 422...);
/// los errores de red y 5xx lanzan <see cref="CoreNoDisponibleException"/> para que la resiliencia reintente.
/// </summary>
public sealed class ClienteCore(HttpClient http)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<RespuestaCore> EnviarAsync(HttpMethod metodo, string ruta, string? cuerpoJson, ContextoLlamada contexto,
        CancellationToken ct = default)
    {
        using var peticion = new HttpRequestMessage(metodo, ruta.TrimStart('/'));
        if (cuerpoJson is not null) peticion.Content = new StringContent(cuerpoJson, Encoding.UTF8, "application/json");

        var usuarioFinal = string.IsNullOrWhiteSpace(contexto.Usuario) ? contexto.Cliente : $"{contexto.Cliente}/{contexto.Usuario}";
        peticion.Headers.TryAddWithoutValidation("X-Usuario-Final", usuarioFinal);
        if (!string.IsNullOrWhiteSpace(contexto.Ip)) peticion.Headers.TryAddWithoutValidation("X-Ip-Origen", contexto.Ip);
        if (!string.IsNullOrWhiteSpace(contexto.CorrelacionId)) peticion.Headers.TryAddWithoutValidation("X-Correlacion-Id", contexto.CorrelacionId);

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await http.SendAsync(peticion, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new CoreNoDisponibleException($"No se pudo conectar con el Core ({http.BaseAddress}): {Causa(ex)}", ex);
        }

        using (respuesta)
        {
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
            var tipo = respuesta.Content.Headers.ContentType?.ToString() ?? "application/json";
            if ((int)respuesta.StatusCode >= 500)
                throw new CoreNoDisponibleException($"El Core respondió {(int)respuesta.StatusCode}.");
            if (respuesta.StatusCode == HttpStatusCode.Unauthorized)
                throw new CoreNoDisponibleException("El Core rechazó la API key de Integración (revise Core:ApiKey).");
            return new RespuestaCore((int)respuesta.StatusCode, cuerpo, tipo);
        }
    }

    public Task<RespuestaCore> GetAsync(string ruta, ContextoLlamada contexto, CancellationToken ct = default)
        => EnviarAsync(HttpMethod.Get, ruta, null, contexto, ct);

    public Task<RespuestaCore> PostAsync(string ruta, object? cuerpo, ContextoLlamada contexto, CancellationToken ct = default)
        => EnviarAsync(HttpMethod.Post, ruta, cuerpo is null ? null : cuerpo as string ?? JsonSerializer.Serialize(cuerpo, Json), contexto, ct);

    public Task<RespuestaCore> PutAsync(string ruta, object? cuerpo, ContextoLlamada contexto, CancellationToken ct = default)
        => EnviarAsync(HttpMethod.Put, ruta, cuerpo is null ? null : cuerpo as string ?? JsonSerializer.Serialize(cuerpo, Json), contexto, ct);

    /// <summary>GET /api/v1/salud: true si el Core y su base de datos responden.</summary>
    public async Task<bool> EstaDisponibleAsync(CancellationToken ct = default)
    {
        try
        {
            using var r = await http.GetAsync("api/v1/salud", ct);
            return r.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>Lee la respuesta como JSON, o lanza si no fue exitosa.</summary>
    public static T LeerOFallar<T>(RespuestaCore r, string operacion)
    {
        if (!r.EsExito) throw new InvalidOperationException($"El Core respondió {r.Estado} a {operacion}: {r.Cuerpo}");
        return r.Leer<T>() ?? throw new InvalidOperationException($"El Core devolvió una respuesta vacía a {operacion}.");
    }

    /// <summary>Mensaje de la excepción más interna (p. ej. el error de certificado SSL o "conexión denegada").</summary>
    private static string Causa(Exception ex)
    {
        var e = ex;
        while (e.InnerException is not null) e = e.InnerException;
        return e == ex ? ex.Message : $"{ex.Message} -> {e.Message}";
    }
}
