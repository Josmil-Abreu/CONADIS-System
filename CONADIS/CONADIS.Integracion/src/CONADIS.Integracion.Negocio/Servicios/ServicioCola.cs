using System.Data;
using CONADIS.Integracion.Negocio.Contratos;
using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Logging;

namespace CONADIS.Integracion.Negocio.Servicios;

/// <summary>
/// Cola de mensajes (outbox) sobre snd.Mensaje. Todo lo que debe llegar a otro servicio
/// y puede esperar (pagos para el Core, e-CF para la DGII, eventos de la Caja) pasa por aquí.
/// </summary>
public sealed class ServicioCola(EjecutorSp sp)
{
    public async Task<MensajeEncoladoDto> EncolarAsync(
        int? clienteId, string servicio, string tipo, string payloadJson,
        string? claveIdempotencia = null, CancellationToken ct = default)
    {
        Validaciones.Requerido(tipo, "tipo de mensaje", 60);
        Validaciones.Json(payloadJson, "contenido del mensaje");

        var mensajeId = Parametro.Salida("@MensajeId", SqlDbType.BigInt);
        var mensajeGuid = Parametro.Salida("@MensajeGuid", SqlDbType.UniqueIdentifier);
        var duplicado = Parametro.Salida("@EraDuplicado", SqlDbType.Bit);

        await sp.EjecutarAsync("snd.ppMensaje_Encolar", ct,
            Parametro.Entero("@ClienteId", clienteId),
            Parametro.Texto("@ServicioCodigo", servicio, 20),
            Parametro.Texto("@Tipo", tipo, 60),
            Parametro.Texto("@Payload", payloadJson),
            Parametro.Texto("@ClaveIdempotencia", claveIdempotencia, 100),
            Parametro.Guid("@CorrelacionId", Correlacion.Id),
            mensajeId, mensajeGuid, duplicado);

        return new MensajeEncoladoDto(mensajeId.EnteroLargo() ?? 0, mensajeGuid.Guid() ?? Guid.Empty, duplicado.Booleano() == true);
    }

    public Task<List<MensajeColaDto>> TomarLoteAsync(string trabajador, int tamano, int segundosBloqueo, CancellationToken ct = default)
        => sp.ConsultarListaAsync("snd.ppMensaje_TomarLote", f => new MensajeColaDto(
                f.EnteroLargo("MensajeId"), f.Guid("MensajeGuid"), f.Texto("ServicioCodigo"), f.Texto("Tipo"),
                f.Texto("Payload"), f.Entero("Intentos"), f.Entero("MaxIntentos"), f.TextoONulo("ClaveIdempotencia"),
                f.GuidONulo("CorrelacionId"), f.Fecha("FechaCreacion")),
            ct,
            Parametro.Texto("@Trabajador", trabajador, 100),
            Parametro.Entero("@Tamano", tamano),
            Parametro.Entero("@SegundosBloqueo", segundosBloqueo));

    public Task MarcarEntregadoAsync(long mensajeId, string? respuestaJson, CancellationToken ct = default)
        => sp.EjecutarAsync("snd.ppMensaje_MarcarEntregado", ct,
            Parametro.EnteroLargo("@MensajeId", mensajeId),
            Parametro.Texto("@RespuestaJson", respuestaJson));

    /// <summary>Devuelve el estado en que quedó: Pendiente (se reintentará) o Muerto.</summary>
    public async Task<string> MarcarFallidoAsync(long mensajeId, string error, CancellationToken ct = default)
    {
        var estado = Parametro.Salida("@EstadoFinal", SqlDbType.NVarChar, 15);
        await sp.EjecutarAsync("snd.ppMensaje_MarcarFallido", ct,
            Parametro.EnteroLargo("@MensajeId", mensajeId),
            Parametro.Texto("@Error", error),
            estado);
        return estado.Texto() ?? EstadosMensaje.Pendiente;
    }

    public Task ReintentarAsync(long mensajeId, CancellationToken ct = default)
        => sp.EjecutarAsync("snd.ppMensaje_Reintentar", ct, Parametro.EnteroLargo("@MensajeId", mensajeId));

    public Task<MensajeDetalleDto?> ObtenerAsync(Guid mensajeGuid, CancellationToken ct = default)
        => sp.ConsultarUnoAsync("snd.ppMensaje_Obtener", f => new MensajeDetalleDto(
                f.EnteroLargo("MensajeId"), f.Guid("MensajeGuid"), f.Texto("ServicioCodigo"), f.Texto("Tipo"),
                f.Texto("Estado"), f.Entero("Intentos"), f.Entero("MaxIntentos"), f.Fecha("ProximoIntento"),
                f.TextoONulo("UltimoError"), f.TextoONulo("RespuestaJson"), f.GuidONulo("CorrelacionId"),
                f.Fecha("FechaCreacion"), f.FechaONula("FechaEntrega")),
            ct, Parametro.Guid("@MensajeGuid", mensajeGuid));

    public Task<List<MensajeResumenDto>> ListarAsync(string? estado = null, DateTime? desde = null, DateTime? hasta = null,
        int top = 200, CancellationToken ct = default)
        => sp.ConsultarListaAsync("snd.ppMensaje_Listar", f => new MensajeResumenDto(
                f.EnteroLargo("MensajeId"), f.Guid("MensajeGuid"), f.Texto("ServicioCodigo"), f.TextoONulo("ClienteCodigo"),
                f.Texto("Tipo"), f.Texto("Estado"), f.Entero("Intentos"), f.Entero("MaxIntentos"),
                f.Fecha("ProximoIntento"), f.TextoONulo("UltimoError"), f.Fecha("FechaCreacion"), f.FechaONula("FechaEntrega")),
            ct,
            Parametro.Texto("@Estado", estado, 15),
            Parametro.Fecha("@Desde", desde),
            Parametro.Fecha("@Hasta", hasta),
            Parametro.Entero("@Top", top));
}
