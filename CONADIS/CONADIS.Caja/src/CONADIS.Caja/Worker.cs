using CONADIS.Caja.Infrastructure;
namespace CONADIS.Caja;
public sealed class Worker(IServiceScopeFactory scopes,ILogger<Worker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(30));
        while(await timer.WaitForNextTickAsync(stoppingToken))
        {
            try{using var scope=scopes.CreateScope();await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().Process(stoppingToken);}
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception){logger.LogWarning("No se pudo procesar la cola de caja. Se reintentará en el siguiente ciclo.");}
        }
    }
}
