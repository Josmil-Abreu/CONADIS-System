namespace CONADIS.Integration.Models.Requests;

/// <summary>Solicitud de validación de cédula contra la JCE</summary>
public class JceValidationRequest
{
    /// <summary>Número de cédula a validar (sin guiones ni espacios)</summary>
    public string Cedula { get; set; } = string.Empty;

    /// <summary>Nombre de la persona (opcional, para validación adicional)</summary>
    public string? FirstName { get; set; }

    /// <summary>Apellido de la persona (opcional, para validación adicional)</summary>
    public string? LastName { get; set; }

    /// <summary>Indica si se debe buscar información completa del ciudadano</summary>
    public bool IncludeFullInfo { get; set; } = false;
}
