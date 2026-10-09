using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using CONADIS.Application.Dtos;

namespace CONADIS.Web.Models.ViewModels;

// ================= Trámites =================
public class TramiteFilaVM
{
    public string Numero { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;      // "Certificación y carnet", "Ayuda técnica"...
    public string Persona { get; set; } = "Yo";
    public string Estado { get; set; } = string.Empty;
    public DateTime Actualizado { get; set; }
}

public class MisTramitesVM
{
    public string? FiltroEstado { get; set; }
    public List<TramiteFilaVM> Tramites { get; set; } = new();
    public string? EstadoCarnet { get; set; }             // "Aún no emitido", "Vigente"...
    public DateTime? ProximaCita { get; set; }
    public string? CentroProximaCita { get; set; }
    public int AccionesPendientes { get; set; }
    public List<string> AvisosRecientes { get; set; } = new();
}

public class DetalleTramiteVM
{
    [ValidateNever] public ExpedienteDetalleDto Expediente { get; set; } = default!;
    public bool PuedeApelar => Expediente.Estado == "Rechazado";
    public bool TieneCarnet => !string.IsNullOrEmpty(Expediente.NumeroCarnet);
}

public class ConsultarTramiteVM
{
    [Required(ErrorMessage = "Escribe el número de trámite.")]
    public string NumeroTramite { get; set; } = string.Empty;

    [Required(ErrorMessage = "Escribe la cédula.")]
    [RegularExpression(@"^\d{3}-?\d{7}-?\d{1}$", ErrorMessage = "La cédula debe tener 11 dígitos.")]
    public string Cedula { get; set; } = string.Empty;

    public ExpedienteDetalleDto? Resultado { get; set; }
}

public class CambiarCitaVM
{
    public string NumeroTramite { get; set; } = string.Empty;
    [Required(ErrorMessage = "Elige la nueva fecha.")] public DateTime? NuevaFecha { get; set; }
}

public class ApelacionVM
{
    public string NumeroTramite { get; set; } = string.Empty;

    [Required(ErrorMessage = "Explica el motivo de tu apelación.")]
    [MinLength(20, ErrorMessage = "Escribe al menos 20 caracteres.")]
    public string Motivo { get; set; } = string.Empty;

    public IFormFile? DocumentoSoporte { get; set; }
}

// ================= Carnet =================
public class MiCarnetVM
{
    public string NombreCompleto { get; set; } = string.Empty;
    public string CedulaEnmascarada { get; set; } = string.Empty;   // ***-***0000-1
    public string NumeroCarnet { get; set; } = string.Empty;
    public DateOnly FechaEmision { get; set; }
    public DateOnly? FechaVencimiento { get; set; }                  // null = permanente
    public string Estado { get; set; } = string.Empty;
    public string CodigoVerificacion { get; set; } = string.Empty;
    public string UrlVerificacion { get; set; } = string.Empty;
    public string? TipoDiscapacidad { get; set; }
    public string? Grado { get; set; }
    public string? NumeroCertificado { get; set; }
}

public class SolicitarDuplicadoVM
{
    [Required(ErrorMessage = "Indica el motivo.")]
    public string Motivo { get; set; } = string.Empty;     // Pérdida, Robo, Deterioro
}

// ================= Verificación =================
public enum ResultadoVerificacion { NoConsultado, Vigente, Vencido, Anulado, NoEncontrado }

public class VerificacionVM
{
    [Display(Name = "Código de verificación")]
    public string? Codigo { get; set; }

    public ResultadoVerificacion Resultado { get; set; } = ResultadoVerificacion.NoConsultado;
    public string? TipoDocumento { get; set; }
    public string? TitularParcial { get; set; }          // J*** P*** R***
    public DateOnly? FechaEmision { get; set; }
    public DateOnly? ValidoHasta { get; set; }
    public bool FirmaValida { get; set; }
}

// ================= Ayudas técnicas =================
public enum CategoriaAyuda { Movilidad = 1, Audicion = 2, Vision = 3, Comunicacion = 4, Otra = 5 }
public enum FormaEntrega { RetiroEnSede = 1, Domicilio = 2 }

public class SolicitudAyudaVM
{
    public string? NombreTitular { get; set; }
    public bool CarnetVigente { get; set; }

    [Required(ErrorMessage = "Elige el tipo de ayuda.")]
    public CategoriaAyuda? Categoria { get; set; }

    [Required(ErrorMessage = "Elige el producto.")]
    public string Producto { get; set; } = string.Empty;

    [Required(ErrorMessage = "Cuéntanos para qué la necesitas.")]
    public string Justificacion { get; set; } = string.Empty;
    public IFormFile? AudioJustificacion { get; set; }

    public IFormFile? PrescripcionMedica { get; set; }

    [Required(ErrorMessage = "Indica tu situación laboral.")]
    public string SituacionLaboral { get; set; } = string.Empty;

    [Required(ErrorMessage = "Indica el ingreso del hogar.")]
    public string IngresoMensualHogar { get; set; } = string.Empty;

    public bool RecibioAyudaSimilar { get; set; }

    [Required] public FormaEntrega FormaEntrega { get; set; } = FormaEntrega.RetiroEnSede;
}

// ================= Pagos =================
public class OrdenPagoVM
{
    public string NumeroOrden { get; set; } = string.Empty;
    public string Concepto { get; set; } = string.Empty;    // "Duplicado de carnet", "Certificación de accesibilidad"
    public decimal Monto { get; set; }
    public string? Referencia { get; set; }                 // número de trámite o carnet
    public string Estado { get; set; } = "Pendiente";       // Pendiente, Pagada, Fallida
}

// ================= Empresas =================
public class CertificacionAccesibilidadVM
{
    // Se llenan en el servidor; no se validan desde el formulario
    [ValidateNever] public string RazonSocial { get; set; } = string.Empty;
    [ValidateNever] public string Rnc { get; set; } = string.Empty;
    public bool RncActivo { get; set; }
    public decimal MontoTasa { get; set; }

    [Required(ErrorMessage = "Elige el tipo de inmueble.")] public string TipoInmueble { get; set; } = string.Empty;
    [Required(ErrorMessage = "Elige la etapa.")] public string Etapa { get; set; } = string.Empty;
    [Required(ErrorMessage = "Escribe el nombre de la obra.")] public string NombreObra { get; set; } = string.Empty;
    [Required(ErrorMessage = "Escribe la dirección.")] public string Direccion { get; set; } = string.Empty;
    [Required(ErrorMessage = "Elige la provincia.")] public string Provincia { get; set; } = string.Empty;

    [Range(1, 1_000_000, ErrorMessage = "Área no válida.")]
    public decimal AreaM2 { get; set; }

    public List<IFormFile> Planos { get; set; } = new();

    [Required(ErrorMessage = "Escribe el nombre del contacto.")] public string ContactoNombre { get; set; } = string.Empty;
    [Required, Phone] public string ContactoTelefono { get; set; } = string.Empty;
    [Required, EmailAddress] public string ContactoEmail { get; set; } = string.Empty;

    /// <summary>"pagar" = Continuar al pago, "ventanilla" = Pagar después.</summary>
    public string Accion { get; set; } = "pagar";
}
