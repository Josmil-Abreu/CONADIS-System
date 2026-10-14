using System.Data;
using Microsoft.Data.SqlClient;

namespace CONADIS.Integracion.Negocio.Datos;

/// <summary>
/// Fábrica de parámetros tipados para los Stored Procedures.
/// Los valores viajan como SqlParameter: SQL Server nunca los interpreta como código,
/// por eso no existe riesgo de SQL Injection.
/// </summary>
public static class Parametro
{
    public static SqlParameter Texto(string nombre, string? valor, int tamano = -1)
        => Crear(nombre, SqlDbType.NVarChar, valor, tamano);

    public static SqlParameter TextoAnsi(string nombre, string? valor, int tamano = -1)
        => Crear(nombre, SqlDbType.VarChar, valor, tamano);

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

    public static SqlParameter FechaHora(string nombre, DateTimeOffset? valor)
        => Crear(nombre, SqlDbType.DateTimeOffset, valor);

    public static SqlParameter Fecha(string nombre, DateOnly? valor)
        => Crear(nombre, SqlDbType.Date, valor?.ToDateTime(TimeOnly.MinValue));

    public static SqlParameter Guid(string nombre, Guid? valor)
        => Crear(nombre, SqlDbType.UniqueIdentifier, valor);

    public static SqlParameter Binario(string nombre, byte[]? valor, int tamano)
        => Crear(nombre, SqlDbType.Binary, valor, tamano);

    private static SqlParameter Crear(string nombre, SqlDbType tipo, object? valor, int tamano = 0)
    {
        if (string.IsNullOrWhiteSpace(nombre) || !nombre.StartsWith('@'))
            throw new ArgumentException("El nombre del parámetro debe empezar con '@'.", nameof(nombre));

        var p = new SqlParameter(nombre, tipo) { Value = valor ?? DBNull.Value };
        if (tamano != 0) p.Size = tamano;
        return p;
    }
}