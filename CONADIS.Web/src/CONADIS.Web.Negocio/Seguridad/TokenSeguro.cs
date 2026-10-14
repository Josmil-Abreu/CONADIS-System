using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;

namespace CONADIS.Web.Negocio.Seguridad;

/// <summary>
/// Tokens de un solo uso (verificación de correo, restablecer contraseña).
/// Al ciudadano se le envía el token; en la base solo se guarda su SHA-256: aunque alguien lea la
/// base de datos, no puede usar los enlaces.
/// </summary>
public static class TokenSeguro
{
    public static (string Token, string Hash) Generar()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (token, Hashear(token));
    }

    public static string Hashear(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
}

/// <summary>Validaciones de formularios del portal.</summary>
public static class ValidacionesPortal
{
    public static bool CorreoValido(string? correo)
        => !string.IsNullOrWhiteSpace(correo) && correo.Length <= 150 && MailAddress.TryCreate(correo.Trim(), out var m)
           && m.Address == correo.Trim() && m.Host.Contains('.');

    /// <summary>Teléfono dominicano: 10 dígitos que empiezan por 809, 829 u 849 (guiones y espacios permitidos).</summary>
    public static bool TelefonoValido(string? telefono)
    {
        var d = new string((telefono ?? string.Empty).Where(char.IsDigit).ToArray());
        return d.Length == 10 && (d.StartsWith("809") || d.StartsWith("829") || d.StartsWith("849"));
    }
}
