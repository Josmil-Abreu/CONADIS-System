using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CONADIS.Web.Negocio.Seguridad;

/// <summary>
/// Pase de sesión que entrega CONADIS.Inicio (el login único) al módulo que abre, para no pedir la
/// contraseña dos veces. Es un texto firmado con HMAC-SHA256 que dice: "este usuario ya se autenticó,
/// para este módulo, y el pase vence en 60 segundos".
///   Formato:  base64url(json {m, u, exp, n})  "."  base64url(firma)
/// El Inicio y el módulo comparten la clave (appsettings: Inicio:ClaveTraspaso, 32+ caracteres).
/// Cada módulo tiene su propia copia de esta clase (los módulos no se referencian entre sí).
/// </summary>
public static class PaseInicio
{
    public static readonly TimeSpan VigenciaPredeterminada = TimeSpan.FromSeconds(60);

    private sealed record Contenido(string m, string u, long exp, string n);

    /// <param name="modulo">"Core", "Web" o "Caja": un pase para un módulo no sirve en otro.</param>
    /// <param name="identificador">Usuario (Core, Caja) o cédula (Web).</param>
    public static string Emitir(string modulo, string identificador, string clave, TimeSpan? vigencia = null, DateTimeOffset? ahora = null)
    {
        ValidarClave(clave);
        var vence = (ahora ?? DateTimeOffset.UtcNow).Add(vigencia ?? VigenciaPredeterminada).ToUnixTimeSeconds();
        var json = JsonSerializer.SerializeToUtf8Bytes(new Contenido(modulo, identificador, vence, Convert.ToHexString(RandomNumberGenerator.GetBytes(8))));
        var cuerpo = Base64Url(json);
        return $"{cuerpo}.{Base64Url(Firmar(cuerpo, clave))}";
    }

    /// <summary>Devuelve el identificador si el pase es auténtico, es para este módulo y no ha vencido; si no, null.</summary>
    public static string? Validar(string? pase, string modulo, string clave, DateTimeOffset? ahora = null)
    {
        ValidarClave(clave);
        if (string.IsNullOrWhiteSpace(pase) || pase.Length > 2048) return null;
        var partes = pase.Split('.');
        if (partes.Length != 2) return null;

        byte[] firma;
        Contenido? c;
        try
        {
            firma = DesdeBase64Url(partes[1]);
            c = JsonSerializer.Deserialize<Contenido>(DesdeBase64Url(partes[0]));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(firma, Firmar(partes[0], clave))) return null;
        if (c is null || c.m != modulo || string.IsNullOrWhiteSpace(c.u)) return null;
        if (DateTimeOffset.FromUnixTimeSeconds(c.exp) < (ahora ?? DateTimeOffset.UtcNow)) return null;
        return c.u;
    }

    private static byte[] Firmar(string cuerpo, string clave)
        => HMACSHA256.HashData(Encoding.UTF8.GetBytes(clave), Encoding.ASCII.GetBytes(cuerpo));

    private static void ValidarClave(string clave)
    {
        if (string.IsNullOrEmpty(clave) || clave.Length < 32)
            throw new InvalidOperationException("Inicio:ClaveTraspaso debe tener al menos 32 caracteres (la misma en CONADIS.Inicio y en el módulo).");
    }

    private static string Base64Url(byte[] datos) => Convert.ToBase64String(datos).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DesdeBase64Url(string texto)
    {
        var b = texto.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b.PadRight(b.Length + (4 - b.Length % 4) % 4, '='));
    }
}
