using CONADIS.Caja.Domain;
using FluentValidation;
namespace CONADIS.Caja.Application;
public record Actor(string Id,string Rol,string Sucursal);
public interface ICajaStore
{
    IQueryable<T> Query<T>() where T:Entity;
    void Add<T>(T entity) where T:Entity;
    Task<ITransaction> Begin(CancellationToken ct);
    Task Save(CancellationToken ct);
    void Clear();
}
public interface ITransaction:IAsyncDisposable { Task Commit(CancellationToken ct); }
public record DocumentoFiscal(string Xml,string CodigoSeguridad,string QrUrl,bool EsPrueba,byte[]? QrPng=null);
public record RespuestaFiscal(EstadoDgii Estado,string? TrackId,string? Mensaje);
public interface IFiscalGateway
{
    Task<bool> CertificadoVigente(CancellationToken ct);
    Task<bool> ValidarRnc(string documento,CancellationToken ct);
    Task<DocumentoFiscal> Firmar(FacturaCaja factura,CancellationToken ct);
    Task<RespuestaFiscal> EnviarOConsultar(FacturaCaja factura,CancellationToken ct);
}
// Consulta una autorización existente. Nunca cobra ni confía en un estado enviado por el navegador.
public interface IVerificadorPago { Task<bool> Verificar(string referencia,Guid orden,decimal total,MedioPago medio,CancellationToken ct); }
public interface IEventosCore { Task Publicar(Guid mensajeId,Guid recursoId,string tipo,CancellationToken ct); }
public interface IFirmaCierre { Task<string> Firmar(string usuario,Guid sesion,string contenido,CancellationToken ct); }
public sealed record CajaCommand
{
    public string Accion {get;init;}="";
    public Guid Id {get;init;}
    public Guid SesionId {get;init;}
    public Guid TasaId {get;init;}
    public string Caja {get;init;}="";
    public string Documento {get;init;}="";
    public string Nombre {get;init;}="";
    public TipoECF Tipo {get;init;}=TipoECF.E32;
    public int Cantidad {get;init;}=1;
    public MedioPago Medio {get;init;}
    public decimal Recibido {get;init;}
    public decimal Tarjeta {get;init;}
    public decimal Transferencia {get;init;}
    public decimal EnLinea {get;init;}
    public Dictionary<decimal,int> Denominaciones {get;init;}=[];
    public string Referencia {get;init;}="";
    public string Motivo {get;init;}="";
    public string Codigo {get;init;}="";
    public decimal Precio {get;init;}
    public decimal Impuesto {get;init;}
    public string Cuenta {get;init;}="";
    public string Resolucion {get;init;}="";
    public DateOnly Desde {get;init;}
    public DateOnly Hasta {get;init;}
    public long Inicio {get;init;}
    public long Fin {get;init;}
}
public sealed class CajaValidator:AbstractValidator<CajaCommand>
{
    public CajaValidator()
    {
        RuleFor(x=>x.Accion).Must(x=>new[]{"abrir","orden","cobrar","arquear","cerrar","forzar-cierre","solicitar-nota","aprobar-nota","tasa","secuencia"}.Contains(x));
        RuleFor(x=>x.Medio).IsInEnum(); RuleFor(x=>x.Tipo).IsInEnum();
        RuleFor(x=>x.Cantidad).InclusiveBetween(1,1000);
        RuleFor(x=>x.Recibido).Must(Money.Valid);
        RuleFor(x=>x.Motivo).MaximumLength(1000); RuleFor(x=>x.Nombre).MaximumLength(200);
        RuleFor(x=>x.Referencia).MaximumLength(100); RuleFor(x=>x.Documento).MaximumLength(11);
        When(x=>x.Accion=="orden",()=>{RuleFor(x=>x.Nombre).NotEmpty(); RuleFor(x=>x.Documento).Matches(@"^(\d{9}|\d{11})$"); RuleFor(x=>x.Tipo).NotEqual(TipoECF.E34);});
        When(x=>x.Accion=="abrir",()=>RuleFor(x=>x.Caja).NotEmpty().MaximumLength(60));
        When(x=>x.Accion=="tasa",()=>{
            RuleFor(x=>x.Codigo).Matches(@"^[A-Z0-9_-]{1,40}$"); RuleFor(x=>x.Nombre).NotEmpty();
            RuleFor(x=>x.Precio).Must(Money.Valid).GreaterThan(0); RuleFor(x=>x.Impuesto).InclusiveBetween(0,1);
            RuleFor(x=>x.Cuenta).Matches(@"^[0-9.-]{1,40}$");RuleFor(x=>x.Resolucion).NotEmpty().MaximumLength(200);
            RuleFor(x=>x.Hasta).GreaterThanOrEqualTo(x=>x.Desde);
        });
        When(x=>x.Accion=="secuencia",()=>{RuleFor(x=>x.Inicio).InclusiveBetween(1,9999999999);RuleFor(x=>x.Fin).GreaterThanOrEqualTo(x=>x.Inicio).LessThanOrEqualTo(9999999999);});
    }
}
