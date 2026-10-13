namespace CONADIS.Integration.Services.Interfaces;

using CONADIS.Domain.Common;
using CONADIS.Integration.Models.Requests;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Servicio de orquestación de firmas digitales.
/// Se encarga de generar y validar firmas para comprobantes fiscales electrónicos (e-CF).
/// </summary>
public interface IDigitalSignatureService
{
    /// <summary>Firma un documento con certificado digital</summary>
    Task<Result<string>> SignDocumentAsync(SignatureRequest signature, CancellationToken ct = default);

    /// <summary>Valida si una firma es válida</summary>
    Task<Result<bool>> ValidateSignatureAsync(string signatureData, CancellationToken ct = default);

    /// <summary>Obtiene el estado del certificado digital</summary>
    Task<Result<string>> GetCertificateStatusAsync(CancellationToken ct = default);
}
