using System.Security.Cryptography;
using System.Text;

namespace CONADIS.Caja.Negocio.Seguridad;

/// <summary>
/// Hash de contraseñas con PBKDF2-SHA256 y salt aleatorio por usuario.
/// Formato guardado en seg.Usuario.PasswordHash:  PBKDF2-SHA256$iteraciones$saltBase64$hashBase64
/// Como cada usuario tiene su propio salt, la comparación se hace aquí y NO dentro del SP.
/// </summary>
public static class HasherContrasena
{
    private const string Algoritmo = "PBKDF2-SHA256";
    private const int Iteraciones = 600_000; // recomendación OWASP para PBKDF2-SHA256
    private const int TamanoSalt = 16;
    private const int TamanoHash = 32;

    // Hash válido de una contraseña aleatoria: se usa para gastar el mismo tiempo cuando el
    // usuario no existe, y así no revelar qué usuarios existen midiendo la respuesta.
    private static readonly Lazy<string> HashFicticio = new(() => Hashear(Guid.NewGuid().ToString()));

    public static string Hashear(string contrasena)
    {
        ArgumentException.ThrowIfNullOrEmpty(contrasena);
        var salt = RandomNumberGenerator.GetBytes(TamanoSalt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(contrasena), salt, Iteraciones,
            HashAlgorithmName.SHA256, TamanoHash);
        return $"{Algoritmo}${Iteraciones}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>True si la contraseña corresponde al hash. Nunca lanza excepciones.</summary>
    public static bool Verificar(string contrasena, string? hashGuardado)
    {
        if (string.IsNullOrEmpty(contrasena) || string.IsNullOrWhiteSpace(hashGuardado)) return false;
        try
        {
            var partes = hashGuardado.Split('$');
            if (partes.Length != 4 || partes[0] != Algoritmo) return false;

            var iteraciones = int.Parse(partes[1]);
            var salt = Convert.FromBase64String(partes[2]);
            var esperado = Convert.FromBase64String(partes[3]);
            var calculado = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(contrasena), salt, iteraciones,
                HashAlgorithmName.SHA256, esperado.Length);

            // Comparación en tiempo constante
            return CryptographicOperations.FixedTimeEquals(calculado, esperado);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return false; // hash dañado o en otro formato (por ejemplo, texto plano antiguo)
        }
    }

    /// <summary>Gasta el mismo tiempo que una verificación real (usuario inexistente).</summary>
    public static void SimularVerificacion(string contrasena) => Verificar(contrasena ?? "x", HashFicticio.Value);

    /// <summary>
    /// Personal interno: 8+ caracteres con mayúscula, minúscula y número.
    /// (El portal ciudadano exige 12, RNF-SEG-03.) Devuelve null si es válida o el mensaje si no.
    /// </summary>
    public static string? ValidarFortaleza(string? contrasena, int longitudMinima = 8)
    {
        if (string.IsNullOrEmpty(contrasena) || contrasena.Length < longitudMinima)
            return $"La contraseña debe tener al menos {longitudMinima} caracteres.";
        if (!contrasena.Any(char.IsUpper)) return "La contraseña debe tener al menos una letra mayúscula.";
        if (!contrasena.Any(char.IsLower)) return "La contraseña debe tener al menos una letra minúscula.";
        if (!contrasena.Any(char.IsDigit)) return "La contraseña debe tener al menos un número.";
        return null;
    }
}