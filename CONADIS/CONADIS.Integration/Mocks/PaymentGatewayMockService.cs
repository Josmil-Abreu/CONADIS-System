namespace CONADIS.Integration.Mocks;

using CONADIS.Integration.Models.Requests;
using CONADIS.Integration.Models.Responses;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>Interfaz para el servicio mock de la pasarela de pago</summary>
public interface IPaymentGatewayMockService
{
    Task<PaymentResponse> InitiatePaymentMockAsync(PaymentRequest request, CancellationToken ct = default);
    Task<PaymentStatusResponse> GetPaymentStatusMockAsync(string transactionId, CancellationToken ct = default);
    Task<PaymentResponse> RefundMockAsync(string transactionId, decimal amount, CancellationToken ct = default);
}

/// <summary>Mock de la pasarela de pago para desarrollo y pruebas</summary>
public class PaymentGatewayMockService : IPaymentGatewayMockService
{
    private readonly ILogger<PaymentGatewayMockService> _logger;
    private static readonly Dictionary<string, PaymentStatusResponse> _paymentsCache = new();

    public PaymentGatewayMockService(ILogger<PaymentGatewayMockService> logger)
    {
        _logger = logger;
    }

    public async Task<PaymentResponse> InitiatePaymentMockAsync(PaymentRequest request, CancellationToken ct = default)
    {
        // Simular latencia de red
        await Task.Delay(Random.Shared.Next(300, 800), ct);

        _logger.LogInformation("Mock Payment: Iniciando pago: TransactionId={TransactionId}, Amount={Amount}",
            request.TransactionId, request.Amount);

        // Simular que el 90% de los pagos son exitosos
        bool isSuccessful = Random.Shared.Next(0, 100) < 90;

        var response = new PaymentResponse
        {
            Success = isSuccessful,
            TransactionId = request.TransactionId,
            Status = isSuccessful ? "Pending" : "Failed",
            ErrorMessage = isSuccessful ? null : "Fondos insuficientes",
            ProcessedAt = DateTime.UtcNow,
            Amount = request.Amount,
            RedirectUrl = isSuccessful ? $"https://payment-gateway.com/checkout/{request.TransactionId}" : null
        };

        // Simular almacenamiento
        _paymentsCache[request.TransactionId] = new PaymentStatusResponse
        {
            TransactionId = request.TransactionId,
            Status = response.Status,
            Amount = request.Amount,
            CreatedAt = DateTime.UtcNow
        };

        return response;
    }

    public async Task<PaymentStatusResponse> GetPaymentStatusMockAsync(string transactionId, CancellationToken ct = default)
    {
        await Task.Delay(Random.Shared.Next(100, 300), ct);

        _logger.LogInformation("Mock Payment: Consultando estado de pago: TransactionId={TransactionId}", transactionId);

        if (_paymentsCache.TryGetValue(transactionId, out var cachedPayment))
        {
            // Simular cambio de estado
            var random = Random.Shared.Next(0, 100);
            string newStatus = random < 70 ? "Completed" : random < 90 ? "Pending" : "Failed";

            cachedPayment.Status = newStatus;
            cachedPayment.CompletedAt = newStatus == "Completed" ? DateTime.UtcNow : null;

            return cachedPayment;
        }

        // Si no está en caché, retornar error
        return new PaymentStatusResponse
        {
            TransactionId = transactionId,
            Status = "Not Found",
            Amount = 0
        };
    }

    public async Task<PaymentResponse> RefundMockAsync(string transactionId, decimal amount, CancellationToken ct = default)
    {
        await Task.Delay(Random.Shared.Next(300, 600), ct);

        _logger.LogInformation("Mock Payment: Procesando reembolso: TransactionId={TransactionId}, Amount={Amount}",
            transactionId, amount);

        return new PaymentResponse
        {
            Success = true,
            TransactionId = $"{transactionId}-REFUND",
            Status = "Refunded",
            ProcessedAt = DateTime.UtcNow,
            Amount = amount
        };
    }
}
