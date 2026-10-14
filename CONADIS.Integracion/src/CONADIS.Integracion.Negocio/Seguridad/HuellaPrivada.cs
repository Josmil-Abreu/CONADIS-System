using System.Security.Cryptography;
using System.Text;

namespace CONADIS.Integracion.Negocio.Seguridad;

/// <summary>
/// Huella HMAC-SHA256 de un dato personal (cédula, RNC...). Integración la guarda en la bitácora
/// y en la caché en lugar del dato en claro: permite buscar "todas las consultas sobre esta cédula"
/// sin que alguien con acceso a la base pueda leer las cédulas.
/// Se usa HMAC con una clave secreta (y no SHA-256 simple) porque solo hay 10^10 cédulas posibles:
/// sin la clave sería fácil calcularlas todas y revertir la huella.
/// </summary>
public sealed class HuellaPrivada
{
    private readonly byte[] _clave;

    /// <param name="claveSecreta">Mínimo 32 caracteres. Se configura en appsettings (Seguridad:ClaveHuella).</param>
    public HuellaPrivada(string? claveSecreta)
    {
        if (string.IsNullOrWhiteSpace(claveSecreta) || claveSecreta.Length < 32)
            throw new ArgumentException("La clave de huellas (Seguridad:ClaveHuella) debe tener al menos 32 caracteres.", nameof(claveSecreta));
        _clave = Encoding.UTF8.GetBytes(claveSecreta);
    }

    /// <summary>64 caracteres hexadecimales en mayúscula. Normaliza (sin guiones ni espacios) antes de calcular.</summary>
    public string Calcular(string tipo, string? valor)
    {
        var normal = new string((valor ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return Convert.ToHexString(HMACSHA256.HashData(_clave, Encoding.UTF8.GetBytes($"{tipo}:{normal}")));
    }

    public string Cedula(string? cedula) => Calcular("CED", cedula);
}
