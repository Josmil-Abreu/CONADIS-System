namespace CONADIS.Integration.Models.Responses;

using System;

/// <summary>Respuesta de solicitud de diagnóstico al MSP</summary>
public class MspDiagnosticResponse
{
    /// <summary>ID único de la solicitud</summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>Estado de la solicitud (Procesado, En Progreso, Rechazado)</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Código de diagnóstico asignado</summary>
    public string DiagnosticCode { get; set; } = string.Empty;

    /// <summary>Fecha de procesamiento</summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Observaciones del diagnóstico</summary>
    public string? Observations { get; set; }

    /// <summary>Mensaje de error si hubo algún problema</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>Estado de una solicitud de diagnóstico</summary>
public class MspDiagnosticStatusResponse
{
    /// <summary>ID de la solicitud</summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>Estado actual</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Porcentaje de progreso (0-100)</summary>
    public int ProgressPercentage { get; set; }

    /// <summary>Fecha de creación</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Fecha de última actualización</summary>
    public DateTime? LastUpdatedAt { get; set; }

    /// <summary>Fecha estimada de completación</summary>
    public DateTime? EstimatedCompletionDate { get; set; }
}

/// <summary>Certificado médico emitido por el MSP</summary>
public class MspCertificateResponse
{
    /// <summary>ID único del certificado</summary>
    public string CertificateId { get; set; } = string.Empty;

    /// <summary>Cédula del paciente</summary>
    public string PatientCedula { get; set; } = string.Empty;

    /// <summary>Nombre del paciente</summary>
    public string PatientName { get; set; } = string.Empty;

    /// <summary>Diagnóstico certificado</summary>
    public string Diagnosis { get; set; } = string.Empty;

    /// <summary>Recomendaciones médicas</summary>
    public string? Recommendations { get; set; }

    /// <summary>Fecha de emisión</summary>
    public DateTime IssuedAt { get; set; }

    /// <summary>Fecha de validez (hasta)</summary>
    public DateTime? ValidUntil { get; set; }

    /// <summary>Cédula del profesional médico que emitió</summary>
    public string MedicalProfessionalCedula { get; set; } = string.Empty;

    /// <summary>Datos del certificado en formato digital/PDF</summary>
    public string? CertificateData { get; set; }
}
