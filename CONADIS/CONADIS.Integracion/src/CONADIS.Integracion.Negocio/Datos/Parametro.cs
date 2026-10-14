using System.Data;
using Microsoft.Data.SqlClient;

namespace CONADIS.Integracion.Negocio.Datos;

/// <summary>
/// Fábrica de parámetros tipados para los procedimientos almacenados.
/// Los valores viajan como SqlParameter: SQL Server nunca los interpreta como código,
/// así que no hay riesgo de SQL Injection.
/// </summary>
public static class Parametro
{
    public static SqlParameter Texto(string nombre, string? valor, int tamano = -1)
        => Crear(nombre, SqlDbType.NVarChar, valor, tamano);

    public static SqlParameter Entero(string nombre, int? valor)
        => Crear(nombre, SqlDbType.Int, valor);

    public static SqlParameter EnteroLargo(string nombre, long? valor)
        => Crear(nombre, SqlDbType.BigInt, valor);

    public static SqlParameter Decimal(string nombre, decimal? valor, byte precision = 18, byte escala = 2)
    {
        var p = Crear(nombre, SqlDbType.Decimal, valor);
        p.Precision = precision;
        p.Scale = escala;
        return p;
    }

    public static SqlParameter Booleano(string nombre, bool? valor)
        => Crear(nombre, SqlDbType.Bit, valor);

    public static SqlParameter Fecha(string nombre, DateTime? valor)
        => Crear(nombre, SqlDbType.DateTime2, valor);

    public static SqlParameter Guid(string nombre, Guid? valor)
        => Crear(nombre, SqlDbType.UniqueIdentifier, valor);

    /// <summary>Hash SHA-256 en hexadecimal (CHAR(64)).</summary>
    public static SqlParameter Hash(string nombre, string? valor)
        => Crear(nombre, SqlDbType.Char, valor, 64);

    /// <summary>Parámetro OUTPUT. Su valor se lee después de ejecutar el SP con <c>parametro.Value</c>.</summary>
    public static SqlParameter Salida(string nombre, SqlDbType tipo, int tamano = 0)
    {
        ValidarNombre(nombre);
        var p = new SqlParameter(nombre, tipo) { Direction = ParameterDirection.Output };
        if (tamano != 0) p.Size = tamano;
        return p;
    }

    private static SqlParameter Crear(string nombre, SqlDbType tipo, object? valor, int tamano = 0)
    {
        ValidarNombre(nombre);
        var p = new SqlParameter(nombre, tipo) { Value = valor ?? DBNull.Value };
        if (tamano != 0) p.Size = tamano;
        return p;
    }

    private static void ValidarNombre(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre) || !nombre.StartsWith('@'))
            throw new ArgumentException("El nombre del parámetro debe empezar con '@'.", nameof(nombre));
    }
}

/// <summary>Lectura de parámetros OUTPUT sin pelear con DBNull.</summary>
public static class SalidaExtensiones
{
    public static string? Texto(this SqlParameter p) => p.Value is DBNull or null ? null : Convert.ToString(p.Value);
    public static int? Entero(this SqlParameter p) => p.Value is DBNull or null ? null : Convert.ToInt32(p.Value);
    public static long? EnteroLargo(this SqlParameter p) => p.Value is DBNull or null ? null : Convert.ToInt64(p.Value);
    public static bool? Booleano(this SqlParameter p) => p.Value is DBNull or null ? null : Convert.ToBoolean(p.Value);
    public static Guid? Guid(this SqlParameter p) => p.Value is System.Guid g ? g : null;
}
