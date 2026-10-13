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

/// <summary>Implementación del servicio de orquestación de firmas digitales</summary>
public class DigitalSignatureService : IDigitalSignatureService
{
    private readonly IDigitalSignatureClient _client;
    private readonly ILogger<DigitalSignatureService> _logger;

    public DigitalSignatureService(
        IDigitalSignatureClient client,
        ILogger<DigitalSignatureService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<Result<string>> SignDocumentAsync(SignatureRequest signature, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Firmando documento con certificado: {CertificateId}", signature.CertificateId);

            var response = await _client.GenerateSignatureAsync(signature, ct);

            if (!response.Success)
            {
                return Result<string>.Failure($"[SIGNATURE_FAILED] Error al firmar: {response.ErrorMessage}");
            }

            return Result<string>.Success(response.SignatureData);
        }
        catch (SignatureException ex)
        {
            _logger.LogError(ex, "Error al firmar documento");
            var code = ex.ErrorCode ?? "SIGNATURE_ERROR";
            return Result<string>.Failure($"[{code}] {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al firmar documento");
            return Result<string>.Failure("[UNKNOWN_ERROR] Error inesperado al firmar");
        }
    }

    public async Task<Result<bool>> ValidateSignatureAsync(string signatureData, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Validando firma digital");

            var response = await _client.ValidateSignatureAsync(signatureData, ct);

            return Result<bool>.Success(response.IsValid);
        }
        catch (SignatureException ex)
        {
            _logger.LogError(ex, "Error al validar firma");
            var code = ex.ErrorCode ?? "VALIDATION_ERROR";
            return Result<bool>.Failure($"[{code}] {ex.Message}");
        }
    }

    public async Task<Result<string>> GetCertificateStatusAsync(CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Obteniendo estado del certificado");

            var response = await _client.GetCertificateInfoAsync(ct);

            if (!response.IsActive)
            {
                return Result<string>.Failure("[INACTIVE_CERTIFICATE] Certificado no activo");
            }

            return Result<string>.Success($"Certificado activo hasta {response.ExpiresAt:yyyy-MM-dd}");
        }
        catch (SignatureException ex)
        {
            _logger.LogError(ex, "Error al obtener estado del certificado");
            var code = ex.ErrorCode ?? "CERTIFICATE_ERROR";
            return Result<string>.Failure($"[{code}] {ex.Message}");
        }
    }
}