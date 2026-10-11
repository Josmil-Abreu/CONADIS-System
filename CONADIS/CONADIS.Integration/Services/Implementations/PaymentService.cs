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

/// <summary>Implementación del servicio de orquestación de pagos</summary>
public class PaymentService : IPaymentService
{
    private readonly IPaymentGatewayClient _client;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IPaymentGatewayClient client,
        ILogger<PaymentService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<Result<string>> ProcessPaymentAsync(PaymentRequest payment, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Procesando pago: {TransactionId}", payment.TransactionId);

            var response = await _client.InitiatePaymentAsync(payment, ct);

            if (!response.Success)
            {
                return Result<string>.Failure(
                    $"Error en pago: {response.ErrorMessage}",
                    "PAYMENT_FAILED");
            }

            return Result<string>.Success(response.TransactionId);
        }
        catch (PaymentException ex)
        {
            _logger.LogError(ex, "Error al procesar pago: {TransactionId}", payment.TransactionId);
            return Result<string>.Failure(ex.Message, ex.ErrorCode ?? "PAYMENT_ERROR");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al procesar pago");
            return Result<string>.Failure("Error inesperado al procesar el pago", "UNKNOWN_ERROR");
        }
    }

    public async Task<Result<string>> GetPaymentStatusAsync(string transactionId, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Consultando estado de pago: {TransactionId}", transactionId);

            var response = await _client.GetPaymentStatusAsync(transactionId, ct);

            return Result<string>.Success(response.Status);
        }
        catch (PaymentException ex)
        {
            _logger.LogError(ex, "Error al consultar estado de pago");
            return Result<string>.Failure(ex.Message, ex.ErrorCode ?? "STATUS_ERROR");
        }
    }

    public async Task<Result<string>> RefundPaymentAsync(string transactionId, decimal amount, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Reembolsando pago: {TransactionId}, Amount: {Amount}", transactionId, amount);

            var response = await _client.RefundAsync(transactionId, amount, ct);

            if (!response.Success)
            {
                return Result<string>.Failure(
                    $"Error en reembolso: {response.ErrorMessage}",
                    "REFUND_FAILED");
            }

            return Result<string>.Success(response.TransactionId);
        }
        catch (PaymentException ex)
        {
            _logger.LogError(ex, "Error al reembolsar pago");
            return Result<string>.Failure(ex.Message, ex.ErrorCode ?? "REFUND_ERROR");
        }
    }
}
