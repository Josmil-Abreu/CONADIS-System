namespace CONADIS.Integration.Models.Responses;

using System;

/// <summary>Respuesta de la pasarela de pago después de iniciar una transacción</summary>
public class PaymentResponse
{
    /// <summary>Indica si el pago fue procesado exitosamente</summary>
    public bool Success { get; set; }

    /// <summary>ID de la transacción en la pasarela</summary>
    public string TransactionId { get; set; } = string.Empty;

    /// <summary>Estado del pago (Pending, Completed, Failed, Cancelled)</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Mensaje de error si hubo algún problema</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Fecha de procesamiento de la transacción</summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Monto procesado</summary>
    public decimal Amount { get; set; }

    /// <summary>URL para redirigir al usuario a la pasarela</summary>
    public string? RedirectUrl { get; set; }
}

/// <summary>Respuesta sobre el estado de una transacción de pago</summary>
public class PaymentStatusResponse
{
    /// <summary>ID de la transacción</summary>
    public string TransactionId { get; set; } = string.Empty;

    /// <summary>Estado actual del pago</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Monto de la transacción</summary>
    public decimal Amount { get; set; }

    /// <summary>Fecha de creación</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Fecha de completación (si aplica)</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Detalles adicionales</summary>
    public string? Details { get; set; }
}
