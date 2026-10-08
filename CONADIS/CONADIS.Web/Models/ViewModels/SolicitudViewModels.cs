using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace CONADIS.Web.Models.ViewModels;

public enum ParaQuien { ParaMi = 1, Representado = 2 }
public enum TipoSolicitud { PrimeraVez = 1, Renovacion = 2, Duplicado = 3 }

// ---------- Paso 1: Identificación ----------
public class IdentificacionVM
{
    [Required] public ParaQuien ParaQuien { get; set; } = ParaQuien.ParaMi;
    [Required] public TipoSolicitud TipoSolicitud { get; set; } = TipoSolicitud.PrimeraVez;

    [Required(ErrorMessage = "Escribe la cédula.")]
    [RegularExpression(@"^\d{3}-?\d{7}-?\d{1}$", ErrorMessage = "La cédula debe tener 11 dígitos.")]
    public string Cedula { get; set; } = string.Empty;

    // Datos que devuelve la JCE (solo lectura en la vista)
    public bool IdentidadValidada { get; set; }
    public string? Nombres { get; set; }
    public string? Apellidos { get; set; }
    public DateOnly? FechaNacimiento { get; set; }

    [Range(1, 2, ErrorMessage = "Selecciona el sexo.")]
    public int Sexo { get; set; } = 1;
}

// ---------- Paso 2: Contacto ----------
public class ContactoVM
{
    [Required(ErrorMessage = "Escribe un teléfono.")]
    [Phone(ErrorMessage = "Teléfono no válido.")]
    public string Telefono { get; set; } = string.Empty;

    [Required(ErrorMessage = "Escribe un correo.")]
    [EmailAddress(ErrorMessage = "Correo no válido.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Elige la provincia.")] public string Provincia { get; set; } = string.Empty;
    [Required(ErrorMessage = "Elige el municipio.")] public string Municipio { get; set; } = string.Empty;
    [Required(ErrorMessage = "Escribe el sector.")] public string Sector { get; set; } = string.Empty;
    [Required(ErrorMessage = "Escribe la dirección.")] public string DireccionDetalle { get; set; } = string.Empty;

    public bool AvisosSms { get; set; } = true;
    public bool AvisosEmail { get; set; } = true;
}

// ---------- Paso 3: Diagnóstico y documentos ----------
public class DiagnosticoVM
{
    [Required(ErrorMessage = "Elige el centro de salud.")]
    public string CentroSalud { get; set; } = string.Empty;

    [Required(ErrorMessage = "Escribe el número del certificado.")]
    public string NumeroCertificado { get; set; } = string.Empty;

    [Required(ErrorMessage = "Escribe la fecha del certificado.")]
    [DataType(DataType.Date)]
    public DateOnly? FechaCertificado { get; set; }

    // Código CIE-10 para validar con Salud Pública (RF-INT-02)
    public string? CodigoCie10 { get; set; }

    // Archivos (se reciben en el POST; se guardan solo sus nombres en el borrador)
    [JsonIgnore] public IFormFile? CertificadoMedico { get; set; }
    [JsonIgnore] public List<IFormFile> OtrosEstudios { get; set; } = new();

    public string? NombreArchivoCertificado { get; set; }
    public List<string> NombresOtrosEstudios { get; set; } = new();

    public bool? ValidadoPorMsp { get; set; } // null = pendiente
}

// ---------- Paso 4: Cita y apoyos ----------
public class CitaApoyosVM
{
    [Required(ErrorMessage = "Elige un centro de valoración.")]
    public string CentroValoracion { get; set; } = string.Empty;

    [Required(ErrorMessage = "Elige una fecha y hora.")]
    public DateTime? FechaCita { get; set; }

    public bool InterpreteLenguaSenas { get; set; }
    public bool AccesoSillaRuedas { get; set; }
    public bool Acompanante { get; set; }
    public string? OtrosApoyos { get; set; }
}

// ---------- Paso 5: Revisión ----------
public class RevisionVM
{
    [ValidateNever] public SolicitudBorrador Borrador { get; set; } = new();

    [Range(typeof(bool), "true", "true", ErrorMessage = "Debes autorizar el tratamiento de tus datos.")]
    public bool AutorizaDatos { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "Debes declarar que la información es verdadera.")]
    public bool DeclaraVeracidad { get; set; }
}

// ---------- Borrador completo (se guarda en sesión) ----------
public class SolicitudBorrador
{
    public IdentificacionVM? Identificacion { get; set; }
    public ContactoVM? Contacto { get; set; }
    public DiagnosticoVM? Diagnostico { get; set; }
    public CitaApoyosVM? CitaApoyos { get; set; }
    public DateTime UltimoGuardado { get; set; } = DateTime.Now;

    /// <summary>Primer paso que falta por completar (1-5).</summary>
    public int PasoPendiente =>
        Identificacion is null ? 1 :
        Contacto is null ? 2 :
        Diagnostico is null ? 3 :
        CitaApoyos is null ? 4 : 5;
}

// ---------- Confirmación ----------
public class ConfirmacionVM
{
    public string NumeroTramite { get; set; } = string.Empty;
    public DateTime? FechaCita { get; set; }
    public string? CentroValoracion { get; set; }
}
