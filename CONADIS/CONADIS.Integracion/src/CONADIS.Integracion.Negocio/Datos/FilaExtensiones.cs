using System.Data;

namespace CONADIS.Integracion.Negocio.Datos;

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

    public static bool Booleano(this DataRow fila, string columna)
        => Leer(fila, columna) is { } v && Convert.ToBoolean(v);

    public static DateTime Fecha(this DataRow fila, string columna)
        => Leer(fila, columna) is { } v ? DateTime.SpecifyKind(Convert.ToDateTime(v), DateTimeKind.Utc) : DateTime.MinValue;

    public static DateTime? FechaONula(this DataRow fila, string columna)
        => Leer(fila, columna) is { } v ? DateTime.SpecifyKind(Convert.ToDateTime(v), DateTimeKind.Utc) : null;

    public static Guid Guid(this DataRow fila, string columna)
        => Leer(fila, columna) is Guid g ? g : System.Guid.Empty;

    public static Guid? GuidONulo(this DataRow fila, string columna)
        => Leer(fila, columna) is Guid g ? g : null;

    private static object? Leer(DataRow fila, string columna)
    {
        if (!fila.Table.Columns.Contains(columna)) return null;
        var valor = fila[columna];
        return valor is DBNull ? null : valor;
    }
}
