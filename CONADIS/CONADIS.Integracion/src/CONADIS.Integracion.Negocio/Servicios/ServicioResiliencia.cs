using System.Data;
using System.Diagnostics;
using log4net;
using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Contratos;

namespace CONADIS.Integracion.Negocio.Servicios;

/// <summary>
/// Envuelve TODA llamada a un servicio de destino:
///   1. Pregunta al cortacircuito (snd.ppServicio_PuedeLlamar) si se puede llamar.
///   2. Llama y mide el tiempo.
///   3. Anota éxito o fallo en el cortacircuito y en la bitácora.
/// El estado del cortacircuito vive en la base, así que la API y el Servicio de Windows
/// comparten la misma vista de qué servicio está caído.
/// </summary>
public sealed class ServicioResiliencia(EjecutorSp sp, ServicioBitacora bitacora)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ServicioResiliencia));

    public async Task<T> EjecutarAsync<T>(string servicio, string operacion,
        Func<CancellationToken, Task<T>> llamada, CancellationToken ct = default)
    {
        if (!await PuedeLlamarAsync(servicio, ct))
        {
            await bitacora.RegistrarAsync(Direcciones.Saliente, operacion, false, 0, 503,
                servicio: servicio, detalle: "No se llamó: cortacircuito abierto.", ct: ct);
            throw new ServicioExternoNoDisponibleException(servicio,
                $"El servicio {servicio} no está disponible en este momento (cortacircuito abierto).");
        }

        var reloj = Stopwatch.StartNew();
        try
        {
            var resultado = await llamada(ct);
            reloj.Stop();
            await RegistrarExitoAsync(servicio, ct);
            await bitacora.RegistrarAsync(Direcciones.Saliente, operacion, true, Ms(reloj), 200, servicio: servicio, ct: ct);
            return resultado;
        }
        catch (RechazoServicioExternoException ex)
        {
            // El servicio respondió (está vivo): para el cortacircuito es un éxito.
            reloj.Stop();
            await RegistrarExitoAsync(servicio, ct);
            await bitacora.RegistrarAsync(Direcciones.Saliente, operacion, true, Ms(reloj), 422,
                servicio: servicio, detalle: ex.Message, ct: ct);
            throw;
        }
        catch (Exception ex) when (EsFalloDeDisponibilidad(ex, ct))
        {
            reloj.Stop();
            var estado = await RegistrarFalloAsync(servicio, ct);
            await bitacora.RegistrarAsync(Direcciones.Saliente, operacion, false, Ms(reloj), 503,
                servicio: servicio, detalle: ex.Message, ct: ct);

            Log.Warn($"{servicio} falló en {operacion} ({ex.GetType().Name}). Cortacircuito: {estado}.", ex);

            throw ex as ServicioExternoNoDisponibleException
                  ?? new ServicioExternoNoDisponibleException(servicio, $"El servicio {servicio} no está disponible en este momento.", ex);
        }
    }

    public async Task<bool> PuedeLlamarAsync(string servicio, CancellationToken ct = default)
    {
        var permitido = Parametro.Salida("@Permitido", SqlDbType.Bit);
        var estado = Parametro.Salida("@Estado", SqlDbType.NVarChar, 15);
        await sp.EjecutarAsync("snd.ppServicio_PuedeLlamar", ct,
            Parametro.Texto("@Codigo", servicio, 20), permitido, estado);
        return permitido.Booleano() == true;
    }

    private Task RegistrarExitoAsync(string servicio, CancellationToken ct)
        => sp.EjecutarAsync("snd.ppServicio_RegistrarExito", ct, Parametro.Texto("@Codigo", servicio, 20));

    private async Task<string?> RegistrarFalloAsync(string servicio, CancellationToken ct)
    {
        var estado = Parametro.Salida("@Estado", SqlDbType.NVarChar, 15);
        await sp.EjecutarAsync("snd.ppServicio_RegistrarFallo", ct, Parametro.Texto("@Codigo", servicio, 20), estado);
        return estado.Texto();
    }

    /// <summary>
    /// Fallos que significan "el servicio no está disponible". Una cancelación pedida por
    /// quien llama no lo es (el usuario se fue, no el servicio).
    /// </summary>
    private static bool EsFalloDeDisponibilidad(Exception ex, CancellationToken ct) => ex switch
    {
        ServicioExternoNoDisponibleException => true,
        HttpRequestException => true,
        TimeoutException => true,
        TaskCanceledException => !ct.IsCancellationRequested,   // cancelado sin pedirlo = tiempo agotado
        _ => false
    };

    private static int Ms(Stopwatch reloj) => (int)Math.Min(int.MaxValue, reloj.ElapsedMilliseconds);
}
