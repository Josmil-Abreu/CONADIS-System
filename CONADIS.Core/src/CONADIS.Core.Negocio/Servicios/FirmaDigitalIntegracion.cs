using System.Net.Http.Json;

namespace CONADIS.Core.Negocio.Servicios;

/// <summary>
/// Implementación real de <see cref="IFirmaDigital"/>: le pide la firma al módulo de Integración
/// (POST api/v1/firma). Integración es quien habla con el proveedor de firma acreditado (simulado).
/// El HttpClient llega ya configurado (dirección base, API key y tiempo de espera) desde Program.cs.
/// </summary>
public sealed class FirmaDigitalIntegracion(HttpClient http) : IFirmaDigital
{
    /// <summary>Ruta del servicio de firma dentro de la API de Integración.</summary>
    public const string Ruta = "api/v1/firma";

    public async Task<string> FirmarAsync(string contenido, CancellationToken ct = default)
    {
        using var respuesta = await http.PostAsJsonAsync(Ruta, new SolicitudFirma(contenido), ct);
        if (!respuesta.IsSuccessStatusCode)
            throw new HttpRequestException($"El servicio de firma respondió {(int)respuesta.StatusCode}.");

        var cuerpo = await respuesta.Content.ReadFromJsonAsync<RespuestaFirma>(ct);
        if (string.IsNullOrWhiteSpace(cuerpo?.Firma))
            throw new HttpRequestException("El servicio de firma devolvió una firma vacía.");

        return cuerpo.Firma;
    }

    // Contrato JSON con Integración: { "contenido": "..." }  ->  { "firma": "..." }
    internal sealed record SolicitudFirma(string Contenido);
    internal sealed record RespuestaFirma(string Firma);
}