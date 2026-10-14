namespace CONADIS.Integration.HttpClients.Implementations;

using CONADIS.Integration.Configuration;
using CONADIS.Integration.Exceptions;
using CONADIS.Integration.HttpClients.Interfaces;
using CONADIS.Integration.Models.Responses;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>Implementación real del cliente HTTP para la JCE</summary>
public class JceApiClient : IJceApiClient
{
    private readonly HttpClient _httpClient;
    private readonly JceSettings _settings;
    private readonly ILogger<JceApiClient> _logger;

    public JceApiClient(
        HttpClient httpClient,
        IOptions<IntegrationSettings> options,
        ILogger<JceApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value.Jce;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_settings.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
        _httpClient.DefaultRequestHeaders.Add("X-API-Key", _settings.ApiKey);
    }

    public async Task<JceValidationResponse> ValidateCedulaAsync(string cedula, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Validando cédula contra JCE: {Cedula}", cedula);

            var endpoint = ApiEndpoints.Jce.ValidateCedula.Replace("{cedula}", cedula);
            var response = await _httpClient.GetAsync(endpoint, cancellationToken: ct);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("Cédula no encontrada en JCE: {Cedula}", cedula);
                    return new JceValidationResponse
                    {
                        IsValid = false,
                        Cedula = cedula,
                        ErrorMessage = "Cédula no encontrada"
                    };
                }

                throw new IdentityValidationException(
                    $"Error al validar cédula: {response.StatusCode}",
                    cedula);
            }

            var result = await response.Content.ReadFromJsonAsync<JceValidationResponse>(cancellationToken: ct)
                ?? throw new IdentityValidationException("Respuesta vacía del servidor", cedula);

            _logger.LogInformation("Validación completada: Cedula={Cedula}, IsValid={IsValid}",
                cedula, result.IsValid);

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error de conexión al validar cédula");
            throw new IdentityValidationException("Error de conexión con la JCE", cedula, ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout al validar cédula");
            throw new IdentityValidationException("Timeout en la JCE", cedula, ex);
        }
    }

    public async Task<JceCitizenInfoResponse> GetCitizenInfoAsync(string cedula, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Obteniendo información del ciudadano: {Cedula}", cedula);

            var endpoint = ApiEndpoints.Jce.GetCitizenInfo.Replace("{cedula}", cedula);
            var response = await _httpClient.GetAsync(endpoint, cancellationToken: ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new IdentityValidationException(
                    $"Error al obtener información: {response.StatusCode}",
                    cedula);
            }

            var result = await response.Content.ReadFromJsonAsync<JceCitizenInfoResponse>(cancellationToken: ct)
                ?? throw new IdentityValidationException("Respuesta vacía del servidor", cedula);

            _logger.LogInformation("Información obtenida: Cedula={Cedula}, FullName={FullName}",
                cedula, result.FullName);

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error de conexión al obtener información");
            throw new IdentityValidationException("Error de conexión con la JCE", cedula, ex);
        }
    }

    public async Task<JceElectoralStatusResponse> GetElectoralStatusAsync(string cedula, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Obteniendo estado electoral: {Cedula}", cedula);

            var endpoint = ApiEndpoints.Jce.GetElectoralStatus.Replace("{cedula}", cedula);
            var response = await _httpClient.GetAsync(endpoint, cancellationToken: ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new IdentityValidationException(
                    $"Error al obtener estado electoral: {response.StatusCode}",
                    cedula);
            }

            var result = await response.Content.ReadFromJsonAsync<JceElectoralStatusResponse>(cancellationToken: ct)
                ?? throw new IdentityValidationException("Respuesta vacía del servidor", cedula);

            _logger.LogInformation("Estado electoral obtenido: Cedula={Cedula}, Status={Status}",
                cedula, result.Status);

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error de conexión al obtener estado electoral");
            throw new IdentityValidationException("Error de conexión con la JCE", cedula, ex);
        }
    }
}