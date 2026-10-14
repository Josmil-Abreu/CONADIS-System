using CONADIS.Caja.Application;
using CONADIS.Caja.Domain;
namespace CONADIS.Caja.Infrastructure;
public sealed class OutboxProcessor(ICajaStore db,IFiscalGateway fiscal,IEventosCore core,CajaService service,TimeProvider clock)
{
    public async Task<int> Process(CancellationToken ct=default)
    {
        await using var tx=await db.Begin(ct);
        var now=clock.GetUtcNow();
        // Materialización por compatibilidad con el proveedor SQLite de demostración.
        var messages=db.Query<Outbox>().Where(x=>!x.Completado).ToList().Where(x=>x.ProximoIntento<=now).OrderBy(x=>x.ProximoIntento).Take(20).ToList();
        foreach(var m in messages)
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                if(m.Tipo=="EnviarECF")
                {
                    var f=db.Query<FacturaCaja>().Single(x=>x.Id==m.RecursoId);
                    var response=await fiscal.EnviarOConsultar(f,timeout.Token);
                    f.Responder(response.Estado,response.TrackId,response.Mensaje);
                    if(response.Estado is EstadoDgii.Aceptado or EstadoDgii.AceptadoCondicional or EstadoDgii.Rechazado)m.Completar();
                    else m.Reintentar(now,"Comprobante pendiente de respuesta definitiva.");
                }
                else {await core.Publicar(m.Id,m.RecursoId,m.Tipo,timeout.Token);m.Completar();}
            }
            catch(Exception) when(!ct.IsCancellationRequested)
            {
                if(m.Tipo=="EnviarECF") db.Query<FacturaCaja>().Single(x=>x.Id==m.RecursoId).Responder(EstadoDgii.Contingencia,null,"Envío pendiente; requiere seguimiento.");
                m.Reintentar(now,"Servicio externo no disponible.");
            }
            service.Audit("sistema","outbox:"+m.Id+":"+m.Intentos+":"+m.Completado);
            await db.Save(ct);
        }
        await tx.Commit(ct);return messages.Count;
    }
}
