namespace CONADIS.Integration.Mocks;

using System;
using System.Threading;
using System.Threading.Tasks;
using CONADIS.Integration.Models.Requests;
using CONADIS.Integration.Models.Responses;
using Microsoft.Extensions.Logging;

/// <summary>Interfaz para el servicio mock del MSP</summary>
public interface IMspMockService
{
    Task<MspDiagnosticResponse> RequestDiagnosticMockAsync(MspDiagnosticRequest request, CancellationToken ct = default);
    Task<MspDiagnosticStatusResponse> GetDiagnosticStatusMockAsync(string requestId, CancellationToken ct = default);
    Task<MspCertificateResponse> GetCertificateMockAsync(string certificateId, CancellationToken ct = default);
}

/// <summary>Mock del MSP para desarrollo y pruebas sin conectarse a la API real</summary>
public class MspMockService : IMspMockService
{
    private readonly ILogger<MspMockService> _logger;
    private static readonly Dictionary<string, MspDiagnosticResponse> _diagnosticsCache = new();
    private static readonly Dictionary<string, MspCertificateResponse> _certificatesCache = new();

    public MspMockService(ILogger<MspMockService> logger)
    {
        _logger = logger;
    }

    public async Task<MspDiagnosticResponse> RequestDiagnosticMockAsync(MspDiagnosticRequest request, CancellationToken ct = default)
    {
        // Simular latencia de red
        await Task.Delay(Random.Shared.Next(200, 500), ct);

        _logger.LogInformation("Mock MSP: Solicitando diagnóstico para paciente {PatientCedula}", request.PatientCedula);

        var requestId = Guid.NewGuid().ToString();
        var diagnosticCode = "MSP-" + DateTime.UtcNow.Ticks;

        var response = new MspDiagnosticResponse
        {
            RequestId = requestId,
            Status = "Procesado",
            DiagnosticCode = diagnosticCode,
            ProcessedAt = DateTime.UtcNow,
            Observations = $"Diagnóstico simulado para {request.DiagnosticType}: {request.Symptoms}"
        };

        // Simular almacenamiento en caché
        _diagnosticsCache[requestId] = response;

        return response;
    }

    public async Task<MspDiagnosticStatusResponse> GetDiagnosticStatusMockAsync(string requestId, CancellationToken ct = default)
    {
        await Task.Delay(Random.Shared.Next(100, 250), ct);

        _logger.LogInformation("Mock MSP: Consultando estado de diagnóstico {RequestId}", requestId);

        // Simular progreso
        int progress = Random.Shared.Next(30, 100);
        string status = progress >= 100 ? "Completado" : progress >= 70 ? "En Progreso Avanzado" : "En Progreso";

        return new MspDiagnosticStatusResponse
        {
            RequestId = requestId,
            Status = status,
            ProgressPercentage = progress,
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            LastUpdatedAt = DateTime.UtcNow,
            EstimatedCompletionDate = DateTime.UtcNow.AddHours(Random.Shared.Next(1, 6))
        };
    }

    public async Task<MspCertificateResponse> GetCertificateMockAsync(string certificateId, CancellationToken ct = default)
    {
        await Task.Delay(Random.Shared.Next(100, 300), ct);

        _logger.LogInformation("Mock MSP: Obteniendo certificado {CertificateId}", certificateId);

        return new MspCertificateResponse
        {
            CertificateId = certificateId,
            PatientCedula = "40123456789",
            PatientName = "Juan Pedro Pérez",
            Diagnosis = "Diagnóstico de discapacidad - Simulado",
            Recommendations = "Seguimiento médico cada 6 meses",
            IssuedAt = DateTime.UtcNow.AddDays(-5),
            ValidUntil = DateTime.UtcNow.AddYears(1),
            MedicalProfessionalCedula = "12345678901",
            CertificateData = "Base64EncodedPDFCertificate..."
        };
    }
}
