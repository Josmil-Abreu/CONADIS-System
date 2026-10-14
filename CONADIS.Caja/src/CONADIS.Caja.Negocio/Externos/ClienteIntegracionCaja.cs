using System.Net;
using System.Text;
using System.Text.Json;
using CONADIS.Caja.Negocio.Datos;
using CONADIS.Caja.Negocio.Modelos;

namespace CONADIS.Caja.Negocio.Externos;

/// <summary>
/// No hay comunicación con Integración (red caída, servicio apagado...). La Caja NO se detiene:
/// sigue cobrando con su base local y sincroniza cuando vuelva la conexión.
/// </summary>
public sealed class SinConexionException(string mensaje, Exception? interna = null) : Exception(mensaje, interna);

/// <summary>Pago que la Caja entrega a Integración (misma forma que el NuevoPago del Core).</summary>
public sealed record PagoCaja(Guid TransaccionId, string Origen, string NumeroComprobante, string TasaCodigo, int Cantidad, decimal Total,
    string MedioPago, string Documento, string NombrePagador, string? NumeroOrden, string? Referencia, string? Caja, string? Cajero,
    DateTimeOffset FechaPago);

public sealed record RespuestaRecepcion(Guid TransaccionId, string Estado, bool EsNuevo, DateTimeOffset FechaRecepcion, string? Detalle);

public sealed record ComprobanteFiscal(string Encf, string TipoEcf, string RncEmisor, string? DocumentoComprador, decimal Total,
    decimal Itbis, DateTimeOffset FechaEmision);

public sealed record RespuestaEcf(string Estado, string? CodigoSeguridad, string? TrackId, string? Mensaje);

internal sealed record TasaCore(string Codigo, string Descripcion, decimal Monto, decimal ItbisPorcentaje, bool Activa);

/// <summary>
/// Cliente HTTP de Integración (api/v1/caja/...), identificado con la API key de la Caja.
/// La Caja nunca habla con el Core directamente.
/// </summary>
public sealed class ClienteIntegracionCaja(HttpClient http)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal Task<List<TasaCore>?> TasasAsync(CancellationToken ct = default) => GetAsync<List<TasaCore>>("api/v1/caja/tasas", ct);

    public Task<OrdenParaCobro?> OrdenAsync(string numeroOrden, CancellationToken ct = default)
        => GetAsync<OrdenParaCobro>($"api/v1/caja/ordenes/{Uri.EscapeDataString(numeroOrden.Trim().ToUpperInvariant())}", ct);

    public Task<RespuestaRnc?> RncAsync(string rnc, CancellationToken ct = default)
        => GetAsync<RespuestaRnc>($"api/v1/caja/rnc/{Uri.EscapeDataString(rnc)}", ct);

    public async Task<RespuestaRecepcion> EnviarPagoAsync(PagoCaja pago, string? usuario, CancellationToken ct = default)
        => await EnviarAsync<RespuestaRecepcion>(HttpMethod.Post, "api/v1/caja/pagos", pago, usuario, ct)
           ?? throw new SinConexionException("Integración no devolvió respuesta al pago.");

    public Task<RespuestaRecepcion?> EstadoPagoAsync(Guid transaccionId, CancellationToken ct = default)
        => GetAsync<RespuestaRecepcion>($"api/v1/caja/pagos/{transaccionId}", ct);

    public async Task<RespuestaEcf> EnviarEcfAsync(ComprobanteFiscal comprobante, CancellationToken ct = default)
        => await EnviarAsync<RespuestaEcf>(HttpMethod.Post, "api/v1/caja/ecf", comprobante, null, ct)
           ?? throw new SinConexionException("Integración no devolvió respuesta al e-CF.");

    private Task<T?> GetAsync<T>(string ruta, CancellationToken ct) => EnviarAsync<T>(HttpMethod.Get, ruta, null, null, ct);

    private async Task<T?> EnviarAsync<T>(HttpMethod metodo, string ruta, object? cuerpo, string? usuario, CancellationToken ct)
    {
        using var peticion = new HttpRequestMessage(metodo, ruta);
        if (cuerpo is not null) peticion.Content = new StringContent(JsonSerializer.Serialize(cuerpo, Json), Encoding.UTF8, "application/json");
        if (!string.IsNullOrWhiteSpace(usuario)) peticion.Headers.TryAddWithoutValidation("X-Usuario-Final", $"cajero:{usuario}");

        HttpResponseMessage r;
        try
        {
            r = await http.SendAsync(peticion, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new SinConexionException("Sin conexión con Integración.", ex);
        }

        using (r)
        {
            var texto = await r.Content.ReadAsStringAsync(ct);
            if (r.StatusCode == HttpStatusCode.NotFound) return default;
            if (r.IsSuccessStatusCode) return string.IsNullOrWhiteSpace(texto) ? default : JsonSerializer.Deserialize<T>(texto, Json);
            if (r.StatusCode == HttpStatusCode.Unauthorized || (int)r.StatusCode >= 500)
                throw new SinConexionException($"Integración respondió {(int)r.StatusCode}: {Detalle(texto)}");
            throw new ReglaNegocioException((int)r.StatusCode, Detalle(texto) ?? "Integración rechazó la operación.");
        }
    }

    private static string? Detalle(string cuerpo)
    {
        try
        {
            using var d = JsonDocument.Parse(cuerpo);
            return d.RootElement.TryGetProperty("detail", out var x) ? x.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
