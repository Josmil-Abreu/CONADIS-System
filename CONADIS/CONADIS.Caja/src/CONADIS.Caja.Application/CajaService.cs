using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CONADIS.Caja.Domain;
namespace CONADIS.Caja.Application;

public sealed class CajaService(ICajaStore db,IFiscalGateway fiscal,IVerificadorPago pagos,IFirmaCierre firmas,TimeProvider clock)
{
    public DateOnly Hoy => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(),TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo")).DateTime);
    static Result<Guid> Fail(string code,string message)=>Result<Guid>.Fail(code,message);
    public async Task<Result<Guid>> Ejecutar(Actor actor,string clave,CajaCommand c,decimal tolerancia=0,CancellationToken ct=default)
    {
        var validation=new CajaValidator().Validate(c);
        if(!validation.IsValid || string.IsNullOrWhiteSpace(clave) || clave.Length>100 || tolerancia<0)
            return Fail("VALIDACION","Revise los campos obligatorios, importes y fechas.");
        var supervisor=c.Accion is "cerrar" or "forzar-cierre" or "aprobar-nota" or "tasa" or "secuencia";
        if(supervisor ? actor.Rol!="SupervisorCaja" : actor.Rol!="Cajero") return Fail("PERMISO","Su rol no permite esta operación.");
        var key=actor.Id+":"+clave;
        var fingerprint=Hash(JsonSerializer.Serialize(c));
        await using var tx=await db.Begin(ct);
        var previous=db.Query<Operacion>().SingleOrDefault(x=>x.Clave==key);
        if(previous!=null) return previous.Hash==fingerprint ? Result<Guid>.Success(Guid.Parse(previous.Respuesta)) : Fail("IDEMPOTENCIA","La referencia de operación ya se usó con otros datos.");
        var result=await Dispatch(actor,c,tolerancia,ct);
        if(!result.Ok){db.Clear(); return result;}
        db.Add(new Operacion(key,fingerprint,result.Value.ToString()));
        Audit(actor.Id,c.Accion+":"+result.Value);
        await db.Save(ct); await tx.Commit(ct); return result;
    }
    public void Audit(string usuario,string accion)
    {
        var last=db.Query<EventoAuditoria>().OrderByDescending(x=>x.Numero).FirstOrDefault();
        var now=clock.GetUtcNow(); var number=(last?.Numero??0)+1; var prev=last?.Hash??"GENESIS";
        db.Add(new EventoAuditoria(number,usuario,accion,now,prev,Hash($"{number}|{usuario}|{accion}|{now:O}|{prev}")));
    }
    public static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    async Task<Result<Guid>> Dispatch(Actor a,CajaCommand c,decimal tolerancia,CancellationToken ct)
    {
        switch(c.Accion)
        {
            case "abrir":
                var count=Money.Count(c.Denominaciones); if(!count.Ok)return Fail(count.Code,count.Error);
                if(db.Query<SesionCaja>().Any(x=>x.Estado!=EstadoSesion.Cerrada && (x.Cajero==a.Id || (x.Caja==c.Caja && x.Sucursal==a.Sucursal))))return Fail("CAJA_OCUPADA","El cajero o la caja tienen una sesión pendiente de cierre.");
                var session=new SesionCaja(c.Caja,a.Sucursal,a.Id,Hoy,count.Value,JsonSerializer.Serialize(c.Denominaciones));db.Add(session);return Result<Guid>.Success(session.Id);
            case "tasa":
                if(c.Codigo is "CARNET_INICIAL" or "CERTIFICACION_DISCAPACIDAD")return Fail("SERVICIO_GRATUITO","Este servicio es gratuito.");
                if(db.Query<Tasa>().Any(x=>x.Codigo==c.Codigo && x.Desde<=c.Hasta && x.Hasta>=c.Desde))return Fail("VIGENCIA_SOLAPADA","Ya existe una tasa para ese periodo.");
                var tasa=new Tasa(c.Codigo,c.Nombre,c.Precio,c.Impuesto,c.Desde,c.Hasta,c.Cuenta,c.Resolucion);db.Add(tasa);return Result<Guid>.Success(tasa.Id);
            case "secuencia":
                if(c.Hasta<Hoy || db.Query<SecuenciaENcf>().Any(x=>x.Tipo==c.Tipo && x.Desde<=c.Fin && x.Hasta>=c.Inicio))return Fail("SECUENCIA_INVALIDA","El rango está vencido o coincide con uno existente.");
                var sequence=new SecuenciaENcf(c.Tipo,c.Inicio,c.Fin,c.Hasta);db.Add(sequence);return Result<Guid>.Success(sequence.Id);
            case "orden":
                var rate=db.Query<Tasa>().SingleOrDefault(x=>x.Id==c.TasaId);
                if(rate==null || !rate.Vigente(Hoy))return Fail("TASA_INVALIDA","Seleccione una tasa vigente.");
                var number=db.Query<OrdenCobro>().Count()+1;
                var order=new OrdenCobro($"OC-{Hoy.Year}-{number:0000000}",a.Sucursal,rate.Id,c.Cantidad,c.Documento,c.Nombre,c.Tipo,string.IsNullOrWhiteSpace(c.Referencia)?null:c.Referencia);db.Add(order);return Result<Guid>.Success(order.Id);
            case "cobrar": return await Cobrar(a,c,ct);
            case "arquear":
                var s=OwnSession(a,c.Id,false);if(s==null)return Fail("SESION_INVALIDA","No tiene acceso a esta sesión.");
                var cash=Money.Count(c.Denominaciones);if(!cash.Ok)return Fail(cash.Code,cash.Error);
                var sign=await firmas.Firmar(a.Id,s.Id,JsonSerializer.Serialize(c),ct);
                var check=s.Arquear(cash.Value,c.Tarjeta,c.Transferencia,c.EnLinea,Totalizar(s),JsonSerializer.Serialize(c.Denominaciones),sign);
                return check.Ok?Result<Guid>.Success(s.Id):Fail(check.Code,check.Error);
            case "forzar-cierre":
                var overdue=db.Query<SesionCaja>().SingleOrDefault(x=>x.Id==c.Id && x.Sucursal==a.Sucursal);
                if(overdue==null || overdue.Fecha>=Hoy || overdue.Cajero==a.Id || overdue.Estado==EstadoSesion.Cerrada || string.IsNullOrWhiteSpace(c.Motivo))return Fail("CIERRE_FORZADO_INVALIDO","Seleccione una sesión anterior, de otro cajero, y documente el motivo.");
                if(overdue.Estado==EstadoSesion.Abierta)
                {
                    var declared=Money.Count(c.Denominaciones);if(!declared.Ok)return Fail(declared.Code,declared.Error);
                    var counted=overdue.Arquear(declared.Value,c.Tarjeta,c.Transferencia,c.EnLinea,Totalizar(overdue),JsonSerializer.Serialize(c.Denominaciones),"CAJERO AUSENTE: conteo administrativo por "+a.Id);
                    if(!counted.Ok)return Fail(counted.Code,counted.Error);
                }
                var forcedSignature=await firmas.Firmar(a.Id,overdue.Id,JsonSerializer.Serialize(c),ct);
                var forced=overdue.Cerrar(a.Id,tolerancia,c.Motivo,forcedSignature,clock.GetUtcNow(),true);
                if(!forced.Ok)return Fail(forced.Code,forced.Error);
                db.Add(new Outbox("CierreAprobado",overdue.Id,clock.GetUtcNow()));return Result<Guid>.Success(overdue.Id);
            case "cerrar":
                var close=db.Query<SesionCaja>().SingleOrDefault(x=>x.Id==c.Id && x.Sucursal==a.Sucursal);
                if(close==null)return Fail("NO_ENCONTRADO","No se encontró la sesión.");
                var signature=await firmas.Firmar(a.Id,close.Id,JsonSerializer.Serialize(c),ct);
                var closed=close.Cerrar(a.Id,tolerancia,c.Motivo,signature,clock.GetUtcNow());
                if(!closed.Ok)return Fail(closed.Code,closed.Error);
                db.Add(new Outbox("CierreAprobado",close.Id,clock.GetUtcNow()));return Result<Guid>.Success(close.Id);
            case "solicitar-nota":
                var original=FindFactura(a,c.Id);
                if(original==null || original.Tipo==TipoECF.E34 || string.IsNullOrWhiteSpace(c.Motivo))return Fail("NOTA_INVALIDA","Seleccione una factura y escriba el motivo.");
                if(db.Query<NotaCredito>().Any(x=>x.FacturaId==c.Id))return Fail("NOTA_EXISTENTE","Esta factura ya tiene una solicitud de nota de crédito.");
                var note=new NotaCredito(c.Id,a.Id,c.Motivo);db.Add(note);return Result<Guid>.Success(note.Id);
            case "aprobar-nota": return await AprobarNota(a,c,ct);
            default:return Fail("ACCION_INVALIDA","La operación no existe.");
        }
    }
    SesionCaja? OwnSession(Actor a,Guid id,bool today)=>db.Query<SesionCaja>().SingleOrDefault(x=>x.Id==id && x.Cajero==a.Id && x.Sucursal==a.Sucursal && (!today || x.Fecha==Hoy));
    public FacturaCaja? FindFactura(Actor a,Guid id)
    {
        var f=db.Query<FacturaCaja>().SingleOrDefault(x=>x.Id==id);if(f==null)return null;
        return db.Query<SesionCaja>().Any(x=>x.Id==f.SesionCajaId && x.Sucursal==a.Sucursal && (a.Rol=="SupervisorCaja" || x.Cajero==a.Id))?f:null;
    }
    async Task<Result<Guid>> Cobrar(Actor a,CajaCommand c,CancellationToken ct)
    {
        var s=OwnSession(a,c.SesionId,true);
        if(s==null || s.Estado!=EstadoSesion.Abierta)return Fail("CAJA_NO_ABIERTA","Abra una caja para la fecha actual.");
        var o=db.Query<OrdenCobro>().SingleOrDefault(x=>x.Id==c.Id && x.Sucursal==a.Sucursal);
        if(o==null || o.Pagada)return Fail("ORDEN_INVALIDA","La orden no existe o ya está pagada.");
        var t=db.Query<Tasa>().Single(x=>x.Id==o.TasaId);
        if(!t.Vigente(Hoy))return Fail("TASA_VENCIDA","La tasa venció; genere una nueva orden con la tasa vigente.");
        var subtotal=Money.Round(t.Precio*o.Cantidad);var total=subtotal+Money.Round(subtotal*t.Impuesto);
        if(c.Medio==MedioPago.Efectivo && c.Recibido<total)return Fail("EFECTIVO_INSUFICIENTE","El efectivo recibido no cubre el total.");
        if(c.Medio!=MedioPago.Efectivo)
        {
            if(string.IsNullOrWhiteSpace(c.Referencia))return Fail("REFERENCIA_REQUERIDA","Indique la referencia de autorización.");
            if(db.Query<FacturaCaja>().Any(x=>x.Referencia==c.Referencia && x.Tipo!=TipoECF.E34))return Fail("PAGO_REUTILIZADO","Esta autorización ya está registrada.");
            if(!await pagos.Verificar(c.Referencia,o.Id,total,c.Medio,ct))return Fail("PAGO_NO_CONFIRMADO","El proveedor no confirmó este pago para la orden y el importe.");
        }
        if(o.Tipo==TipoECF.E31 && !await fiscal.ValidarRnc(o.Documento,ct))return Fail("RNC_INACTIVO","RNC no validado. Puede crear una orden E32 si el cliente lo acepta.");
        var f=await Emitir(s.Id,o,t,c.Medio,c.Medio==MedioPago.Efectivo?c.Recibido:total,c.Referencia,null,ct);
        if(!f.Ok)return f;
        o.MarcarPagada();db.Add(new Outbox("OrdenPagada",o.Id,clock.GetUtcNow()));return f;
    }
    async Task<Result<Guid>> Emitir(Guid session,OrdenCobro o,Tasa t,MedioPago medio,decimal recibido,string? referencia,Guid? original,CancellationToken ct)
    {
        if(!await fiscal.CertificadoVigente(ct))return Fail("CERTIFICADO_INVALIDO","El certificado tributario no está vigente.");
        var tipo=original==null?o.Tipo:TipoECF.E34;
        var seq=db.Query<SecuenciaENcf>().Where(x=>x.Tipo==tipo && x.Vence>=Hoy && x.Siguiente<=x.Hasta).OrderBy(x=>x.Desde).FirstOrDefault();
        if(seq==null)return Fail("SECUENCIA_AGOTADA","No hay secuencia vigente disponible.");
        var number=seq.Consumir(Hoy);if(!number.Ok)return Fail(number.Code,number.Error);
        var f=new FacturaCaja(session,o.Id,tipo,number.Value!,o.Documento,o.Nombre,t,o.Cantidad,medio,recibido,referencia,clock.GetUtcNow(),original);
        var document=await fiscal.Firmar(f,ct);
        if(string.IsNullOrWhiteSpace(document.Xml)||string.IsNullOrWhiteSpace(document.CodigoSeguridad)||(!document.EsPrueba && (document.QrPng is not {Length:>0} || !Uri.TryCreate(document.QrUrl,UriKind.Absolute,out _))))return Fail("FIRMA_INVALIDA","No fue posible firmar el comprobante.");
        f.Firmar(document.Xml,document.CodigoSeguridad,document.QrUrl,document.EsPrueba,document.QrPng);db.Add(f);db.Add(new Outbox("EnviarECF",f.Id,clock.GetUtcNow()));return Result<Guid>.Success(f.Id);
    }
    async Task<Result<Guid>> AprobarNota(Actor a,CajaCommand c,CancellationToken ct)
    {
        var n=db.Query<NotaCredito>().SingleOrDefault(x=>x.Id==c.Id);
        if(n==null || n.Solicitante==a.Id || n.ComprobanteId!=null)return Fail("NOTA_INVALIDA","La nota debe estar pendiente y ser aprobada por otra persona.");
        var f=FindFactura(a,n.FacturaId);if(f==null)return Fail("NO_ENCONTRADO","No se encontró la factura.");
        // La nota registra corrección fiscal, no mueve efectivo: el reembolso se concilia externamente.
        var s=db.Query<SesionCaja>().SingleOrDefault(x=>x.Id==c.SesionId && x.Sucursal==a.Sucursal && x.Fecha==Hoy && x.Estado==EstadoSesion.Abierta && x.Cajero==n.Solicitante);
        if(s==null)return Fail("CAJA_NO_ABIERTA","El solicitante debe tener una caja abierta hoy.");
        var o=db.Query<OrdenCobro>().Single(x=>x.Id==f.OrdenCobroId);
        var rate=new Tasa(f.CodigoTasa,f.Concepto,f.Precio,f.Subtotal==0?0:f.Itbis/f.Subtotal,Hoy,Hoy,f.Cuenta,"Corrección de "+f.ENcf);
        var emitted=await Emitir(s.Id,o,rate,f.Medio,0,null,f.Id,ct);if(!emitted.Ok)return emitted;
        n.Aprobar(a.Id,emitted.Value);db.Add(new Outbox("NotaCreditoAprobada",emitted.Value,clock.GetUtcNow()));return emitted;
    }
    public Totales Totalizar(SesionCaja s)
    {
        var rows=db.Query<FacturaCaja>().Where(x=>x.SesionCajaId==s.Id && x.Tipo!=TipoECF.E34).ToList();
        decimal Sum(MedioPago medio)=>rows.Where(x=>x.Medio==medio).Sum(x=>x.Total);
        return new(s.Fondo+Sum(MedioPago.Efectivo),Sum(MedioPago.Tarjeta),Sum(MedioPago.Transferencia),Sum(MedioPago.EnLinea));
    }
}
