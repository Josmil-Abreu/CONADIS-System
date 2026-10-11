namespace CONADIS.Integration.Services.Interfaces;

using System;
using System.Threading;
using System.Threading.Tasks;
using CONADIS.Domain.Common;
using CONADIS.Integration.Models.Requests;

/// <summary>
/// Servicio de orquestación de diagnósticos.
/// Se encarga de solicitar y gestionar diagnósticos médicos del MSP.
/// </summary>
public interface IDiagnosticService
{
    /// <summary>Solicita un diagnóstico al MSP</summary>
    Task<Result<string>> RequestDiagnosticAsync(MspDiagnosticRequest request, CancellationToken ct = default);

    /// <summary>Consulta el estado de un diagnóstico</summary>
    Task<Result<string>> GetDiagnosticStatusAsync(string requestId, CancellationToken ct = default);

    /// <summary>Obtiene un certificado médico</summary>
    Task<Result<string>> GetCertificateAsync(string certificateId, CancellationToken ct = default);
}
