using System.ComponentModel.DataAnnotations;

namespace CONADIS.Web.Models.ViewModels;

public enum TipoCuenta
{
    Titular = 1,
    Tutor = 2,
    Empresa = 3
}

public class IniciarSesionVM
{
    [Required(ErrorMessage = "Escribe tu cédula.")]
    [RegularExpression(@"^\d{3}-?\d{7}-?\d{1}$", ErrorMessage = "La cédula debe tener 11 dígitos.")]
    public string Cedula { get; set; } = string.Empty;

    [Required(ErrorMessage = "Escribe tu contraseña.")]
    [DataType(DataType.Password)]
    public string Contrasena { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}

public class CrearCuentaVM
{
    [Required(ErrorMessage = "Elige cómo quieres registrarte.")]
    public TipoCuenta TipoCuenta { get; set; } = TipoCuenta.Titular;

    // Titular o tutor
    [RegularExpression(@"^\d{3}-?\d{7}-?\d{1}$", ErrorMessage = "La cédula debe tener 11 dígitos.")]
    public string? Cedula { get; set; }

    // Empresa
    [RegularExpression(@"^\d{9}$|^\d{11}$", ErrorMessage = "El RNC debe tener 9 u 11 dígitos.")]
    public string? Rnc { get; set; }

    [Required(ErrorMessage = "Escribe tu correo.")]
    [EmailAddress(ErrorMessage = "Correo no válido.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Escribe una contraseña.")]
    [MinLength(8, ErrorMessage = "Mínimo 8 caracteres.")]
    [DataType(DataType.Password)]
    public string Contrasena { get; set; } = string.Empty;

    [Compare(nameof(Contrasena), ErrorMessage = "Las contraseñas no coinciden.")]
    [DataType(DataType.Password)]
    public string ConfirmarContrasena { get; set; } = string.Empty;
}

public class OlvideContrasenaVM
{
    [Required(ErrorMessage = "Escribe tu correo.")]
    [EmailAddress(ErrorMessage = "Correo no válido.")]
    public string Email { get; set; } = string.Empty;
}
