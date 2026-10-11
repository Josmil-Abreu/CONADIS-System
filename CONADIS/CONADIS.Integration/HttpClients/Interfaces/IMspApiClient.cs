namespace CONADIS.Integration.HttpClients.Interfaces;

using CONADIS.Integration.Models.Requests;
using CONADIS.Integration.Models.Responses;

/// <summary>
/// Cliente HTTP para el MSP (Ministerio de Salud Pública).
/// Se encarga de solicitar diagnósticos médicos y obtener certificados.
/// </summary>
public interface IMspApiClient
{
    /// <summary>Solicita un diagnóstico médico a través del MSP</summary>
    Task<MspDiagnosticResponse> RequestDiagnosticAsync(MspDiagnosticRequest request, CancellationToken ct = default);

    /// <summary>Consulta el estado de un diagnóstico solicitado</summary>
    Task<MspDiagnosticStatusResponse> GetDiagnosticStatusAsync(string requestId, CancellationToken ct = default);

    /// <summary>Obtiene un certificado médico emitido por el MSP</summary>
    Task<MspCertificateResponse> GetCertificateAsync(string certificateId, CancellationToken ct = default);
}
