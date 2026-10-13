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
                return Result<string>.Failure($"[PAYMENT_FAILED] Error en pago: {response.ErrorMessage}");
            }

            return Result<string>.Success(response.TransactionId);
        }
        catch (PaymentException ex)
        {
            _logger.LogError(ex, "Error al procesar pago: {TransactionId}", payment.TransactionId);
            var code = ex.ErrorCode ?? "PAYMENT_ERROR";
            return Result<string>.Failure($"[{code}] {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al procesar pago");
            return Result<string>.Failure("[UNKNOWN_ERROR] Error inesperado al procesar el pago");
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
            var code = ex.ErrorCode ?? "STATUS_ERROR";
            return Result<string>.Failure($"[{code}] {ex.Message}");
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
                return Result<string>.Failure($"[REFUND_FAILED] Error en reembolso: {response.ErrorMessage}");
            }

            return Result<string>.Success(response.TransactionId);
        }
        catch (PaymentException ex)
        {
            _logger.LogError(ex, "Error al reembolsar pago");
            var code = ex.ErrorCode ?? "REFUND_ERROR";
            return Result<string>.Failure($"[{code}] {ex.Message}");
        }
    }
}