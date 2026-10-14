namespace CONADIS.Integracion.Negocio.Simuladores;

/// <summary>Dígitos verificadores dominicanos (cédula y RNC) y de tarjetas (Luhn).</summary>
public static class Digitos
{
    /// <summary>
    /// Cédula dominicana: 11 dígitos; el último se calcula con pesos 1,2,1,2... sobre los
    /// primeros 10, sumando los dígitos de cada producto (módulo 10).
    /// </summary>
    public static bool CedulaValida(string? cedula)
    {
        if (cedula is null || cedula.Length != 11 || !cedula.All(char.IsAsciiDigit)) return false;
        return DigitoCedula(cedula[..10]) == cedula[10] - '0';
    }

    /// <summary>Calcula el dígito verificador para los 10 primeros dígitos de una cédula.</summary>
    public static int DigitoCedula(string diezDigitos)
    {
        var suma = 0;
        for (var i = 0; i < 10; i++)
        {
            var producto = (diezDigitos[i] - '0') * (i % 2 == 0 ? 1 : 2);
            suma += producto > 9 ? producto - 9 : producto;
        }
        return (10 - suma % 10) % 10;
    }

    /// <summary>RNC: 9 dígitos, pesos 7,9,8,6,5,4,3,2 sobre los 8 primeros (módulo 11).</summary>
    public static bool RncValido(string? rnc)
    {
        if (rnc is null || rnc.Length != 9 || !rnc.All(char.IsAsciiDigit)) return false;
        int[] pesos = [7, 9, 8, 6, 5, 4, 3, 2];
        var suma = 0;
        for (var i = 0; i < 8; i++) suma += (rnc[i] - '0') * pesos[i];
        var resto = suma % 11;
        var digito = resto switch { 0 => 2, 1 => 1, _ => 11 - resto };
        return digito == rnc[8] - '0';
    }

    /// <summary>Algoritmo de Luhn para números de tarjeta.</summary>
    public static bool TarjetaValida(string? numero)
    {
        if (numero is null) return false;
        var limpio = new string(numero.Where(c => c != ' ' && c != '-').ToArray());
        if (limpio.Length is < 13 or > 19 || !limpio.All(char.IsAsciiDigit)) return false;

        var suma = 0;
        var doblar = false;
        for (var i = limpio.Length - 1; i >= 0; i--)
        {
            var d = limpio[i] - '0';
            if (doblar) { d *= 2; if (d > 9) d -= 9; }
            suma += d;
            doblar = !doblar;
        }
        return suma % 10 == 0;
    }
}
