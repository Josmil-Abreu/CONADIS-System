using System.Data;
using CONADIS.Core.Negocio.Datos;
using CONADIS.Core.Negocio.Modelos;
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

    private static void ValidarRango(DateOnly desde, DateOnly hasta)
    {
        if (desde > hasta) throw new ReglaNegocioException(0, "La fecha inicial no puede ser mayor que la final.");
        if (hasta.DayNumber - desde.DayNumber > 366 * 5) throw new ReglaNegocioException(0, "El rango máximo es de 5 años.");
    }
}