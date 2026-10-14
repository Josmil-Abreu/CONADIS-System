using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CONADIS.Integracion.Negocio.Modelos;

namespace CONADIS.Integracion.Negocio.Externos;

/// <summary>
/// Proveedor de firma digital simulado pero criptográficamente real (RF-INT-03):
///  - Calcula el SHA-256 del contenido.
///  - Lo firma con una clave ECDSA P-256 (algoritmo ES256, el que pide el SRS para JWS).
///  - Devuelve un JWS compacto: base64url(encabezado).base64url(carga).base64url(firma),
///    donde la carga lleva la huella SHA-256 y el sello de tiempo (iat).
/// La clave privada se guarda en un archivo PEM (se crea la primera vez). En producción sería
/// el certificado de una entidad acreditada por INDOTEL dentro de un HSM.
/// </summary>
public sealed class FirmaEcdsa : IProveedorFirma, IDisposable
{
    private readonly ECDsa _clave;
    private readonly OpcionesSimulacion _simulacion;
    private readonly string _idCertificado;

    public DateOnly VencimientoCertificado { get; }

    /// <param name="rutaClave">Archivo PEM con la clave privada (se genera si no existe).</param>
    /// <param name="vencimiento">Vencimiento del "certificado" (alertas 30 días antes).</param>
    public FirmaEcdsa(string rutaClave, DateOnly vencimiento, OpcionesSimulacion simulacion)
    {
        _simulacion = simulacion;
        VencimientoCertificado = vencimiento;
        _clave = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        if (File.Exists(rutaClave))
        {
            _clave.ImportFromPem(File.ReadAllText(rutaClave));
        }
        else
        {
            var carpeta = Path.GetDirectoryName(Path.GetFullPath(rutaClave));
            if (!string.IsNullOrEmpty(carpeta)) Directory.CreateDirectory(carpeta);
            File.WriteAllText(rutaClave, _clave.ExportECPrivateKeyPem());
        }

        _idCertificado = Convert.ToHexString(SHA256.HashData(_clave.ExportSubjectPublicKeyInfo()))[..16];
    }

    public async Task<FirmaDocumento> FirmarAsync(string contenido, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(contenido);
        await Simulacion.EsperarAsync(_simulacion, _simulacion.FirmaDisponible, "Firma", ct);

        if (DateOnly.FromDateTime(DateTime.Today) > VencimientoCertificado)
            throw new InvalidOperationException("El certificado de firma está vencido: no se puede firmar.");

        var huella = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contenido)));
        var sello = DateTimeOffset.UtcNow;

        var encabezado = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", typ = "JWT", kid = _idCertificado }));
        var carga = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { sha256 = huella, iat = sello.ToUnixTimeSeconds(), iss = "CONADIS" }));
        var firma = _clave.SignData(Encoding.ASCII.GetBytes($"{encabezado}.{carga}"), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return new FirmaDocumento($"{encabezado}.{carga}.{Base64Url(firma)}", "ES256", huella, sello, _idCertificado);
    }

    /// <summary>Comprueba que la firma es nuestra y que corresponde exactamente a ese contenido.</summary>
    public bool Verificar(string contenido, string firma)
    {
        try
        {
            var partes = firma.Split('.');
            if (partes.Length != 3) return false;

            var firmaBytes = DesdeBase64Url(partes[2]);
            if (!_clave.VerifyData(Encoding.ASCII.GetBytes($"{partes[0]}.{partes[1]}"), firmaBytes, HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                return false;

            using var carga = JsonDocument.Parse(DesdeBase64Url(partes[1]));
            var huella = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contenido)));
            return carga.RootElement.GetProperty("sha256").GetString() == huella;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or KeyNotFoundException or CryptographicException)
        {
            return false;
        }
    }

    public void Dispose() => _clave.Dispose();

    private static string Base64Url(byte[] datos) => Convert.ToBase64String(datos).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DesdeBase64Url(string texto)
    {
        var s = texto.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
