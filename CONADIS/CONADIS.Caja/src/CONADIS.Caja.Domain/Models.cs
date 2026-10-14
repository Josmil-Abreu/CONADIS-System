namespace CONADIS.Caja.Domain;

public record Result<T>(bool Ok, T? Value, string Code, string Error)
{
    public static Result<T> Success(T value) => new(true, value, "", "");
    public static Result<T> Fail(string code, string error) => new(false, default, code, error);
}
public enum MedioPago { Efectivo, Tarjeta, Transferencia, EnLinea }
public enum TipoECF { E31 = 31, E32 = 32, E34 = 34, E45 = 45 }
public enum EstadoSesion { Abierta, PendienteAprobacion, Cerrada }
public enum EstadoDgii { Pendiente, EnProceso, Aceptado, AceptadoCondicional, Rechazado, Contingencia }
public static class Money
{
    public static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.ToEven);
    public static bool Valid(decimal value) => value >= 0 && value <= 9999999999999999.99m && Round(value) == value;
    public static readonly decimal[] Denominations = [2000,1000,500,200,100,50,25,10,5,1];
    public static Result<decimal> Count(IReadOnlyDictionary<decimal,int> values) =>
        values.Count == 0 || values.Any(x => !Denominations.Contains(x.Key) || x.Value < 0 || x.Value > 100000)
        ? Result<decimal>.Fail("CONTEO_INVALIDO", "Revise las denominaciones y cantidades.")
        : Result<decimal>.Success(values.Sum(x => x.Key * x.Value));
}
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
    public byte[] RowVersion { get; private set; } = [];
}
public sealed class Tasa : Entity
{
    private Tasa() { }
    public Tasa(string codigo, string descripcion, decimal precio, decimal impuesto, DateOnly desde, DateOnly hasta, string cuenta, string resolucion)
    { Codigo=codigo; Descripcion=descripcion; Precio=precio; Impuesto=impuesto; Desde=desde; Hasta=hasta; Cuenta=cuenta; Resolucion=resolucion; }
    public string Codigo { get; private set; } = "";
    public string Descripcion { get; private set; } = "";
    public decimal Precio { get; private set; }
    public decimal Impuesto { get; private set; }
    public DateOnly Desde { get; private set; }
    public DateOnly Hasta { get; private set; }
    public string Cuenta { get; private set; } = "";
    public string Resolucion { get; private set; } = "";
    public bool Vigente(DateOnly date) => Desde <= date && Hasta >= date;
}
public sealed class OrdenCobro : Entity
{
    private OrdenCobro() { }
    public OrdenCobro(string numero, string sucursal, Guid tasaId, int cantidad, string documento, string nombre, TipoECF tipo, string? tramite)
    { Numero=numero; Sucursal=sucursal; TasaId=tasaId; Cantidad=cantidad; Documento=documento; Nombre=nombre; Tipo=tipo; Tramite=tramite; }
    public string Numero { get; private set; } = "";
    public string Sucursal { get; private set; } = "";
    public Guid TasaId { get; private set; }
    public int Cantidad { get; private set; }
    public string Documento { get; private set; } = "";
    public string Nombre { get; private set; } = "";
    public TipoECF Tipo { get; private set; }
    public string? Tramite { get; private set; }
    public bool Pagada { get; private set; }
    public void MarcarPagada() => Pagada=true;
}
public sealed class SesionCaja : Entity
{
    private SesionCaja() { }
    public SesionCaja(string caja, string sucursal, string cajero, DateOnly fecha, decimal fondo, string denominaciones)
    { Caja=caja; Sucursal=sucursal; Cajero=cajero; Fecha=fecha; Fondo=fondo; DenominacionesApertura=denominaciones; }
    public string Caja { get; private set; } = "";
    public string Sucursal { get; private set; } = "";
    public string Cajero { get; private set; } = "";
    public DateOnly Fecha { get; private set; }
    public decimal Fondo { get; private set; }
    public string DenominacionesApertura { get; private set; } = "";
    public string? DenominacionesCierre { get; private set; }
    public EstadoSesion Estado { get; private set; }
    public decimal? DeclaradoEfectivo { get; private set; }
    public decimal? DeclaradoTarjeta { get; private set; }
    public decimal? DeclaradoTransferencia { get; private set; }
    public decimal? DeclaradoEnLinea { get; private set; }
    public decimal? DiferenciaEfectivo { get; private set; }
    public decimal? DiferenciaTarjeta { get; private set; }
    public decimal? DiferenciaTransferencia { get; private set; }
    public decimal? DiferenciaEnLinea { get; private set; }
    public string? Justificacion { get; private set; }
    public string? Supervisor { get; private set; }
    public DateTimeOffset? CerradaEn { get; private set; }
    public bool Forzado { get; private set; }
    public string? FirmaCajero { get; private set; }
    public string? FirmaSupervisor { get; private set; }
    public Result<bool> Arquear(decimal efectivo, decimal tarjeta, decimal transferencia, decimal enLinea, Totales esperado, string denominaciones, string firma)
    {
        if(Estado != EstadoSesion.Abierta) return Result<bool>.Fail("SESION_BLOQUEADA","La sesión ya tiene un arqueo enviado.");
        if(!new[]{efectivo,tarjeta,transferencia,enLinea}.All(Money.Valid)) return Result<bool>.Fail("MONTO_INVALIDO","Revise los importes declarados.");
        DeclaradoEfectivo=efectivo; DeclaradoTarjeta=tarjeta; DeclaradoTransferencia=transferencia; DeclaradoEnLinea=enLinea;
        DiferenciaEfectivo=efectivo-esperado.Efectivo; DiferenciaTarjeta=tarjeta-esperado.Tarjeta;
        DiferenciaTransferencia=transferencia-esperado.Transferencia; DiferenciaEnLinea=enLinea-esperado.EnLinea;
        DenominacionesCierre=denominaciones; FirmaCajero=firma; Estado=EstadoSesion.PendienteAprobacion;
        return Result<bool>.Success(true);
    }
    public Result<bool> Cerrar(string supervisor, decimal tolerancia, string motivo, string firma, DateTimeOffset ahora, bool forzado=false)
    {
        if(supervisor==Cajero) return Result<bool>.Fail("SEGREGACION","Otra persona debe aprobar el cierre.");
        if(Estado!=EstadoSesion.PendienteAprobacion) return Result<bool>.Fail("ESTADO_INVALIDO","Debe enviarse el arqueo antes del cierre.");
        var diferencias=new[]{DiferenciaEfectivo!.Value,DiferenciaTarjeta!.Value,DiferenciaTransferencia!.Value,DiferenciaEnLinea!.Value};
        if((forzado || diferencias.Any(x=>Math.Abs(x)>tolerancia)) && string.IsNullOrWhiteSpace(motivo))
            return Result<bool>.Fail("JUSTIFICACION","Escriba la justificación de las diferencias.");
        Supervisor=supervisor; Justificacion=motivo; FirmaSupervisor=firma; CerradaEn=ahora; Forzado=forzado; Estado=EstadoSesion.Cerrada;
        return Result<bool>.Success(true);
    }
}
public record Totales(decimal Efectivo, decimal Tarjeta, decimal Transferencia, decimal EnLinea);
public sealed class SecuenciaENcf : Entity
{
    private SecuenciaENcf() { }
    public SecuenciaENcf(TipoECF tipo,long desde,long hasta,DateOnly vence) { Tipo=tipo; Desde=desde; Hasta=hasta; Siguiente=desde; Vence=vence; }
    public TipoECF Tipo {get;private set;}
    public long Desde {get;private set;}
    public long Hasta {get;private set;}
    public long Siguiente {get;private set;}
    public DateOnly Vence {get;private set;}
    public bool Alerta(DateOnly hoy) => Siguiente > Hasta || (Hasta-Siguiente+1m)/(Hasta-Desde+1m)<=0.1m || Vence.DayNumber-hoy.DayNumber<=30;
    public Result<string> Consumir(DateOnly hoy)
    {
        if(hoy>Vence || Siguiente>Hasta) return Result<string>.Fail("SECUENCIA_AGOTADA","No hay secuencia vigente disponible para este comprobante.");
        return Result<string>.Success($"E{(int)Tipo:00}{Siguiente++:0000000000}");
    }
}
public sealed class FacturaCaja : Entity
{
    private FacturaCaja() { }
    public FacturaCaja(Guid sesion, Guid orden, TipoECF tipo, string encf, string documento, string cliente, Tasa tasa, int cantidad, MedioPago medio, decimal recibido, string? referencia, DateTimeOffset fecha, Guid? original=null)
    { SesionCajaId=sesion; OrdenCobroId=orden; Tipo=tipo; ENcf=encf; Documento=documento; Cliente=cliente; CodigoTasa=tasa.Codigo; Concepto=tasa.Descripcion; Cuenta=tasa.Cuenta; Cantidad=cantidad; Precio=tasa.Precio; Subtotal=Money.Round(Precio*cantidad); Itbis=Money.Round(Subtotal*tasa.Impuesto); Total=Subtotal+Itbis; Medio=medio; Recibido=recibido; Referencia=referencia; Fecha=fecha; FacturaReferenciaId=original; }
    public Guid SesionCajaId {get;private set;}
    public Guid OrdenCobroId {get;private set;}
    public TipoECF Tipo {get;private set;}
    public string ENcf {get;private set;} = "";
    public string Documento {get;private set;} = "";
    public string Cliente {get;private set;} = "";
    public string CodigoTasa {get;private set;} = "";
    public string Concepto {get;private set;} = "";
    public string Cuenta {get;private set;} = "";
    public int Cantidad {get;private set;}
    public decimal Precio {get;private set;}
    public decimal Subtotal {get;private set;}
    public decimal Itbis {get;private set;}
    public decimal Total {get;private set;}
    public MedioPago Medio {get;private set;}
    public decimal Recibido {get;private set;}
    public decimal Cambio => Medio==MedioPago.Efectivo && Tipo!=TipoECF.E34 ? Recibido-Total : 0;
    public string? Referencia {get;private set;}
    public DateTimeOffset Fecha {get;private set;}
    public Guid? FacturaReferenciaId {get;private set;}
    public string XmlFirmado {get;private set;} = "";
    public string CodigoSeguridad {get;private set;} = "";
    public string QrUrl {get;private set;} = "";
    public byte[]? QrPng {get;private set;}
    public string? TrackId {get;private set;}
    public string? MensajeDgii {get;private set;}
    public EstadoDgii EstadoDgii {get;private set;}
    public bool EsPrueba {get;private set;}
    public void Firmar(string xml,string codigo,string qr,bool prueba,byte[]? qrPng=null) { XmlFirmado=xml;CodigoSeguridad=codigo;QrUrl=qr;EsPrueba=prueba;QrPng=qrPng; }
    public void Responder(EstadoDgii estado,string? track,string? mensaje) { EstadoDgii=estado; TrackId=track??TrackId; MensajeDgii=mensaje; }
}
public sealed class NotaCredito : Entity
{
    private NotaCredito(){}
    public NotaCredito(Guid factura,string solicitante,string motivo) { FacturaId=factura;Solicitante=solicitante;Motivo=motivo; }
    public Guid FacturaId {get;private set;}
    public string Solicitante {get;private set;}="";
    public string Motivo {get;private set;}="";
    public string? Aprobador {get;private set;}
    public Guid? ComprobanteId {get;private set;}
    public Result<bool> Aprobar(string usuario,Guid comprobante)
    {
        if(usuario==Solicitante) return Result<bool>.Fail("SEGREGACION","Otra persona debe aprobar la nota de crédito.");
        if(ComprobanteId!=null) return Result<bool>.Fail("NOTA_APROBADA","La nota ya está aprobada.");
        Aprobador=usuario; ComprobanteId=comprobante; return Result<bool>.Success(true);
    }
}
public sealed class Outbox : Entity
{
    private Outbox(){}
    public Outbox(string tipo,Guid recurso,DateTimeOffset ahora){Tipo=tipo;RecursoId=recurso;ProximoIntento=ahora;}
    public string Tipo {get;private set;}="";
    public Guid RecursoId {get;private set;}
    public int Intentos {get;private set;}
    public DateTimeOffset ProximoIntento {get;private set;}
    public bool Completado {get;private set;}
    public string? Error {get;private set;}
    public void Completar(){Completado=true;Error=null;}
    public void Reintentar(DateTimeOffset ahora,string error){Intentos++;ProximoIntento=ahora.AddSeconds(Math.Min(3600,Math.Pow(2,Math.Min(Intentos,10))*15));Error=error;}
}
public sealed class Operacion : Entity
{
    private Operacion(){}
    public Operacion(string clave,string hash,string respuesta){Clave=clave;Hash=hash;Respuesta=respuesta;}
    public string Clave {get;private set;}="";
    public string Hash {get;private set;}="";
    public string Respuesta {get;private set;}="";
}
public sealed class EventoAuditoria : Entity
{
    private EventoAuditoria(){}
    public EventoAuditoria(long numero,string usuario,string accion,DateTimeOffset fecha,string previo,string hash){Numero=numero;Usuario=usuario;Accion=accion;Fecha=fecha;Previo=previo;Hash=hash;}
    public long Numero {get;private set;}
    public string Usuario {get;private set;}="";
    public string Accion {get;private set;}="";
    public DateTimeOffset Fecha {get;private set;}
    public string Previo {get;private set;}="";
    public string Hash {get;private set;}="";
}
