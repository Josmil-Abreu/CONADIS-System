using System.Data;
using System.Reflection;
using Microsoft.Reporting.NETCore;

namespace CONADIS.Web.Negocio.Reportes;

public enum FormatoReporte { Pdf, Excel }

/// <summary>Archivo de reporte listo para descargar o abrir.</summary>
public sealed record ArchivoReporte(byte[] Contenido, string TipoContenido, string NombreArchivo);

/// <summary>Datos de cabecera y pie que aparecen en todos los reportes (parámetros del RDLC).</summary>
public sealed record EncabezadoReporte(string Modulo, string GeneradoPor, string? Filtros = null);

/// <summary>
/// Genera los reportes RDLC (ReportViewerCore.NETCore). Los .rdlc están incrustados en esta biblioteca
/// (carpeta Reportes/) y se llenan con DataTables que devuelven los SP "ppRpt..." (como ppGetData en clase).
/// Todos los reportes declaran los mismos parámetros: Institucion, Modulo, Filtros, GeneradoPor y FechaGeneracion.
/// Cada módulo tiene su propia copia de esta clase (los módulos no se referencian entre sí).
/// </summary>
public static class GeneradorReportes
{
    public static ArchivoReporte Generar(string reporte, EncabezadoReporte encabezado, FormatoReporte formato,
        params (string DataSet, DataTable Datos)[] datos)
    {
        using var definicion = typeof(GeneradorReportes).Assembly.GetManifestResourceStream(Recurso(reporte))
            ?? throw new InvalidOperationException($"No existe el reporte {reporte}.rdlc (¿Acción de compilación = Recurso incrustado?).");

        using var local = new LocalReport();
        local.LoadReportDefinition(definicion);
        foreach (var (dataSet, tabla) in datos) local.DataSources.Add(new ReportDataSource(dataSet, Preparar(tabla)));
        local.SetParameters(
        [
            new ReportParameter("Modulo", encabezado.Modulo),
            new ReportParameter("Filtros", encabezado.Filtros ?? ""),
            new ReportParameter("GeneradoPor", encabezado.GeneradoPor),
            new ReportParameter("FechaGeneracion", DateTime.Now.ToString("dd/MM/yyyy HH:mm"))
        ]);

        var marca = DateTime.Now.ToString("yyyyMMdd-HHmm");
        return formato == FormatoReporte.Excel
            ? new ArchivoReporte(local.Render("EXCELOPENXML"), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{reporte}-{marca}.xlsx")
            : new ArchivoReporte(local.Render("PDF"), "application/pdf", $"{reporte}-{marca}.pdf");
    }

    /// <summary>Nombres de los reportes incrustados (sin extensión), útil para pruebas y menús.</summary>
    public static IEnumerable<string> Disponibles()
        => typeof(GeneradorReportes).Assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".rdlc", StringComparison.OrdinalIgnoreCase))
            .Select(n => Path.GetFileNameWithoutExtension(n).Split('.').Last());

    private static string Recurso(string reporte)
        => typeof(GeneradorReportes).Assembly.GetManifestResourceNames()
               .FirstOrDefault(n => n.EndsWith($".{reporte}.rdlc", StringComparison.OrdinalIgnoreCase))
           ?? $"{typeof(GeneradorReportes).Namespace}.{reporte}.rdlc";

    /// <summary>
    /// El ReportViewer no sabe mostrar DATETIMEOFFSET, DATE de .NET (DateOnly) ni VARBINARY:
    /// se convierten a DateTime local y a texto hexadecimal antes de entregarle la tabla.
    /// </summary>
    internal static DataTable Preparar(DataTable original)
    {
        var conversiones = original.Columns.Cast<DataColumn>()
            .Where(c => c.DataType == typeof(DateTimeOffset) || c.DataType == typeof(DateOnly) || c.DataType == typeof(byte[]))
            .ToList();
        if (conversiones.Count == 0) return original;

        var copia = original.Clone();
        foreach (var c in conversiones)
            copia.Columns[c.ColumnName]!.DataType = c.DataType == typeof(byte[]) ? typeof(string) : typeof(DateTime);

        foreach (DataRow fila in original.Rows)
        {
            var nueva = copia.NewRow();
            foreach (DataColumn c in original.Columns)
            {
                nueva[c.ColumnName] = fila[c] switch
                {
                    DateTimeOffset d => d.LocalDateTime,
                    DateOnly d => d.ToDateTime(TimeOnly.MinValue),
                    byte[] b => Convert.ToHexString(b),
                    var v => v
                };
            }
            copia.Rows.Add(nueva);
        }
        return copia;
    }

    /// <summary>Convierte una lista de objetos (records) en DataTable con una columna por propiedad.</summary>
    public static DataTable Tabla<T>(IEnumerable<T> filas)
    {
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0 && EsSimple(p.PropertyType)).ToArray();
        var tabla = new DataTable(typeof(T).Name);
        foreach (var p in props) tabla.Columns.Add(p.Name, Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType);
        foreach (var f in filas) tabla.Rows.Add(props.Select(p => p.GetValue(f) ?? DBNull.Value).ToArray());
        return tabla;
    }

    private static bool EsSimple(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime)
               || t == typeof(DateTimeOffset) || t == typeof(DateOnly) || t == typeof(Guid);
    }
}
