using CONADIS.Core.Negocio.Datos;
using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Seguridad;

namespace CONADIS.Core.Negocio.Servicios;

/// <summary>Consulta de la bitácora inmutable y de los logs (solo Auditor y Administrador).</summary>
public sealed class ServicioAuditoria(EjecutorSp db)
{
    public Task<List<EventoAuditoria>> ListarEventosAsync(UsuarioSesion u, DateTimeOffset desde, DateTimeOffset hasta,
        string? usuario = null, string? entidad = null, CancellationToken ct = default)
    {
        Autorizacion.Exigir(u, Roles.Auditor, Roles.Administrador);
        return db.ConsultarListaAsync("audit.ppEvento_Listar", f => new EventoAuditoria(f.EnteroLargo("EventoId"),
                f.FechaHora("Fecha"), f.Texto("Usuario"), f.TextoONulo("Ip"), f.Texto("Accion"), f.Texto("Entidad"),
                f.TextoONulo("EntidadId"), f.TextoONulo("Detalle")),
            [
                Parametro.FechaHora("@Desde", desde), Parametro.FechaHora("@Hasta", hasta),
                Parametro.Texto("@Usuario", usuario, 100), Parametro.Texto("@Entidad", entidad, 50)
            ], ct);
    }

    /// <summary>Recalcula la cadena de hashes. Lista vacía = nadie alteró la bitácora.</summary>
    public Task<List<ProblemaCadena>> VerificarCadenaAsync(UsuarioSesion u, CancellationToken ct = default)
    {
        Autorizacion.Exigir(u, Roles.Auditor, Roles.Administrador);
        return db.ConsultarListaAsync("audit.ppEvento_VerificarCadena", f => new ProblemaCadena(f.EnteroLargo("EventoId"),
            f.FechaHora("Fecha"), f.Texto("Usuario"), f.Texto("Accion"), f.Texto("Problema")), ct: ct);
    }

    /// <summary>Registros de Log4Net guardados en la tabla Log4NetLog (estadísticas de errores).</summary>
    /// <remarks>Las fechas del log se guardan en hora local; SQL Server toma la hora local del DateTimeOffset.</remarks>
    public Task<List<RegistroLog>> ListarLogsAsync(UsuarioSesion u, DateTimeOffset desde, DateTimeOffset hasta, string? nivel = null, CancellationToken ct = default)
    {
        Autorizacion.Exigir(u, Roles.Auditor, Roles.Administrador);
        return db.ConsultarListaAsync("dbo.ppLog_Listar", f => new RegistroLog(f.Entero("Id"), f.FechaHora("Fecha"),
                f.Texto("Nivel"), f.Texto("Origen"), f.Texto("Mensaje"), f.TextoONulo("Excepcion")),
            [
                Parametro.FechaHora("@Desde", desde), Parametro.FechaHora("@Hasta", hasta),
                Parametro.Texto("@Nivel", nivel, 50)
            ], ct);
    }
}