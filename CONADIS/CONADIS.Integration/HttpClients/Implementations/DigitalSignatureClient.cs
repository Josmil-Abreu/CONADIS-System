namespace CONADIS.Integration.HttpClients.Implementations;

using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using CONADIS.Integration.Configuration;
using CONADIS.Integration.Exceptions;
using CONADIS.Integration.HttpClients.Interfaces;
using CONADIS.Integration.Models.Requests;
using CONADIS.Integration.Models.Responses;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>Implementación real del cliente HTTP para firmas digitales (DGII)</summary>
public class DigitalSignatureClient : IDigitalSignatureClient
{
    private readonly HttpClient _httpClient;
    private readonly DigitalSignatureSettings _settings;
    private readonly ILogger<DigitalSignatureClient> _logger;

    public DigitalSignatureClient(
        HttpClient httpClient,
        IOptions<IntegrationSettings> options,
        ILogger<DigitalSignatureClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value.DigitalSignature;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_settings.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
        _httpClient.DefaultRequestHeaders.Add("X-API-Key", _settings.ApiKey);
    }

    public async Task<SignatureResponse> GenerateSignatureAsync(SignatureRequest request, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Generando firma digital para certificado: {CertificateId}", request.CertificateId);

            var endpoint = ApiEndpoints.DigitalSignature.GenerateSignature;
            var response = await _httpClient.PostAsJsonAsync(endpoint, request, cancellationToken: ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new SignatureException(
                    $"Error al generar firma: {response.StatusCode}",
                    request.CertificateId);
            }

            var result = await response.Content.ReadAsAsync<SignatureResponse>(cancellationToken: ct)
                ?? throw new SignatureException("Respuesta vacía del servidor", request.CertificateId);

            _logger.LogInformation("Firma generada exitosamente: Formato={SignatureFormat}", result.SignatureFormat);

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error de conexión al generar firma");
            throw new SignatureException("Error de conexión con el servicio de firmas", request.CertificateId, ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout al generar firma");
            throw new SignatureException("Timeout en el servicio de firmas", request.CertificateId, ex);
        }
    }

    public async Task<SignatureValidationResponse> ValidateSignatureAsync(string signatureData, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Validando firma digital");

            var endpoint = ApiEndpoints.DigitalSignature.ValidateSignature;
            var request = new { signatureData };
            var response = await _httpClient.PostAsJsonAsync(endpoint, request, cancellationToken: ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new SignatureException($"Error al validar firma: {response.StatusCode}");
            }

            var result = await response.Content.ReadAsAsync<SignatureValidationResponse>(cancellationToken: ct)
                ?? throw new SignatureException("Respuesta vacía del servidor");

            _logger.LogInformation("Validación completada: IsValid={IsValid}", result.IsValid);

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error de conexión al validar firma");
            throw new SignatureException("Error de conexión con el servicio de firmas", ex.Message, ex);
        }
    }

    public async Task<CertificateResponse> GetCertificateInfoAsync(CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Obteniendo información del certificado");

            var endpoint = ApiEndpoints.DigitalSignature.GetCertificateInfo;
            var response = await _httpClient.GetAsync(endpoint, cancellationToken: ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new SignatureException($"Error al obtener info del certificado: {response.StatusCode}");
            }

            var result = await response.Content.ReadAsAsync<CertificateResponse>(cancellationToken: ct)
                ?? throw new SignatureException("Respuesta vacía del servidor");

            _logger.LogInformation("Información del certificado obtenida: ID={CertificateId}, IsActive={IsActive}", 
                result.CertificateId, result.IsActive);

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error de conexión al obtener info del certificado");
            throw new SignatureException("Error de conexión con el servicio de firmas", ex.Message, ex);
        }
    }
}
