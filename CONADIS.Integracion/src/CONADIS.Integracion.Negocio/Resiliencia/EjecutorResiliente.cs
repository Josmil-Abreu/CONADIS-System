using System.Diagnostics;
using CONADIS.Integracion.Negocio.Modelos;
using CONADIS.Integracion.Negocio.Repositorios;
using log4net;

namespace CONADIS.Integracion.Negocio.Resiliencia;

/// <summary>Cómo terminó una llamada a un servicio externo.</summary>
public sealed record ResultadoLlamada<T>(bool Exito, T? Valor, string Resultado, string? Error, int LatenciaMs)
{
    public static ResultadoLlamada<T> Correcto(T valor, string resultado, int latencia) => new(true, valor, resultado, null, latencia);
    public static ResultadoLlamada<T> Fallo(string resultado, string error, int latencia) => new(false, default, resultado, error, latencia);
}

public static class ResultadosLlamada
{
    public const string Ok = "Ok";
    public const string TiempoAgotado = "TiempoAgotado";
    public const string CircuitoAbierto = "CircuitoAbierto";
    public const string Error = "Error";
}

/// <summary>Opciones de una llamada: operación, huella del dato consultado y si se puede reintentar.</summary>
public sealed record OpcionesLlamada(string Operacion, string? ReferenciaHash = null, bool Reintentar = true);

/// <summary>
/// Envuelve TODA llamada a un servicio externo con las reglas de RIC-06 y RF-INT-01:
///   1. Cortacircuito: si está abierto, no se llama (respuesta inmediata "CircuitoAbierto").
///   2. Tiempo de espera configurable por servicio (5 s JCE/MSP, 15 s DGII...).
///   3. Hasta 3 reintentos con retroceso exponencial (200 ms, 400 ms, 800 ms).
///   4. Resultado al cortacircuito (éxito lo cierra; fallos seguidos lo abren).
///   5. Una fila en la bitácora con resultado y latencia (sin datos sensibles).
/// </summary>
public sealed class EjecutorResiliente(IRepositorioCircuito circuitos, IRepositorioBitacora bitacora, IParametros parametros)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(EjecutorResiliente));

    /// <param name="servicio">Valor de <see cref="ServiciosExternos"/> (JCE, MSP, Core...).</param>
    /// <param name="llamada">La llamada real. Recibe un token que se cancela al agotar el tiempo de espera.</param>
    /// <param name="clasificar">Texto del resultado para la bitácora (ej.: "Valida"/"Invalida"). Por defecto "Ok".</param>
    /// <param name="esErrorPermanente">
    /// Errores que NO deben reintentarse ni contar como caída del servicio (ej.: un 4xx del Core).
    /// Se propagan tal cual al llamador.
    /// </param>
    public async Task<ResultadoLlamada<T>> EjecutarAsync<T>(string servicio, OpcionesLlamada opciones, ContextoLlamada contexto,
        Func<CancellationToken, Task<T>> llamada, Func<T, string>? clasificar = null,
        Func<Exception, bool>? esErrorPermanente = null, CancellationToken ct = default)
    {
        var circuito = await PuedeLlamarAsync(servicio, ct);
        if (!circuito.Permitido)
        {
            await RegistrarBitacoraAsync(new EntradaBitacora(servicio, opciones.Operacion, "Saliente", false, ResultadosLlamada.CircuitoAbierto,
                contexto, opciones.ReferenciaHash, Detalle: $"Circuito abierto hasta {circuito.AbiertoHasta:HH:mm:ss}."), ct);
            return ResultadoLlamada<T>.Fallo(ResultadosLlamada.CircuitoAbierto, $"El servicio {servicio} no está disponible (circuito abierto).", 0);
        }

        var segundos = await parametros.EnteroAsync(ClaveTiempoEspera(servicio), servicio == ServiciosExternos.Dgii ? 15 : 5, ct);
        var maxIntentos = opciones.Reintentar ? Math.Clamp(await parametros.EnteroAsync("ReintentosInmediatosMaximos", 3, ct), 1, 5) : 1;

        var reloj = Stopwatch.StartNew();
        Exception? ultimoError = null;
        var resultadoFallo = ResultadosLlamada.Error;

        for (var intento = 1; intento <= maxIntentos; intento++)
        {
            using var tiempo = CancellationTokenSource.CreateLinkedTokenSource(ct);
            tiempo.CancelAfter(TimeSpan.FromSeconds(segundos));
            try
            {
                var valor = await llamada(tiempo.Token);
                var latencia = (int)reloj.ElapsedMilliseconds;
                var texto = clasificar?.Invoke(valor) ?? ResultadosLlamada.Ok;

                await RegistrarCircuitoAsync(servicio, true, null, ct);
                await RegistrarBitacoraAsync(new EntradaBitacora(servicio, opciones.Operacion, "Saliente", true, texto, contexto,
                    opciones.ReferenciaHash, "200", latencia, Detalle: intento > 1 ? $"Éxito en el intento {intento}." : null), ct);
                return ResultadoLlamada<T>.Correcto(valor, texto, latencia);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // la petición original se canceló: no es culpa del servicio externo
            }
            catch (Exception ex) when (esErrorPermanente?.Invoke(ex) == true)
            {
                // El servicio respondió, pero rechazó los datos: no es una caída. Se registra y se propaga.
                await RegistrarCircuitoAsync(servicio, true, null, ct);
                await RegistrarBitacoraAsync(new EntradaBitacora(servicio, opciones.Operacion, "Saliente", false, "Rechazado", contexto,
                    opciones.ReferenciaHash, LatenciaMs: (int)reloj.ElapsedMilliseconds, Detalle: ex.Message), ct);
                throw;
            }
            catch (Exception ex)
            {
                ultimoError = ex;
                resultadoFallo = ex is OperationCanceledException or TimeoutException ? ResultadosLlamada.TiempoAgotado : ResultadosLlamada.Error;
                Log.Warn($"{servicio}.{opciones.Operacion}: intento {intento}/{maxIntentos} falló ({resultadoFallo}): {ex.Message}");

                if (intento < maxIntentos)
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * Math.Pow(2, intento - 1)), ct);
            }
        }

        var latenciaTotal = (int)reloj.ElapsedMilliseconds;
        var mensaje = ultimoError is OperationCanceledException ? $"Sin respuesta en {segundos} s." : ultimoError?.Message ?? "Error desconocido.";
        await RegistrarCircuitoAsync(servicio, false, mensaje, ct);
        await RegistrarBitacoraAsync(new EntradaBitacora(servicio, opciones.Operacion, "Saliente", false, resultadoFallo, contexto,
            opciones.ReferenciaHash, LatenciaMs: latenciaTotal, Detalle: $"{maxIntentos} intento(s): {mensaje}"), ct);
        return ResultadoLlamada<T>.Fallo(resultadoFallo, mensaje, latenciaTotal);
    }

    /// <summary>Registra en la bitácora una respuesta que salió de la caché (sin llamada externa).</summary>
    public Task RegistrarDesdeCacheAsync(string servicio, string operacion, string resultado, ContextoLlamada contexto,
        string? referenciaHash, CancellationToken ct = default)
        => RegistrarBitacoraAsync(new EntradaBitacora(servicio, operacion, "Saliente", true, resultado, contexto, referenciaHash,
            DesdeCache: true), ct);

    public static string ClaveTiempoEspera(string servicio) => servicio switch
    {
        ServiciosExternos.Jce => "JceTiempoEsperaSegundos",
        ServiciosExternos.Msp => "MspTiempoEsperaSegundos",
        ServiciosExternos.Dgii => "DgiiTiempoEsperaSegundos",
        ServiciosExternos.Firma => "FirmaTiempoEsperaSegundos",
        ServiciosExternos.Pasarela => "PasarelaTiempoEsperaSegundos",
        ServiciosExternos.Core => "CoreTiempoEsperaSegundos",
        _ => "CoreTiempoEsperaSegundos"
    };

    // La bitácora y el circuito viven en la base de Integración: si ella falla, la llamada externa
    // NO debe fallar por eso (se registra en el log de archivo y se sigue).
    private async Task<EstadoCircuito> PuedeLlamarAsync(string servicio, CancellationToken ct)
    {
        try { return await circuitos.PuedeLlamarAsync(servicio, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error($"No se pudo consultar el circuito de {servicio}; se permite la llamada.", ex);
            return new EstadoCircuito(true, "Desconocido", null);
        }
    }

    private async Task RegistrarCircuitoAsync(string servicio, bool exito, string? error, CancellationToken ct)
    {
        try { await circuitos.RegistrarResultadoAsync(servicio, exito, error, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error($"No se pudo registrar el resultado del circuito de {servicio}.", ex);
        }
    }

    private async Task RegistrarBitacoraAsync(EntradaBitacora entrada, CancellationToken ct)
    {
        try { await bitacora.RegistrarAsync(entrada, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error($"No se pudo escribir la bitácora ({entrada.Servicio}.{entrada.Operacion}: {entrada.Resultado}).", ex);
        }
    }
}
