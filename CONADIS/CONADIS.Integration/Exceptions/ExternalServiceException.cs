namespace CONADIS.Integration.Exceptions;

using System;

/// <summary>Excepción base para errores de servicios externos</summary>
public class ExternalServiceException : Exception
{
    /// <summary>Código de error del servicio externo</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Código de estado HTTP</summary>
    public int? StatusCode { get; set; }

    /// <summary>Detalles adicionales del error</summary>
    public string? Details { get; set; }

    public ExternalServiceException(string message) : base(message) { }

    public ExternalServiceException(string message, Exception innerException) 
        : base(message, innerException) { }

    public ExternalServiceException(string message, string? errorCode, int? statusCode, string? details = null)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
        Details = details;
    }
}

/// <summary>Excepción específica para errores de pago</summary>
public class PaymentException : ExternalServiceException
{
    /// <summary>ID de la transacción</summary>
    public string? TransactionId { get; set; }

    public PaymentException(string message) : base(message) { }

    public PaymentException(string message, string transactionId) 
        : base(message)
    {
        TransactionId = transactionId;
    }

    public PaymentException(string message, string transactionId, Exception innerException)
        : base(message, innerException)
    {
        TransactionId = transactionId;
    }
}

/// <summary>Excepción específica para errores de firma digital</summary>
public class SignatureException : ExternalServiceException
{
    /// <summary>ID del certificado</summary>
    public string? CertificateId { get; set; }

    public SignatureException(string message) : base(message) { }

    public SignatureException(string message, string certificateId)
        : base(message)
    {
        CertificateId = certificateId;
    }

    public SignatureException(string message, string certificateId, Exception innerException)
        : base(message, innerException)
    {
        CertificateId = certificateId;
    }
}

/// <summary>Excepción específica para errores de validación de identidad (JCE)</summary>
public class IdentityValidationException : ExternalServiceException
{
    /// <summary>Cédula que fue validada</summary>
    public string? Cedula { get; set; }

    public IdentityValidationException(string message) : base(message) { }

    public IdentityValidationException(string message, string cedula)
        : base(message)
    {
        Cedula = cedula;
    }

    public IdentityValidationException(string message, string cedula, Exception innerException)
        : base(message, innerException)
    {
        Cedula = cedula;
    }
}

/// <summary>Excepción específica para errores de diagnóstico (MSP)</summary>
public class DiagnosticException : ExternalServiceException
{
    /// <summary>ID de la solicitud de diagnóstico</summary>
    public string? DiagnosticRequestId { get; set; }

    public DiagnosticException(string message) : base(message) { }

    public DiagnosticException(string message, string diagnosticRequestId)
        : base(message)
    {
        DiagnosticRequestId = diagnosticRequestId;
    }

    public DiagnosticException(string message, string diagnosticRequestId, Exception innerException)
        : base(message, innerException)
    {
        DiagnosticRequestId = diagnosticRequestId;
    }
}
