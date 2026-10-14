using CONADIS.Web.Negocio.Datos;
using CONADIS.Web.Negocio.Servicios;

namespace CONADIS.Web.Infraestructura;

/// <summary>
/// Proceso en segundo plano del portal: cada 30 s reenvía lo que quedó en la bandeja de salida
/// (operaciones hechas mientras Integración o el Core estaban caídos). Si la base del portal tampoco
/// responde, espera a la próxima vuelta: nada se pierde porque todo está guardado en web.Envio.
/// </summary>
public sealed class TrabajadorBandejaSalida(IServiceScopeFactory alcances, IConfiguration config, ILogger<TrabajadorBandejaSalida> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = TimeSpan.FromSeconds(Math.Max(5, config.GetValue("BandejaSalida:Segundos", 30)));
        using var temporizador = new PeriodicTimer(intervalo);
        do
        {
            try
            {
                await using var alcance = alcances.CreateAsyncScope();
                var entregados = await alcance.ServiceProvider.GetRequiredService<BandejaSalida>().ProcesarAsync(stoppingToken);
                if (entregados > 0) log.LogInformation("Bandeja de salida: {Cantidad} envío(s) entregados a Integración.", entregados);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (BaseDatosNoDisponibleException ex)
            {
                log.LogWarning("Bandeja de salida: la base del portal no responde ({Mensaje}).", ex.Message);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Bandeja de salida: error inesperado; se reintenta en la próxima vuelta.");
            }
        } while (await temporizador.WaitForNextTickAsync(stoppingToken));
    }
}
