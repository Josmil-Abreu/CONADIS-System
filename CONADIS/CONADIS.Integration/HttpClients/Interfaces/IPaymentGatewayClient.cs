namespace CONADIS.Integration.HttpClients.Interfaces;

using CONADIS.Integration.Models.Requests;
using CONADIS.Integration.Models.Responses;

/// <summary>
/// Cliente HTTP para la pasarela de pago.
/// Se encarga de iniciar transacciones de pago, consultar estado y procesar reembolsos.
/// </summary>
public interface IPaymentGatewayClient
{
    /// <summary>Inicia una transacción de pago</summary>
    Task<PaymentResponse> InitiatePaymentAsync(PaymentRequest request, CancellationToken ct = default);

    /// <summary>Consulta el estado de una transacción de pago</summary>
    Task<PaymentStatusResponse> GetPaymentStatusAsync(string transactionId, CancellationToken ct = default);

    /// <summary>Procesa un reembolso de una transacción anterior</summary>
    Task<PaymentResponse> RefundAsync(string transactionId, decimal amount, CancellationToken ct = default);
}
