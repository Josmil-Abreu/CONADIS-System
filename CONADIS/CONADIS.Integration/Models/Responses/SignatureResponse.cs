namespace CONADIS.Integration.Models.Responses;

using System;

/// <summary>Respuesta de la generación de firma digital</summary>
public class SignatureResponse
{
    /// <summary>Indica si la firma fue generada exitosamente</summary>
    public bool Success { get; set; }

    /// <summary>Datos de la firma en formato especificado</summary>
    public string SignatureData { get; set; } = string.Empty;

    /// <summary>Formato de la firma (XAdES, CAdES, PAdES)</summary>
    public string SignatureFormat { get; set; } = string.Empty;

    /// <summary>Fecha y hora en que se realizó la firma</summary>
    public DateTime SignedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Mensaje de error si hubo algún problema</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>Respuesta de validación de una firma digital</summary>
public class SignatureValidationResponse
{
    /// <summary>Indica si la firma es válida</summary>
    public bool IsValid { get; set; }

    /// <summary>ID del certificado usado en la firma</summary>
    public string CertificateId { get; set; } = string.Empty;

    /// <summary>Fecha en que se realizó la firma</summary>
    public DateTime SignedAt { get; set; }

    /// <summary>Detalles de la validación</summary>
    public string? ValidationDetails { get; set; }
}

/// <summary>Información del certificado digital</summary>
public class CertificateResponse
{
    /// <summary>ID único del certificado</summary>
    public string CertificateId { get; set; } = string.Empty;

    /// <summary>Asunto del certificado (CN)</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Fecha de emisión</summary>
    public DateTime IssuedAt { get; set; }

    /// <summary>Fecha de expiración</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Indica si el certificado es activo</summary>
    public bool IsActive { get; set; }

    /// <summary>Número de serie</summary>
    public string? SerialNumber { get; set; }
}
