namespace CONADIS.Integration.HttpClients.Interfaces;

using CONADIS.Integration.Models.Requests;
using CONADIS.Integration.Models.Responses;

/// <summary>
/// Cliente HTTP para el generador/validador de firmas digitales de la DGII.
/// Se encarga de generar y validar firmas para comprobantes fiscales electrónicos (e-CF).
/// </summary>
public interface IDigitalSignatureClient
{
    /// <summary>Genera una firma digital para un comprobante fiscal electrónico</summary>
    Task<SignatureResponse> GenerateSignatureAsync(SignatureRequest request, CancellationToken ct = default);

    /// <summary>Valida si una firma digital es válida y no ha sido alterada</summary>
    Task<SignatureValidationResponse> ValidateSignatureAsync(string signatureData, CancellationToken ct = default);

    /// <summary>Obtiene información del certificado digital actual</summary>
    Task<CertificateResponse> GetCertificateInfoAsync(CancellationToken ct = default);
}
