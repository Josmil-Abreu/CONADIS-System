namespace CONADIS.Integracion.Negocio.Seguridad;

/// <summary>
/// Formato y dígito verificador de la cédula dominicana (RF-CORE-01, paso 1).
/// El dígito 11 se calcula con el algoritmo de Luhn sobre los 10 primeros:
/// pesos 1,2,1,2...; si un producto pasa de 9 se le restan 9; verificador = (10 - suma % 10) % 10.
/// </summary>
public static class ValidadorCedula
{
    /// <summary>Quita guiones y espacios: "001-1234567-8" -> "00112345678".</summary>
    public static string Normalizar(string? cedula)
        => new((cedula ?? string.Empty).Where(char.IsDigit).ToArray());

    public static bool EsValida(string? cedula)
    {
        var c = Normalizar(cedula);
        if (c.Length != 11 || c == "00000000000") return false;
        return c[10] - '0' == CalcularDigito(c[..10]);
    }

    /// <summary>Dígito verificador para los 10 primeros dígitos.</summary>
    public static int CalcularDigito(string diezDigitos)
    {
        if (diezDigitos.Length != 10 || !diezDigitos.All(char.IsDigit))
            throw new ArgumentException("Se necesitan exactamente 10 dígitos.", nameof(diezDigitos));

        var suma = 0;
        for (var i = 0; i < 10; i++)
        {
            var producto = (diezDigitos[i] - '0') * (i % 2 == 0 ? 1 : 2);
            suma += producto > 9 ? producto - 9 : producto;
        }
        return (10 - suma % 10) % 10;
    }

    /// <summary>"00112345678" -> "001-1234567-8"</summary>
    public static string Formatear(string cedula)
    {
        var c = Normalizar(cedula);
        return c.Length == 11 ? $"{c[..3]}-{c[3..10]}-{c[10]}" : cedula;
    }

    /// <summary>"Juan Pérez Gómez" -> "J*** P*** G***" (verificación pública, RINV-02).</summary>
    public static string EnmascararNombre(string? nombreCompleto)
        => string.Join(' ', (nombreCompleto ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => $"{char.ToUpperInvariant(p[0])}***"));
}