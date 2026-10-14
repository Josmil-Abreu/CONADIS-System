using System.Data;
using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Reportes;
using log4net;

namespace CONADIS.Integracion.Negocio.Servicios;

/// <summary>
/// Reportes de Integración (RDLC → PDF/Excel). Cada transacción de interoperabilidad (integ.Bitacora) tiene su
/// comprobante; además: listado por período, pagos recibidos frente a aplicados y disponibilidad de servicios.
/// Los pide el sistema interno del Core por la API (ámbito "reportes").
/// </summary>
public sealed class ServicioReportesIntegracion(EjecutorSp db)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ServicioReportesIntegracion));

    /// <summary>Clave del reporte → archivo RDLC (los que van por período).</summary>
    public static readonly IReadOnlyDictionary<string, string> PorPeriodo = new Dictionary<string, string>
    {
        ["transacciones"] = "rptTransaccionesIntegracion",
        ["pagos"] = "rptPagosRecibidos",
        ["servicios"] = "rptDisponibilidadServicios"
    };

    public async Task<ArchivoReporte> PeriodoAsync(string reporte, DateOnly desde, DateOnly hasta, FormatoReporte formato, string solicitante,
        string? servicio = null, CancellationToken ct = default)
    {
        if (!PorPeriodo.TryGetValue(reporte, out var rdlc)) throw new ReglaNegocioException(56042, $"El reporte '{reporte}' no existe.");
        if (desde > hasta) throw new ReglaNegocioException(56040, "La fecha inicial no puede ser mayor que la final.");
        if (hasta.DayNumber - desde.DayNumber > 366) throw new ReglaNegocioException(56043, "El rango máximo es de un año.");

        IReadOnlyList<Microsoft.Data.SqlClient.SqlParameter> p = [Parametro.Fecha("@Desde", desde), Parametro.Fecha("@Hasta", hasta)];
        var datos = reporte switch
        {
            "transacciones" => await db.ConsultarAsync("integ.ppRptTransacciones",
                [.. p, Parametro.TextoAnsi("@Servicio", string.IsNullOrWhiteSpace(servicio) ? null : servicio, 20)], ct),
            "pagos" => await db.ConsultarAsync("integ.ppRptPagos", p, ct),
            _ => await db.ConsultarAsync("integ.ppRptServicios", p, ct)
        };
        return Generar(rdlc, datos, formato, solicitante, $"Del {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}{(servicio is null ? "" : $" · {servicio}")}");
    }

    public async Task<ArchivoReporte> ComprobanteAsync(long bitacoraId, FormatoReporte formato, string solicitante, CancellationToken ct = default)
        => Generar("rptComprobanteIntegracion",
            await db.ConsultarAsync("integ.ppRptComprobanteTransaccion", [Parametro.EnteroLargo("@BitacoraId", bitacoraId)], ct),
            formato, solicitante, $"Transacción N.º {bitacoraId}");

    private static ArchivoReporte Generar(string rdlc, DataTable datos, FormatoReporte formato, string solicitante, string filtros)
    {
        var a = GeneradorReportes.Generar(rdlc, new EncabezadoReporte("Módulo de Integración (SND)", solicitante, filtros), formato, ("Datos", datos));
        Log.Info($"Reporte {rdlc} ({formato}) generado para {solicitante}: {filtros}.");
        return a;
    }
}
