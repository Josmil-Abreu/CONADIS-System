using System.Data;
using CONADIS.Web.Negocio.Datos;
using CONADIS.Web.Negocio.Modelos;
using CONADIS.Web.Negocio.Reportes;

namespace CONADIS.Web.Negocio.Servicios;

/// <summary>
/// Reportes del Portal Ciudadano (RDLC → PDF/Excel). Cada operación del ciudadano tiene su reporte:
///  - documentos del trámite (acuse con historial, comprobante del pago en línea, carnet digital),
///    armados con los datos que devuelve Integración;
///  - comprobante de cualquier operación registrada en su cuenta y el listado de su actividad.
/// </summary>
public sealed class ServicioReportesPortal(EjecutorSp db)
{
    private const string Modulo = "Portal Ciudadano";

    /// <summary>Deja constancia de una operación del ciudadano (así tiene su comprobante).</summary>
    public Task RegistrarOperacionAsync(CuentaSesion c, string operacion, string? detalle, string? ip, CancellationToken ct = default)
        => db.EjecutarAsync("web.ppEvento_Registrar",
        [
            Parametro.Entero("@CuentaId", c.CuentaId), Parametro.Texto("@Evento", operacion, 50),
            Parametro.TextoAnsi("@Ip", ip, 45), Parametro.Texto("@Detalle", detalle, 300)
        ], ct);

    public Task<ArchivoReporte> AcuseTramiteAsync(CuentaSesion c, TramiteCiudadano t, FormatoReporte formato = FormatoReporte.Pdf, string? ip = null,
        CancellationToken ct = default)
    {
        var historial = GeneradorReportes.Tabla(t.Historial.OrderBy(h => h.Fecha));
        return ArchivoAsync(c, "rptAcuseTramite", formato, $"Trámite {t.Numero}", ip, ct, ("Tramite", GeneradorReportes.Tabla([t])), ("Historial", historial));
    }

    public Task<ArchivoReporte> ComprobantePagoAsync(CuentaSesion c, OrdenCobro o, PagoEnLinea? pago, FormatoReporte formato = FormatoReporte.Pdf,
        string? ip = null, CancellationToken ct = default)
        => ArchivoAsync(c, "rptComprobantePago", formato, $"Orden {o.NumeroOrden}", ip, ct, ("Datos", TablaPago(o, pago)));

    public Task<ArchivoReporte> CarnetAsync(CuentaSesion c, CarnetCiudadano carnet, string urlVerificacion, FormatoReporte formato = FormatoReporte.Pdf,
        string? ip = null, CancellationToken ct = default)
    {
        var t = GeneradorReportes.Tabla([carnet]);
        t.Columns.Add("UrlVerificacion", typeof(string));
        t.Rows[0]["UrlVerificacion"] = urlVerificacion;
        return ArchivoAsync(c, "rptCarnetDigital", formato, $"Carnet {carnet.NumeroCarnet}", ip, ct, ("Datos", t));
    }

    public Task<DataTable> ActividadAsync(CuentaSesion c, DateOnly desde, DateOnly hasta, CancellationToken ct = default)
        => db.ConsultarAsync("web.ppRptTransacciones",
            [Parametro.Entero("@CuentaId", c.CuentaId), Parametro.Fecha("@Desde", desde), Parametro.Fecha("@Hasta", hasta)], ct);

    public async Task<ArchivoReporte> ActividadArchivoAsync(CuentaSesion c, DateOnly desde, DateOnly hasta, FormatoReporte formato = FormatoReporte.Pdf,
        string? ip = null, CancellationToken ct = default)
        => await ArchivoAsync(c, "rptMiActividad", formato, $"Del {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}", ip, ct,
            ("Datos", await ActividadAsync(c, desde, hasta, ct)));

    /// <summary>Comprobante de una operación de SU cuenta (el SP rechaza las ajenas).</summary>
    public async Task<ArchivoReporte> ComprobanteOperacionAsync(CuentaSesion c, long eventoId, FormatoReporte formato = FormatoReporte.Pdf,
        string? ip = null, CancellationToken ct = default)
        => await ArchivoAsync(c, "rptComprobanteOperacionPortal", formato, $"Operación N.º {eventoId}", ip, ct,
            ("Datos", await db.ConsultarAsync("web.ppRptComprobanteTransaccion",
                [Parametro.EnteroLargo("@EventoId", eventoId), Parametro.Entero("@CuentaId", c.CuentaId)], ct)));

    internal static DataTable TablaPago(OrdenCobro o, PagoEnLinea? pago)
    {
        var t = GeneradorReportes.Tabla([o]);
        t.Columns.Add("TransaccionId", typeof(string));
        t.Columns.Add("CodigoAutorizacion", typeof(string));
        t.Rows[0]["TransaccionId"] = pago?.TransaccionId.ToString() ?? "-";
        t.Rows[0]["CodigoAutorizacion"] = pago?.CodigoAutorizacion ?? "-";
        if (pago is not null && o.NumeroComprobante is null) t.Rows[0]["NumeroComprobante"] = pago.NumeroComprobante;
        return t;
    }

    private Task<ArchivoReporte> ArchivoAsync(CuentaSesion c, string reporte, FormatoReporte formato, string filtros, string? ip, CancellationToken ct,
        params (string, DataTable)[] datos)
    {
        var archivo = GeneradorReportes.Generar(reporte, new EncabezadoReporte(Modulo, $"{c.NombreCompleto} (cédula {c.Cedula})", filtros), formato, datos);
        return RegistrarYDevolver();

        async Task<ArchivoReporte> RegistrarYDevolver()
        {
            await RegistrarOperacionAsync(c, "ReporteDescargado", $"{reporte} · {formato} · {filtros}", ip, ct);
            return archivo;
        }
    }
}
