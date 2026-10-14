namespace CONADIS.Integration.HttpClients.Implementations;

using CONADIS.Integration.HttpClients.Interfaces;
using CONADIS.Integration.Mocks;
using CONADIS.Integration.Models.Requests;
using CONADIS.Integration.Models.Responses;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Adaptador que implementa IPaymentGatewayClient usando el mock.
/// Permite intercambiar entre implementación real y mock sin cambiar código.
/// </summary>
public class PaymentGatewayMockAdapter : IPaymentGatewayClient
{
    private readonly IPaymentGatewayMockService _mockService;
    private readonly ILogger<PaymentGatewayMockAdapter> _logger;

    public PaymentGatewayMockAdapter(
        IPaymentGatewayMockService mockService,
        ILogger<PaymentGatewayMockAdapter> logger)
    {
        _mockService = mockService;
        _logger = logger;
    }

    public async Task<PaymentResponse> InitiatePaymentAsync(PaymentRequest request, CancellationToken ct = default)
    {
        _logger.LogWarning("?? Usando MOCK de Payment Gateway");
        return await _mockService.InitiatePaymentMockAsync(request, ct);
    }

    public async Task<PaymentStatusResponse> GetPaymentStatusAsync(string transactionId, CancellationToken ct = default)
    {
        _logger.LogWarning("?? Usando MOCK de Payment Gateway");
        return await _mockService.GetPaymentStatusMockAsync(transactionId, ct);
    }

    public async Task<PaymentResponse> RefundAsync(string transactionId, decimal amount, CancellationToken ct = default)
    {
        _logger.LogWarning("?? Usando MOCK de Payment Gateway");
        return await _mockService.RefundMockAsync(transactionId, amount, ct);
    }
}

/// <summary>
/// Adaptador que implementa IDigitalSignatureClient usando mock.
/// </summary>
public class DigitalSignatureMockAdapter : IDigitalSignatureClient
{
    private readonly ILogger<DigitalSignatureMockAdapter> _logger;

    public DigitalSignatureMockAdapter(ILogger<DigitalSignatureMockAdapter> logger)
    {
        _logger = logger;
    }

    public async Task<SignatureResponse> GenerateSignatureAsync(SignatureRequest request, CancellationToken ct = default)
    {
        _logger.LogWarning("?? Usando MOCK de Firmas Digitales");

        // Simular latencia
        await Task.Delay(Random.Shared.Next(200, 500), ct);

        return new SignatureResponse
        {
            Success = true,
            SignatureData = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("MOCK_SIGNATURE_DATA")),
            SignatureFormat = request.SignatureFormat,
            SignedAt = DateTime.UtcNow
        };
    }

    public async Task<SignatureValidationResponse> ValidateSignatureAsync(string signatureData, CancellationToken ct = default)
    {
        _logger.LogWarning("?? Usando MOCK de Firmas Digitales");

        await Task.Delay(Random.Shared.Next(100, 300), ct);

        return new SignatureValidationResponse
        {
            IsValid = true,
            CertificateId = "MOCK_CERT_ID",
            SignedAt = DateTime.UtcNow,
            ValidationDetails = "Firma válida en mock"
        };
    }

    public async Task<CertificateResponse> GetCertificateInfoAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("?? Usando MOCK de Firmas Digitales");

        await Task.Delay(Random.Shared.Next(100, 200), ct);

        return new CertificateResponse
        {
            CertificateId = "MOCK_CERT_ID",
            Subject = "CN=CONADIS Mock Certificate",
            IssuedAt = DateTime.UtcNow.AddYears(-1),
            ExpiresAt = DateTime.UtcNow.AddYears(2),
            IsActive = true,
            SerialNumber = "123456789"
        };
    }
}

/// <summary>
/// Adaptador que implementa IJceApiClient usando mock.
/// </summary>
public class JceApiMockAdapter : IJceApiClient
{
    private readonly IJceMockService _mockService;
    private readonly ILogger<JceApiMockAdapter> _logger;

    public JceApiMockAdapter(
        IJceMockService mockService,
        ILogger<JceApiMockAdapter> logger)
    {
        _mockService = mockService;
        _logger = logger;
    }

    public async Task<JceValidationResponse> ValidateCedulaAsync(string cedula, CancellationToken ct = default)
    {
        _logger.LogWarning("?? Usando MOCK de JCE");
        return await _mockService.ValidateCedulaMockAsync(cedula, ct);
    }

    public async Task<JceCitizenInfoResponse> GetCitizenInfoAsync(string cedula, CancellationToken ct = default)
    {
        _logger.LogWarning("?? Usando MOCK de JCE");
        return await _mockService.GetCitizenInfoMockAsync(cedula, ct);
    }

    public async Task<JceElectoralStatusResponse> GetElectoralStatusAsync(string cedula, CancellationToken ct = default)
    {
        _logger.LogWarning("?? Usando MOCK de JCE");
        return await _mockService.GetElectoralStatusMockAsync(cedula, ct);
    }
}

/// <summary>
/// Adaptador que implementa IMspApiClient usando mock.
/// </summary>
public class MspApiMockAdapter : IMspApiClient
{
    private readonly IMspMockService _mockService;
    private readonly ILogger<MspApiMockAdapter> _logger;

    public MspApiMockAdapter(
        IMspMockService mockService,
        ILogger<MspApiMockAdapter> logger)
    {
        _mockService = mockService;
        _logger = logger;
    }

    public async Task<MspDiagnosticResponse> RequestDiagnosticAsync(MspDiagnosticRequest request, CancellationToken ct = default)
    {
        _logger.LogWarning("?? Usando MOCK de MSP");
        return await _mockService.RequestDiagnosticMockAsync(request, ct);
    }

    public async Task<MspDiagnosticStatusResponse> GetDiagnosticStatusAsync(string requestId, CancellationToken ct = default)
    {
        _logger.LogWarning("?? Usando MOCK de MSP");
        return await _mockService.GetDiagnosticStatusMockAsync(requestId, ct);
    }

    public async Task<MspCertificateResponse> GetCertificateAsync(string certificateId, CancellationToken ct = default)
    {
        _logger.LogWarning("?? Usando MOCK de MSP");
        return await _mockService.GetCertificateMockAsync(certificateId, ct);
    }
}
