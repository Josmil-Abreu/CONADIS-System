using CONADIS.Core.Negocio.Datos;
using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Seguridad;

namespace CONADIS.Core.Negocio.Servicios;

/// <summary>
/// Cola de notificaciones al ciudadano. Los SP las encolan al cambiar un trámite;
/// el servicio de Windows de Integración las toma, las "envía" (correo/SMS simulados) y reporta el resultado.
/// </summary>
public sealed class ServicioNotificaciones(EjecutorSp db)
{
    public Task<List<NotificacionPendiente>> PendientesAsync(int maximo = 50, CancellationToken ct = default)
        => db.ConsultarListaAsync("core.ppNotificacion_Pendientes", f => new NotificacionPendiente(
                f.EnteroLargo("NotificacionId"), f.Texto("Canal"), f.Texto("Destinatario"), f.Texto("Asunto"),
                f.Texto("Mensaje"), f.Entero("Intentos")),
            [Parametro.Entero("@Maximo", Math.Clamp(maximo, 1, 500))], ct);

    /// <summary>Tras 5 intentos fallidos la notificación queda como Fallida.</summary>
    public Task MarcarResultadoAsync(long notificacionId, bool exito, CancellationToken ct = default)
        => db.EjecutarAsync("core.ppNotificacion_MarcarResultado",
            [Parametro.EnteroLargo("@NotificacionId", notificacionId), Parametro.Booleano("@Exito", exito)], ct);

    /// <summary>Avisos que el ciudadano ve dentro del portal.</summary>
    public Task<List<AvisoPortal>> ListarPortalAsync(string cedula, int maximo = 10, CancellationToken ct = default)
        => db.ConsultarListaAsync("core.ppNotificacion_ListarPortal",
            f => new AvisoPortal(f.Texto("Asunto"), f.Texto("Mensaje"), f.FechaHora("FechaCreacion")),
            [Parametro.TextoAnsi("@Cedula", ValidadorCedula.Normalizar(cedula), 11), Parametro.Entero("@Maximo", Math.Clamp(maximo, 1, 100))], ct);
}