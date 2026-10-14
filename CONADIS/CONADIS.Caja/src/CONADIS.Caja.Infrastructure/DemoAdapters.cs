using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using CONADIS.Caja.Application;
using CONADIS.Caja.Domain;
namespace CONADIS.Caja.Infrastructure;
// Solamente se registran en Development con Caja:Demo=true. No emiten documentos fiscales válidos.
public sealed class DemoFiscal:IFiscalGateway
{
    public Task<bool> CertificadoVigente(CancellationToken ct)=>Task.FromResult(true);
    public Task<bool> ValidarRnc(string doc,CancellationToken ct)=>Task.FromResult(doc=="101010101");
    public Task<DocumentoFiscal> Firmar(FacturaCaja f,CancellationToken ct)
    {
        var xml=new XElement("ComprobantePrueba",new XAttribute("SinValidezFiscal",true),new XElement("ENcf",f.ENcf),new XElement("Total",f.Total)).ToString();
        return Task.FromResult(new DocumentoFiscal(xml,CajaService.Hash(xml)[..12],"",true));
    }
    public Task<RespuestaFiscal> EnviarOConsultar(FacturaCaja f,CancellationToken ct)=>Task.FromResult(new RespuestaFiscal(EstadoDgii.Aceptado,"DEMO-"+f.Id,"Respuesta simulada; sin comunicación con DGII."));
}
public sealed class DemoPagos:IVerificadorPago
{
    public Task<bool> Verificar(string referencia,Guid orden,decimal total,MedioPago medio,CancellationToken ct)=>Task.FromResult(referencia==$"DEMO-{orden:N}");
}
public sealed class DemoFirma:IFirmaCierre
{
    public Task<string> Firmar(string usuario,Guid sesion,string contenido,CancellationToken ct)=>Task.FromResult("DEMO-SIN-VALIDEZ:"+CajaService.Hash(usuario+sesion+contenido));
}
public sealed class DemoCore:IEventosCore
{
    public Task Publicar(Guid mensajeId,Guid recursoId,string tipo,CancellationToken ct)=>Task.CompletedTask;
}
// Fallan de forma explícita hasta que integración registre los adaptadores institucionales.
public sealed class PendienteIntegracion:IFiscalGateway,IVerificadorPago,IFirmaCierre,IEventosCore
{
    static Exception Missing()=>new InvalidOperationException("Adaptador institucional pendiente de configurar.");
    public Task<bool> CertificadoVigente(CancellationToken ct)=>Task.FromResult(false);
    public Task<bool> ValidarRnc(string documento,CancellationToken ct)=>throw Missing();
    public Task<DocumentoFiscal> Firmar(FacturaCaja f,CancellationToken ct)=>throw Missing();
    public Task<RespuestaFiscal> EnviarOConsultar(FacturaCaja f,CancellationToken ct)=>throw Missing();
    public Task<bool> Verificar(string r,Guid o,decimal t,MedioPago m,CancellationToken ct)=>throw Missing();
    public Task<string> Firmar(string u,Guid s,string c,CancellationToken ct)=>throw Missing();
    public Task Publicar(Guid m,Guid r,string t,CancellationToken ct)=>throw Missing();
}
