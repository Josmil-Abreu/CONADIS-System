using System.Data;
using CONADIS.Core.Negocio.Datos;
using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Reportes;
using CONADIS.Core.Negocio.Seguridad;

namespace CONADIS.Core.Negocio.Servicios;

/// <summary>
/// Datos de los reportes: cada método ejecuta su SP (como ppGetData en clase) y devuelve el DataTable
/// que se le entrega al reporte RDLC. El diseño visual de los .rdlc y la exportación a PDF se hacen
/// junto con las interfaces.
/// </summary>
public sealed class ServicioReportes(EjecutorSp db)
{
    // Quién puede ver los reportes institucionales
    private static readonly string[] RolesReportes = [Roles.Administrador, Roles.Auditor, Roles.AnalistaRegistro, Roles.SupervisorRegistro];

    public Task<DataTable> ExpedientesPorEstadoAsync(UsuarioSesion u, DateOnly desde, DateOnly hasta, string? estado = null, CancellationToken ct = default)
    {
        Autorizacion.Exigir(u, RolesReportes);
        ValidarRango(desde, hasta);
        return db.ConsultarAsync("core.ppRptExpedientesPorEstado",
            [Parametro.Fecha("@Desde", desde), Parametro.Fecha("@Hasta", hasta), Parametro.Texto("@Estado", estado, 20)], ct);
    }

    /// <summary>Estadística anonimizada (ROT-01): celdas con menos de 10 personas agrupadas en "Otros".</summary>
    public Task<DataTable> EstadisticaAnonimizadaAsync(UsuarioSesion u, DateOnly desde, DateOnly hasta, CancellationToken ct = default)
    {
        Autorizacion.Exigir(u, RolesReportes);
        ValidarRango(desde, hasta);
        return db.ConsultarAsync("core.ppRptEstadisticaAnonimizada", [Parametro.Fecha("@Desde", desde), Parametro.Fecha("@Hasta", hasta)], ct);
    }

    public Task<DataTable> IngresosAsync(UsuarioSesion u, DateOnly desde, DateOnly hasta, CancellationToken ct = default)
    {
        Autorizacion.Exigir(u, Roles.Administrador, Roles.Auditor);
        ValidarRango(desde, hasta);
        return db.ConsultarAsync("core.ppRptIngresos", [Parametro.Fecha("@Desde", desde), Parametro.Fecha("@Hasta", hasta)], ct);
    }

    public Task<DataTable> CarnetsEmitidosAsync(UsuarioSesion u, DateOnly desde, DateOnly hasta, CancellationToken ct = default)
    {
        Autorizacion.Exigir(u, RolesReportes);
        ValidarRango(desde, hasta);
        return db.ConsultarAsync("core.ppRptCarnetsEmitidos", [Parametro.Fecha("@Desde", desde), Parametro.Fecha("@Hasta", hasta)], ct);
    }

    /// <summary>Informe de valoración para el comité (salida de RF-CORE-02).</summary>
    public Task<DataTable> InformeValoracionAsync(UsuarioSesion u, int evaluacionId, CancellationToken ct = default)
    {
        Autorizacion.Exigir(u, Roles.Comite, Roles.Evaluador, Roles.Administrador, Roles.Auditor);
        return db.ConsultarAsync("core.ppRptInformeValoracion", [Parametro.Entero("@EvaluacionId", evaluacionId)], ct);
    }

    /// <summary>Indicadores de gestión del trámite (ROT-06).</summary>
    public Task<DataTable> IndicadoresTramiteAsync(UsuarioSesion u, DateOnly desde, DateOnly hasta, CancellationToken ct = default)
    {
        Autorizacion.Exigir(u, RolesReportes);
        ValidarRango(desde, hasta);
        return db.ConsultarAsync("core.ppRptIndicadoresTramite", [Parametro.Fecha("@Desde", desde), Parametro.Fecha("@Hasta", hasta)], ct);
    }

    // ------------------------------------------------------------------ cada transacción tiene su reporte

    /// <summary>
    /// Listado de transacciones (bitácora audit.Evento) por período. El auditor y el administrador ven las de todos;
    /// cualquier otro usuario ve solo las suyas ("Mis transacciones").
    /// </summary>
    public Task<DataTable> TransaccionesAsync(UsuarioSesion u, DateOnly desde, DateOnly hasta, string? accion = null, string? usuario = null,
        CancellationToken ct = default)
    {
        if (u.Rol is not (Roles.Auditor or Roles.Administrador)) usuario = u.Username;
        ValidarRango(desde, hasta);
        return db.ConsultarAsync("core.ppRptTransacciones",
        [
            Parametro.Fecha("@Desde", desde), Parametro.Fecha("@Hasta", hasta),
            Parametro.Texto("@Accion", Limpiar(accion), 100), Parametro.Texto("@Usuario", Limpiar(usuario), 100)
        ], ct);
    }

    /// <summary>
    /// Comprobante de UNA transacción. El auditor y el administrador ven cualquiera;
    /// los demás, solo las transacciones que hicieron ellos mismos.
    /// </summary>
    public async Task<DataTable> ComprobanteTransaccionAsync(UsuarioSesion u, long eventoId, CancellationToken ct = default)
    {
        var tabla = await db.ConsultarAsync("core.ppRptComprobanteTransaccion", [Parametro.EnteroLargo("@EventoId", eventoId)], ct);
        if (u.Rol is not (Roles.Auditor or Roles.Administrador)
            && !string.Equals(tabla.Rows[0].Texto("Usuario"), u.Username, StringComparison.OrdinalIgnoreCase))
            throw new AccesoDenegadoException("Solo puede imprimir el comprobante de las transacciones que usted realizó.");
        return tabla;
    }

    public Task<DataTable> AcuseSolicitudAsync(UsuarioSesion u, string numeroTramite, CancellationToken ct = default)
    {
        Autorizacion.Exigir(u, RolesReportes);
        return db.ConsultarAsync("core.ppRptAcuseSolicitud", [Parametro.TextoAnsi("@NumeroTramite", numeroTramite.Trim(), 30)], ct);
    }

    public Task<DataTable> CarnetEmitidoAsync(UsuarioSesion u, int carnetId, CancellationToken ct = default)
    {
        Autorizacion.Exigir(u, RolesReportes);
        return db.ConsultarAsync("core.ppRptCarnetEmitido", [Parametro.Entero("@CarnetId", carnetId)], ct);
    }

    public Task<DataTable> ReciboPagoAsync(UsuarioSesion u, string numeroOrden, CancellationToken ct = default)
    {
        Autorizacion.Exigir(u, RolesReportes);
        return db.ConsultarAsync("core.ppRptReciboPago", [Parametro.TextoAnsi("@NumeroOrden", numeroOrden.Trim(), 30)], ct);
    }

    // ------------------------------------------------------------------ archivo PDF / Excel

    /// <summary>
    /// Convierte los datos en el archivo del reporte RDLC y deja constancia en la bitácora
    /// (generar un reporte también es una transacción auditada).
    /// </summary>
    public async Task<ArchivoReporte> ArchivoAsync(UsuarioSesion u, string reporte, DataTable datos, FormatoReporte formato,
        string? filtros = null, string? ip = null, CancellationToken ct = default)
    {
        var archivo = GeneradorReportes.Generar(reporte, new EncabezadoReporte("Módulo Core", $"{u.NombreCompleto} ({u.Username})", filtros),
            formato, ("Datos", datos));
        await db.EjecutarAsync("audit.ppEvento_Registrar",
        [
            Parametro.Texto("@Usuario", u.Username, 100), Parametro.Texto("@Accion", "ReporteGenerado", 100),
            Parametro.Texto("@Entidad", "Reporte", 50), Parametro.Texto("@EntidadId", reporte, 50),
            Parametro.Texto("@Detalle", $"{formato} · {filtros}".Trim(' ', '·'), 2000), Parametro.TextoAnsi("@Ip", ip, 45)
        ], ct);
        return archivo;
    }

    private static string? Limpiar(string? t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();

    private static void ValidarRango(DateOnly desde, DateOnly hasta)
    {
        if (desde > hasta) throw new ReglaNegocioException(0, "La fecha inicial no puede ser mayor que la final.");
        if (hasta.DayNumber - desde.DayNumber > 366 * 5) throw new ReglaNegocioException(0, "El rango máximo es de 5 años.");
    }
}