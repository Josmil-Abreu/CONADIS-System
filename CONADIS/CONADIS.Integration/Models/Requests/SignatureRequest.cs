namespace CONADIS.Integration.Models.Requests;

using System;

/// <summary>Solicitud de firma digital para comprobante fiscal electrónico</summary>
public class SignatureRequest
{
    /// <summary>Contenido del documento a firmar (generalmente en formato XML)</summary>
    public string DocumentContent { get; set; } = string.Empty;

    /// <summary>ID del certificado digital a usar para la firma</summary>
    public string CertificateId { get; set; } = string.Empty;

    /// <summary>PIN o contraseña del certificado (opcional, podría venir del vault)</summary>
    public string? Pin { get; set; }

    /// <summary>Timestamp de cuando se solicita la firma</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Formato de firma deseado (XAdES, CAdES, PAdES)</summary>
    public string SignatureFormat { get; set; } = "XAdES";
}
