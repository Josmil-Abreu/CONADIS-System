using System.Text.Json;
using CONADIS.Integracion.Negocio.Simuladores;

namespace CONADIS.Integracion.Negocio.Servicios;

/// <summary>
/// Validaciones de entrada comunes. Lanzan <see cref="SolicitudInvalidaException"/> con un
/// mensaje para el usuario. Los procedimientos almacenados vuelven a validar: esto solo
/// da mensajes más claros y evita viajes inútiles a la base.
/// </summary>
internal static class Validaciones
{
    public static string Cedula(string? cedula)
    {
        var limpia = (cedula ?? string.Empty).Replace("-", "").Trim();
        if (limpia.Length != 11 || !limpia.All(char.IsAsciiDigit))
            throw new SolicitudInvalidaException("La cédula debe tener 11 dígitos.");
        return limpia;
    }

    public static string Rnc(string? rnc)
    {
        var limpio = (rnc ?? string.Empty).Replace("-", "").Trim();
        if (limpio.Length != 9 || !limpio.All(char.IsAsciiDigit))
            throw new SolicitudInvalidaException("El RNC debe tener 9 dígitos.");
        return limpio;
    }

    /// <summary>Cédula (11) o RNC (9).</summary>
    public static string Documento(string? documento)
    {
        var limpio = (documento ?? string.Empty).Replace("-", "").Trim();
        if (limpio.Length is not (9 or 11) || !limpio.All(char.IsAsciiDigit))
            throw new SolicitudInvalidaException("El documento debe ser una cédula (11 dígitos) o un RNC (9 dígitos).");
        return limpio;
    }

    public static string Requerido(string? valor, string campo, int maximo)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new SolicitudInvalidaException($"El campo {campo} es obligatorio.");
        var limpio = valor.Trim();
        if (limpio.Length > maximo)
            throw new SolicitudInvalidaException($"El campo {campo} admite como máximo {maximo} caracteres.");
        return limpio;
    }

    public static string? Opcional(string? valor, string campo, int maximo)
        => string.IsNullOrWhiteSpace(valor) ? null : Requerido(valor, campo, maximo);

    public static void Monto(decimal monto, string campo, bool permitirCero = false)
    {
        if (monto < 0 || (!permitirCero && monto == 0))
            throw new SolicitudInvalidaException($"El campo {campo} debe ser mayor que cero.");
        if (decimal.Round(monto, 2) != monto)
            throw new SolicitudInvalidaException($"El campo {campo} admite como máximo 2 decimales.");
    }

    public static void Rango(DateTime desde, DateTime hasta)
    {
        if (hasta <= desde)
            throw new SolicitudInvalidaException("La fecha final debe ser posterior a la inicial.");
        if ((hasta - desde).TotalDays > 366)
            throw new SolicitudInvalidaException("El rango de fechas no puede pasar de un año.");
    }

    public static void Json(string? json, string campo)
    {
        if (json is null) return;
        try
        {
            using var _ = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new SolicitudInvalidaException($"El campo {campo} debe ser JSON válido.");
        }
    }

    public static string Tarjeta(string? numero)
    {
        if (!Digitos.TarjetaValida(numero))
            throw new SolicitudInvalidaException("El número de tarjeta no es válido.");
        return new string(numero!.Where(char.IsAsciiDigit).ToArray());
    }
}
