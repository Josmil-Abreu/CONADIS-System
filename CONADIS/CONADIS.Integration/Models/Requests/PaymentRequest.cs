namespace CONADIS.Integration.Models.Requests;

using System;
using System.Collections.Generic;

/// <summary>Solicitud de pago a la pasarela</summary>
public class PaymentRequest
{
    /// <summary>ID único de la transacción en CONADIS</summary>
    public string TransactionId { get; set; } = string.Empty;

    /// <summary>Monto a pagar</summary>
    public decimal Amount { get; set; }

    /// <summary>Moneda de pago (ej: DOP, USD)</summary>
    public string Currency { get; set; } = "DOP";

    /// <summary>Descripción de la transacción</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Email del cliente</summary>
    public string CustomerEmail { get; set; } = string.Empty;

    /// <summary>Teléfono del cliente</summary>
    public string CustomerPhone { get; set; } = string.Empty;

    /// <summary>Fecha de creación de la solicitud</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Metadatos adicionales (ID expediente, etc.)</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}
