using System.Data;
using System.Text.Json;
using log4net;
using CONADIS.Integracion.Negocio.Conectores;
using CONADIS.Integracion.Negocio.Contratos;
using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Logging;

namespace CONADIS.Integracion.Negocio.Servicios;

/// <summary>
/// Una "vuelta" del Servicio de Windows sobre la cola:
///   1. Toma un lote de mensajes pendientes (los de servicios con el cortacircuito abierto no se toman).
///   2. Entrega cada uno a su destino, pasando por el cortacircuito y la bitácora.
///   3. Entregado -> se marca Entregado (y el pago, Aplicado).
///      Rechazado por el destino -> se marca Entregado con el rechazo (reintentar no sirve).
///      Destino caído o error -> se marca fallido: vuelve a la cola con espera, o queda Muerto.
/// Un mensaje que falla nunca detiene a los demás del lote.
/// </summary>
public sealed class ProcesadorCola(
    EjecutorSp sp,
    ServicioCola cola,
    ServicioResiliencia resiliencia,
    ServicioPagos pagos,
    ServicioAdministracion administracion,
    IConectorCore core,
    IConectorDgii dgii)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ProcesadorCola));

    public async Task<ResumenLoteDto> ProcesarLoteAsync(string trabajador, CancellationToken ct = default)
    {
        var tamano = await administracion.ObtenerParametroEnteroAsync("Cola.TamanoLote", 20, ct);
        var bloqueo = await administracion.ObtenerParametroEnteroAsync("Cola.SegundosBloqueo", 120, ct);

        var lote = await cola.TomarLoteAsync(trabajador, tamano, bloqueo, ct);
        if (lote.Count == 0) return ResumenLoteDto.Vacio;

        int entregados = 0, reintentar = 0, muertos = 0;
        foreach (var mensaje in lote)
        {
            ct.ThrowIfCancellationRequested();
            using var _ = Correlacion.Iniciar(mensaje.CorrelacionId ?? Guid.NewGuid());

            switch (await ProcesarAsync(mensaje, ct))
            {
                case EstadosMensaje.Entregado: entregados++; break;
                case EstadosMensaje.Muerto: muertos++; break;
                default: reintentar++; break;
            }
        }

        var resumen = new ResumenLoteDto(lote.Count, entregados, reintentar, muertos);
        Log.Info($"Lote procesado por {trabajador}: {resumen.Tomados} tomados, {resumen.Entregados} entregados, " +
                 $"{resumen.Reintentar} para reintentar, {resumen.Muertos} muertos.");
        return resumen;
    }

    /// <summary>Limpieza periódica: mensajes entregados viejos, claves de idempotencia y caché vencidas.</summary>
    public async Task<ResumenMantenimientoDto> MantenimientoAsync(CancellationToken ct = default)
    {
        var resumen = new ResumenMantenimientoDto(
            await PurgarAsync("snd.ppMensaje_Purgar", ct),
            await PurgarAsync("snd.ppIdempotencia_Purgar", ct),
            await PurgarAsync("snd.ppCache_Purgar", ct));

        Log.Info($"Mantenimiento: {resumen.MensajesBorrados} mensajes, {resumen.ClavesIdempotenciaBorradas} claves de " +
                 $"idempotencia y {resumen.EntradasCacheBorradas} entradas de caché borradas.");
        return resumen;
    }

    // -------------------------------------------------------------------------

    private async Task<string> ProcesarAsync(MensajeColaDto mensaje, CancellationToken ct)
    {
        try
        {
            var respuesta = await resiliencia.EjecutarAsync(mensaje.ServicioCodigo, mensaje.Tipo,
                c => EntregarAsync(mensaje, c), ct);

            await cola.MarcarEntregadoAsync(mensaje.MensajeId, respuesta, ct);
            if (mensaje.Tipo == TiposMensaje.PagoRegistrar)
                await pagos.MarcarAplicadoAsync(LeerTransaccion(mensaje), ct);

            return EstadosMensaje.Entregado;
        }
        catch (RechazoServicioExternoException ex)
        {
            Log.Warn($"Mensaje {mensaje.MensajeGuid} ({mensaje.Tipo}) rechazado por {ex.Servicio}: {ex.Message}");
            await cola.MarcarEntregadoAsync(mensaje.MensajeId,
                JsonSerializer.Serialize(new { rechazado = true, servicio = ex.Servicio, motivo = ex.Message }), ct);
            if (mensaje.Tipo == TiposMensaje.PagoRegistrar)
                await pagos.MarcarRechazadoAsync(LeerTransaccion(mensaje), ex.Message, ct);

            return EstadosMensaje.Entregado;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            try
            {
                var estado = await cola.MarcarFallidoAsync(mensaje.MensajeId, $"{ex.GetType().Name}: {ex.Message}", ct);
                if (estado == EstadosMensaje.Muerto)
                    Log.Error($"Mensaje {mensaje.MensajeGuid} ({mensaje.Tipo} -> {mensaje.ServicioCodigo}) agotó sus intentos: queda Muerto.", ex);
                else
                    Log.Warn($"Mensaje {mensaje.MensajeGuid} ({mensaje.Tipo}) falló; se reintentará. {ex.Message}");
                return estado;
            }
            catch (Exception exMarcar) when (exMarcar is not OperationCanceledException)
            {
                // Si ni siquiera se puede marcar (base caída), el bloqueo vence solo
                // y el mensaje vuelve a estar disponible: no se pierde.
                Log.Error($"No se pudo marcar como fallido el mensaje {mensaje.MensajeGuid}.", exMarcar);
                return EstadosMensaje.Pendiente;
            }
        }
    }

    private Task<string> EntregarAsync(MensajeColaDto mensaje, CancellationToken ct) => mensaje.ServicioCodigo switch
    {
        CodigosServicio.Core => core.EntregarAsync(mensaje, ct),
        CodigosServicio.Dgii when mensaje.Tipo == TiposMensaje.EcfEnviar => EnviarEcfAsync(mensaje, ct),
        _ => throw new InvalidOperationException(
            $"No hay forma de entregar mensajes {mensaje.Tipo} al servicio {mensaje.ServicioCodigo}.")
    };

    private async Task<string> EnviarEcfAsync(MensajeColaDto mensaje, CancellationToken ct)
    {
        var factura = JsonSerializer.Deserialize<FacturaFiscalDto>(mensaje.Payload, ServicioCache.Json)
                      ?? throw new InvalidOperationException("El mensaje no contiene un comprobante fiscal.");

        var respuesta = await dgii.EnviarEcfAsync(factura, ct);
        if (respuesta.Estado == EstadosDgii.Rechazado)
            throw new RechazoServicioExternoException(CodigosServicio.Dgii, respuesta.Mensaje ?? "La DGII rechazó el comprobante.");

        return JsonSerializer.Serialize(respuesta, ServicioCache.Json);
    }

    private static Guid LeerTransaccion(MensajeColaDto mensaje)
    {
        using var doc = JsonDocument.Parse(mensaje.Payload);
        return doc.RootElement.GetProperty("TransaccionId").GetGuid();
    }

    private async Task<int> PurgarAsync(string procedimiento, CancellationToken ct)
    {
        var borrados = Parametro.Salida("@Borrados", SqlDbType.Int);
        await sp.EjecutarAsync(procedimiento, ct, borrados);
        return borrados.Entero() ?? 0;
    }
}
