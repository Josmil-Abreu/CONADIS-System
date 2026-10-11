namespace CONADIS.Integration.Models.Requests;

using System;

/// <summary>Solicitud de diagnóstico médico al MSP</summary>
public class MspDiagnosticRequest
{
    /// <summary>Cédula del paciente</summary>
    public string PatientCedula { get; set; } = string.Empty;

    /// <summary>Nombre del paciente</summary>
    public string PatientName { get; set; } = string.Empty;

    /// <summary>Edad del paciente</summary>
    public int PatientAge { get; set; }

    /// <summary>Descripción de los síntomas o razón del diagnóstico</summary>
    public string Symptoms { get; set; } = string.Empty;

    /// <summary>Tipo de diagnóstico solicitado</summary>
    public string DiagnosticType { get; set; } = string.Empty;

    /// <summary>Cédula del profesional médico que solicita</summary>
    public string MedicalProfessionalCedula { get; set; } = string.Empty;

    /// <summary>Fecha de la solicitud</summary>
    public DateTime RequestDate { get; set; } = DateTime.UtcNow;

    /// <summary>Observaciones adicionales</summary>
    public string? Observations { get; set; }
}
