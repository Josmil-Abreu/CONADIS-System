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

/// <summary>Implementación real del cliente HTTP para el MSP</summary>
public class MspApiClient : IMspApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MspSettings _settings;
    private readonly ILogger<MspApiClient> _logger;

    public MspApiClient(
        HttpClient httpClient,
        IOptions<IntegrationSettings> options,
        ILogger<MspApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value.Msp;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_settings.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
        _httpClient.DefaultRequestHeaders.Add("X-API-Key", _settings.ApiKey);
    }

    public async Task<MspDiagnosticResponse> RequestDiagnosticAsync(MspDiagnosticRequest request, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Solicitando diagnóstico al MSP para paciente: {PatientCedula}",
                request.PatientCedula);

            var endpoint = ApiEndpoints.Msp.RequestDiagnostic;
            var response = await _httpClient.PostAsJsonAsync(endpoint, request, cancellationToken: ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new DiagnosticException(
                    $"Error al solicitar diagnóstico: {response.StatusCode}",
                    request.PatientCedula);
            }

            var result = await response.Content.ReadFromJsonAsync<MspDiagnosticResponse>(cancellationToken: ct)
                ?? throw new DiagnosticException("Respuesta vacía del servidor", request.PatientCedula);

            _logger.LogInformation("Diagnóstico solicitado exitosamente: RequestId={RequestId}, Status={Status}",
                result.RequestId, result.Status);

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error de conexión al solicitar diagnóstico");
            throw new DiagnosticException("Error de conexión con el MSP", request.PatientCedula, ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout al solicitar diagnóstico");
            throw new DiagnosticException("Timeout en el MSP", request.PatientCedula, ex);
        }
    }

    public async Task<MspDiagnosticStatusResponse> GetDiagnosticStatusAsync(string requestId, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Consultando estado de diagnóstico: RequestId={RequestId}", requestId);

            var endpoint = ApiEndpoints.Msp.GetDiagnosticStatus.Replace("{requestId}", requestId);
            var response = await _httpClient.GetAsync(endpoint, cancellationToken: ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new DiagnosticException($"Error al consultar estado: {response.StatusCode}", requestId);
            }

            var result = await response.Content.ReadFromJsonAsync<MspDiagnosticStatusResponse>(cancellationToken: ct)
                ?? throw new DiagnosticException("Respuesta vacía del servidor", requestId);

            _logger.LogInformation("Estado de diagnóstico obtenido: RequestId={RequestId}, Status={Status}",
                result.RequestId, result.Status);

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error de conexión al consultar estado");
            throw new DiagnosticException("Error de conexión con el MSP", requestId, ex);
        }
    }

    public async Task<MspCertificateResponse> GetCertificateAsync(string certificateId, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Obteniendo certificado médico: CertificateId={CertificateId}", certificateId);

            var endpoint = ApiEndpoints.Msp.GetCertificate.Replace("{certificateId}", certificateId);
            var response = await _httpClient.GetAsync(endpoint, cancellationToken: ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new DiagnosticException($"Error al obtener certificado: {response.StatusCode}", certificateId);
            }

            var result = await response.Content.ReadFromJsonAsync<MspCertificateResponse>(cancellationToken: ct)
                ?? throw new DiagnosticException("Respuesta vacía del servidor", certificateId);

            _logger.LogInformation("Certificado obtenido: CertificateId={CertificateId}, PatientCedula={PatientCedula}",
                certificateId, result.PatientCedula);

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error de conexión al obtener certificado");
            throw new DiagnosticException("Error de conexión con el MSP", certificateId, ex);
        }
    }
}