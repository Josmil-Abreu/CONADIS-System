using System.Security.Cryptography;
using System.Text;

namespace CONADIS.Integracion.Negocio.Seguridad;

/// <summary>
/// Clave que identifica a un sistema cliente (Web, Caja, Core, instituciones) ante la API de Integración.
/// En seg.ClienteApi se guarda su SHA-256, nunca la clave en claro.
/// </summary>
public static class ApiKey
{
    /// <summary>Encabezado HTTP que transporta la clave.</summary>
    public const string Encabezado = "X-Api-Key";

    /// <summary>SHA-256 en hexadecimal (64 caracteres en mayúscula).</summary>
    public static string Hashear(string clave)
    {
        ArgumentException.ThrowIfNullOrEmpty(clave);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clave)));
    }

    /// <summary>Compara la clave recibida con el hash configurado, en tiempo constante.</summary>
    public static bool Coincide(string? claveRecibida, string? hashConfigurado)
    {
        if (string.IsNullOrEmpty(claveRecibida) || string.IsNullOrEmpty(hashConfigurado)) return false;
        var calculado = Encoding.ASCII.GetBytes(Hashear(claveRecibida));
        var esperado = Encoding.ASCII.GetBytes(hashConfigurado.ToUpperInvariant());
        return CryptographicOperations.FixedTimeEquals(calculado, esperado);
    }

    /// <summary>Genera una clave nueva aleatoria.</summary>
    public static string Generar()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}