using System.Security.Cryptography;
using System.Text;

namespace CONADIS.Integracion.Negocio.Seguridad;

/// <summary>
/// Las API keys identifican a cada módulo cliente (WEB, CAJA) ante Integración.
/// En la base de datos solo se guarda su SHA-256 (nunca la clave en claro).
/// </summary>
public static class ApiKey
{
    /// <summary>Encabezado HTTP que transporta la clave.</summary>
    public const string Encabezado = "X-Api-Key";

    /// <summary>
    /// SHA-256 en hexadecimal mayúscula (64 caracteres). Da lo mismo que
    /// CONVERT(CHAR(64), HASHBYTES('SHA2_256', ...), 2) en SQL Server.
    /// </summary>
    public static string Hashear(string clave)
    {
        ArgumentException.ThrowIfNullOrEmpty(clave);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clave)));
    }

    /// <summary>Genera una clave nueva aleatoria (256 bits) para dar de alta o rotar un cliente.</summary>
    public static string Generar()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
}

/// <summary>SHA-256 de cualquier texto (lo usa la idempotencia para comparar cuerpos).</summary>
public static class Huella
{
    public static string Calcular(string? texto)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto ?? string.Empty)));
}
