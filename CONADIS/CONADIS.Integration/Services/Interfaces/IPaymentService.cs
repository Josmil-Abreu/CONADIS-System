namespace CONADIS.Integration.Services.Interfaces;

using System;
using System.Threading;
using System.Threading.Tasks;
using CONADIS.Domain.Common;
using CONADIS.Integration.Models.Requests;

/// <summary>
/// Servicio de orquestación de pagos.
/// Se encarga de coordinar la lógica de negocio del pago con los clientes HTTP.
/// Retorna resultados usando el patrón Result para mantener consistencia con Application.
/// </summary>
public interface IPaymentService
{
    /// <summary>Procesa una solicitud de pago</summary>
    Task<Result<string>> ProcessPaymentAsync(PaymentRequest payment, CancellationToken ct = default);

    /// <summary>Obtiene el estado actual de un pago</summary>
    Task<Result<string>> GetPaymentStatusAsync(string transactionId, CancellationToken ct = default);

    /// <summary>Procesa un reembolso de una transacción anterior</summary>
    Task<Result<string>> RefundPaymentAsync(string transactionId, decimal amount, CancellationToken ct = default);
}
