using System.Data;
using System.Text.RegularExpressions;
using log4net;
using Microsoft.Data.SqlClient;

namespace CONADIS.Web.Negocio.Datos;

/// <summary>
/// Única puerta de acceso a SQL Server del Portal Web (copia de la del Core: cada módulo se entrega por separado).
/// Solo ejecuta Stored Procedures (CommandType.StoredProcedure) con parámetros tipados.
/// No existe ningún método que reciba texto SQL: en el código C# es imposible
/// escribir una consulta concatenada.
/// </summary>
public sealed partial class EjecutorSp
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(EjecutorSp));

    // Nombre permitido: esquema.nombre con letras, números y guion bajo (ej.: core.ppSolicitud_Registrar)
    [GeneratedRegex(@"^(\[?[A-Za-z_][A-Za-z0-9_]*\]?\.)?\[?[A-Za-z_][A-Za-z0-9_]*\]?$")]
    private static partial Regex NombreSpValido();

    private readonly string _cadenaConexion;
    private readonly int _tiempoEsperaSegundos;

    public EjecutorSp(string? cadenaConexion, int tiempoEsperaSegundos = 30)
    {
        if (string.IsNullOrWhiteSpace(cadenaConexion))
            throw new ArgumentException("Falta la cadena de conexión.", nameof(cadenaConexion));

        _cadenaConexion = cadenaConexion;
        _tiempoEsperaSegundos = tiempoEsperaSegundos;
    }

    /// <summary>Ejecuta un SP que devuelve filas y las entrega en un DataTable.</summary>
    public Task<DataTable> ConsultarAsync(string sp, IReadOnlyList<SqlParameter>? parametros = null, CancellationToken ct = default)
        => EjecutarConManejoDeErroresAsync(sp, parametros, async comando =>
        {
            await using var lector = await comando.ExecuteReaderAsync(ct);
            var tabla = new DataTable(sp);
            tabla.Load(lector);
            return tabla;
        }, ct);

    /// <summary>
    /// Ejecuta un SP que devuelve VARIOS conjuntos de resultados (por ejemplo, ppExpediente_Obtener).
    /// Cada conjunto queda en una tabla del DataSet, en el mismo orden.
    /// </summary>
    public Task<DataSet> ConsultarVariosAsync(string sp, IReadOnlyList<SqlParameter>? parametros = null, CancellationToken ct = default)
        => EjecutarConManejoDeErroresAsync(sp, parametros, async comando =>
        {
            await using var lector = await comando.ExecuteReaderAsync(ct);
            var conjunto = new DataSet(sp);
            // DataTable.Load avanza solo al siguiente conjunto; cuando no quedan más, cierra el lector.
            while (!lector.IsClosed)
            {
                var tabla = new DataTable($"Resultado{conjunto.Tables.Count + 1}");
                tabla.Load(lector);
                conjunto.Tables.Add(tabla);
            }
            return conjunto;
        }, ct);

    /// <summary>Ejecuta un SP y convierte cada fila con <paramref name="mapear"/>.</summary>
    public async Task<List<T>> ConsultarListaAsync<T>(string sp, Func<DataRow, T> mapear,
        IReadOnlyList<SqlParameter>? parametros = null, CancellationToken ct = default)
    {
        var tabla = await ConsultarAsync(sp, parametros, ct);
        var lista = new List<T>(tabla.Rows.Count);
        foreach (DataRow fila in tabla.Rows) lista.Add(mapear(fila));
        return lista;
    }

    /// <summary>Ejecuta un SP y devuelve la primera fila convertida, o null si no hubo filas.</summary>
    public async Task<T?> ConsultarUnoAsync<T>(string sp, Func<DataRow, T> mapear,
        IReadOnlyList<SqlParameter>? parametros = null, CancellationToken ct = default) where T : class
    {
        var tabla = await ConsultarAsync(sp, parametros, ct);
        return tabla.Rows.Count == 0 ? null : mapear(tabla.Rows[0]);
    }

    /// <summary>Ejecuta un SP de escritura que no devuelve filas.</summary>
    public Task EjecutarAsync(string sp, IReadOnlyList<SqlParameter>? parametros = null, CancellationToken ct = default)
        => EjecutarConManejoDeErroresAsync(sp, parametros, comando => comando.ExecuteNonQueryAsync(ct), ct);

    /// <summary>Comprueba si la base de datos responde. Nunca lanza excepciones.</summary>
    public async Task<bool> ProbarConexionAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conexion = new SqlConnection(_cadenaConexion);
            await conexion.OpenAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("La base de datos no respondió a la prueba de conexión.", ex);
            return false;
        }
    }

    // -------------------------------------------------------------------------

    private async Task<T> EjecutarConManejoDeErroresAsync<T>(string sp, IReadOnlyList<SqlParameter>? parametros,
        Func<SqlCommand, Task<T>> accion, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sp) || !NombreSpValido().IsMatch(sp))
            throw new ArgumentException($"Nombre de Stored Procedure no válido: '{sp}'.", nameof(sp));

        try
        {
            await using var conexion = new SqlConnection(_cadenaConexion);
            await conexion.OpenAsync(ct);

            await using var comando = new SqlCommand(sp, conexion)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = _tiempoEsperaSegundos
            };
            if (parametros is { Count: > 0 })
                foreach (var p in parametros) comando.Parameters.Add(p);

            var resultado = await accion(comando);
            if (Log.IsDebugEnabled) Log.Debug($"SP {sp} ejecutado correctamente.");
            return resultado;
        }
        catch (SqlException ex) when (ex.Number is >= 50001 and <= 59999)
        {
            // Regla de negocio lanzada a propósito por el SP con THROW: no es un fallo del sistema.
            Log.Warn($"SP {sp} rechazó la operación ({ex.Number}): {ex.Message}");
            throw new ReglaNegocioException(ex.Number, ex.Message, ex);
        }
        catch (SqlException ex) when (EsErrorDeConexion(ex))
        {
            Log.Error($"Base de datos no disponible al ejecutar {sp} (error {ex.Number}).", ex);
            throw new BaseDatosNoDisponibleException("La base de datos no está disponible en este momento.", ex);
        }
        catch (SqlException ex)
        {
            Log.Error($"Error SQL {ex.Number} al ejecutar {sp}.", ex);
            throw new ErrorBaseDatosException("Ocurrió un error al guardar o leer los datos.", ex);
        }
        catch (InvalidOperationException ex)
        {
            // Por ejemplo: el pool de conexiones se agotó esperando al servidor.
            Log.Error($"Operación no válida al ejecutar {sp}.", ex);
            throw new BaseDatosNoDisponibleException("La base de datos no está disponible en este momento.", ex);
        }
        catch (OperationCanceledException)
        {
            Log.Info($"Ejecución de {sp} cancelada.");
            throw;
        }
    }

    private static bool EsErrorDeConexion(SqlException ex) => ex.Number switch
    {
        -2 => true,      // tiempo de espera agotado
        -1 => true,      // error al conectar
        2 or 53 => true, // servidor no encontrado / no accesible
        233 => true,     // conexión cerrada por el servidor
        4060 => true,    // la base de datos no existe o no se puede abrir
        10053 or 10054 or 10060 or 10061 or 11001 => true, // errores de red
        _ => ex.Class >= 20 // severidad 20+ = la conexión quedó inutilizable
    };
}