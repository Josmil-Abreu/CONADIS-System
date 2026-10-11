namespace CONADIS.Integration.Models.Responses;

using System;

/// <summary>Respuesta de validación de cédula contra la JCE</summary>
public class JceValidationResponse
{
    /// <summary>Indica si la cédula es válida</summary>
    public bool IsValid { get; set; }

    /// <summary>Número de cédula validado</summary>
    public string Cedula { get; set; } = string.Empty;

    /// <summary>Nombre del ciudadano</summary>
    public string? FirstName { get; set; }

    /// <summary>Apellido del ciudadano</summary>
    public string? LastName { get; set; }

    /// <summary>Estado del ciudadano (Activo, Inactivo, Fallecido, etc.)</summary>
    public string? Status { get; set; }

    /// <summary>Mensaje de error si hubo algún problema</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>Información completa de un ciudadano según la JCE</summary>
public class JceCitizenInfoResponse
{
    /// <summary>Número de cédula</summary>
    public string Cedula { get; set; } = string.Empty;

    /// <summary>Nombre completo</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Fecha de nacimiento</summary>
    public DateTime? DateOfBirth { get; set; }

    /// <summary>Género (M/F)</summary>
    public string? Gender { get; set; }

    /// <summary>Dirección registrada</summary>
    public string? Address { get; set; }

    /// <summary>Estado electoral</summary>
    public string? ElectoralStatus { get; set; }
}

/// <summary>Estado electoral de un ciudadano</summary>
public class JceElectoralStatusResponse
{
    /// <summary>Número de cédula</summary>
    public string Cedula { get; set; } = string.Empty;

    /// <summary>Estado electoral (Activo, Inactivo, Suspendido)</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Si puede votar o no</summary>
    public bool CanVote { get; set; }

    /// <summary>Última actualización del estado</summary>
    public DateTime? LastUpdated { get; set; }

    /// <summary>Observaciones adicionales</summary>
    public string? Observations { get; set; }
}
