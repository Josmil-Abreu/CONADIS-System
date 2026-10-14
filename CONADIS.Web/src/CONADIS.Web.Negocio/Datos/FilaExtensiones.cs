using System.Data;

namespace CONADIS.Web.Negocio.Datos;

/// <summary>Lectura segura de columnas de un DataRow (maneja DBNull y columnas ausentes).</summary>
public static class FilaExtensiones
{
    public static string Texto(this DataRow fila, string columna)
        => Leer(fila, columna) is { } v ? Convert.ToString(v) ?? string.Empty : string.Empty;

    public static string? TextoONulo(this DataRow fila, string columna)
        => Leer(fila, columna) is { } v ? Convert.ToString(v) : null;

    public static int Entero(this DataRow fila, string columna)
        => Leer(fila, columna) is { } v ? Convert.ToInt32(v) : 0;

    public static int? EnteroONulo(this DataRow fila, string columna)
        => Leer(fila, columna) is { } v ? Convert.ToInt32(v) : null;

    public static long EnteroLargo(this DataRow fila, string columna)
        => Leer(fila, columna) is { } v ? Convert.ToInt64(v) : 0;

    public static decimal Decimal(this DataRow fila, string columna)
        => Leer(fila, columna) is { } v ? Convert.ToDecimal(v) : 0m;

    public static decimal? DecimalONulo(this DataRow fila, string columna)
        => Leer(fila, columna) is { } v ? Convert.ToDecimal(v) : null;

    public static bool Booleano(this DataRow fila, string columna)
        => Leer(fila, columna) is { } v && Convert.ToBoolean(v);

    /// <summary>Fechas DATETIMEOFFSET, DATETIME2 o DATE convertidas a DateTimeOffset.</summary>
    public static DateTimeOffset FechaHora(this DataRow fila, string columna)
        => FechaHoraONula(fila, columna) ?? DateTimeOffset.MinValue;

    public static DateTimeOffset? FechaHoraONula(this DataRow fila, string columna) => Leer(fila, columna) switch
    {
        DateTimeOffset d => d,
        DateTime d => new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Local)),
        _ => null
    };

    public static DateOnly? FechaONula(this DataRow fila, string columna) => Leer(fila, columna) switch
    {
        DateTime d => DateOnly.FromDateTime(d),
        DateTimeOffset d => DateOnly.FromDateTime(d.DateTime),
        DateOnly d => d,
        _ => null
    };

    public static byte[]? Binario(this DataRow fila, string columna)
        => Leer(fila, columna) as byte[];

    public static Guid Guid(this DataRow fila, string columna)
        => Leer(fila, columna) is Guid g ? g : System.Guid.Empty;

    private static object? Leer(DataRow fila, string columna)
    {
        if (!fila.Table.Columns.Contains(columna)) return null;
        var valor = fila[columna];
        return valor is DBNull ? null : valor;
    }
}