namespace CONADIS.Integration.Services.Implementations;

using System;
using System.Threading;
using System.Threading.Tasks;
using CONADIS.Domain.Common;
using CONADIS.Integration.Exceptions;
using CONADIS.Integration.HttpClients.Interfaces;
using CONADIS.Integration.Models.Requests;
using CONADIS.Integration.Services.Interfaces;
using Microsoft.Extensions.Logging;

/// <summary>Implementación del servicio de orquestación de diagnósticos</summary>
public class DiagnosticService : IDiagnosticService
{
    private readonly IMspApiClient _client;
    private readonly ILogger<DiagnosticService> _logger;

    public DiagnosticService(
        IMspApiClient client,
        ILogger<DiagnosticService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<Result<string>> RequestDiagnosticAsync(MspDiagnosticRequest request, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Solicitando diagnóstico para paciente: {PatientCedula}", request.PatientCedula);

            var response = await _client.RequestDiagnosticAsync(request, ct);

            if (string.IsNullOrEmpty(response.RequestId))
            {
                var errorMsg = response.ErrorMessage ?? "Error al solicitar diagnóstico";
                return Result<string>.Failure($"[REQUEST_FAILED] {errorMsg}");
            }

            return Result<string>.Success(response.RequestId);
        }
        catch (DiagnosticException ex)
        {
            _logger.LogError(ex, "Error al solicitar diagnóstico");
            var code = ex.ErrorCode ?? "DIAGNOSTIC_ERROR";
            return Result<string>.Failure($"[{code}] {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al solicitar diagnóstico");
            return Result<string>.Failure("[UNKNOWN_ERROR] Error inesperado al solicitar diagnóstico");
        }
    }

    public async Task<Result<string>> GetDiagnosticStatusAsync(string requestId, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Consultando estado de diagnóstico: {RequestId}", requestId);

            var response = await _client.GetDiagnosticStatusAsync(requestId, ct);

            return Result<string>.Success(
                $"Estado: {response.Status} ({response.ProgressPercentage}%) - Completación estimada: {response.EstimatedCompletionDate}");
        }
        catch (DiagnosticException ex)
        {
            _logger.LogError(ex, "Error al consultar estado del diagnóstico");
            var code = ex.ErrorCode ?? "STATUS_ERROR";
            return Result<string>.Failure($"[{code}] {ex.Message}");
        }
    }

    public async Task<Result<string>> GetCertificateAsync(string certificateId, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Obteniendo certificado médico: {CertificateId}", certificateId);

            var response = await _client.GetCertificateAsync(certificateId, ct);

            return Result<string>.Success(
                $"Certificado: {response.PatientName} Vigencia hasta: {response.ValidUntil?.ToString("yyyy-MM-dd")}");
        }
        catch (DiagnosticException ex)
        {
            _logger.LogError(ex, "Error al obtener certificado");
            var code = ex.ErrorCode ?? "CERTIFICATE_ERROR";
            return Result<string>.Failure($"[{code}] {ex.Message}");
        }
    }
}