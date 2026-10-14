using CONADIS.Caja.Domain;
using CONADIS.Caja.Application;
using CONADIS.Caja.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace CONADIS.Caja.Tests;
public sealed class CajaTests:IAsyncLifetime
{
    readonly SqliteConnection connection=new("Data Source=:memory:");
    CajaDbContext db=null!;CajaService service=null!;
    readonly Actor cashier=new("cajero","Cajero","SEDE");readonly Actor supervisor=new("supervisor","SupervisorCaja","SEDE");
    readonly FixedClock clock=new();Tasa tasa=null!;
    public async Task InitializeAsync()
    {
        await connection.OpenAsync();db=new(new DbContextOptionsBuilder<CajaDbContext>().UseSqlite(connection).Options);await db.Database.EnsureCreatedAsync();
        tasa=new("DUPLICADO","Duplicado",500,0,new(2020,1,1),new(2040,1,1),"4101","TEST");db.Add(tasa);
        foreach(var t in Enum.GetValues<TipoECF>())db.Add(new SecuenciaENcf(t,1,10,new(2040,1,1)));await db.SaveChangesAsync();
        service=new(new EfCajaStore(db),new DemoFiscal(),new DemoPagos(),new DemoFirma(),clock);
    }
    public async Task DisposeAsync(){await db.DisposeAsync();await connection.DisposeAsync();}
    Task<Result<Guid>> Run(CajaCommand c,Actor? a=null,string? key=null)=>service.Ejecutar(a??cashier,key??Guid.NewGuid().ToString(),c);
    async Task<Guid> Open(){var r=await Run(new(){Accion="abrir",Caja="01",Denominaciones=new(){[100]=10}});Assert.True(r.Ok,r.Error);return r.Value;}
    async Task<Guid> Order(){var r=await Run(new(){Accion="orden",TasaId=tasa.Id,Documento="00100000001",Nombre="Prueba"});Assert.True(r.Ok,r.Error);return r.Value;}
    [Fact]public async Task Cobro_y_reintento_no_duplican_efectos()
    {
        var s=await Open();var o=await Order();var c=new CajaCommand{Accion="cobrar",SesionId=s,Id=o,Recibido=1000};
        var one=await Run(c,key:"same");var two=await Run(c,key:"same");Assert.True(one.Ok,one.Error);Assert.Equal(one.Value,two.Value);
        Assert.Single(db.Set<FacturaCaja>());Assert.Equal(2,db.Set<SecuenciaENcf>().Single(x=>x.Tipo==TipoECF.E32).Siguiente);
        Assert.Equal(500,db.Set<FacturaCaja>().Single().Cambio);Assert.Equal(2,db.Set<Outbox>().Count());
    }
    [Fact]public async Task Misma_clave_con_otro_contenido_se_rechaza(){await Run(new(){Accion="abrir",Caja="01",Denominaciones=new(){[100]=1}},key:"same");var r=await Run(new(){Accion="abrir",Caja="02",Denominaciones=new(){[100]=1}},key:"same");Assert.Equal("IDEMPOTENCIA",r.Code);}
    [Fact]public async Task Efectivo_insuficiente_no_consume_secuencia(){var s=await Open();var o=await Order();var r=await Run(new(){Accion="cobrar",SesionId=s,Id=o,Recibido=499});Assert.Equal("EFECTIVO_INSUFICIENTE",r.Code);Assert.Empty(db.Set<FacturaCaja>());Assert.Equal(1,db.Set<SecuenciaENcf>().Single(x=>x.Tipo==TipoECF.E32).Siguiente);}
    [Fact]public async Task Caja_no_abierta_impide_cobrar(){var o=await Order();var r=await Run(new(){Accion="cobrar",Id=o,Recibido=500});Assert.Equal("CAJA_NO_ABIERTA",r.Code);}
    [Fact]public async Task No_se_puede_abrir_dos_cajas(){await Open();var r=await Run(new(){Accion="abrir",Caja="02",Denominaciones=new(){[1]=1}});Assert.Equal("CAJA_OCUPADA",r.Code);}
    [Fact]public async Task No_se_puede_cobrar_orden_dos_veces(){var s=await Open();var o=await Order();var c=new CajaCommand{Accion="cobrar",SesionId=s,Id=o,Recibido=500};Assert.True((await Run(c)).Ok);Assert.Equal("ORDEN_INVALIDA",(await Run(c)).Code);}
    [Theory][InlineData(MedioPago.Tarjeta)][InlineData(MedioPago.Transferencia)][InlineData(MedioPago.EnLinea)]
    public async Task Pago_externo_se_verifica(MedioPago medio){var s=await Open();var o=await Order();var c=new CajaCommand{Accion="cobrar",SesionId=s,Id=o,Medio=medio,Referencia="NO-APROBADO"};Assert.Equal("PAGO_NO_CONFIRMADO",(await Run(c)).Code);Assert.True((await Run(c with{Referencia=$"DEMO-{o:N}"})).Ok);}
    [Fact]public async Task Otro_cajero_no_puede_usar_la_sesion(){var s=await Open();var o=await Order();var r=await Run(new(){Accion="cobrar",SesionId=s,Id=o,Recibido=500},new("otro","Cajero","SEDE"));Assert.Equal("CAJA_NO_ABIERTA",r.Code);}
    [Fact]public async Task Otra_sucursal_no_puede_consultar_factura(){var s=await Open();var o=await Order();var r=await Run(new(){Accion="cobrar",SesionId=s,Id=o,Recibido=500});Assert.Null(service.FindFactura(new("supervisor","SupervisorCaja","OTRA"),r.Value));}
    [Fact]public async Task Arqueo_bloquea_cobros_y_exige_justificacion()
    {
        var s=await Open();var o=await Order();Assert.True((await Run(new(){Accion="arquear",Id=s,Denominaciones=new(){[100]=9}})).Ok);
        Assert.Equal("CAJA_NO_ABIERTA",(await Run(new(){Accion="cobrar",SesionId=s,Id=o,Recibido=500})).Code);
        Assert.Equal("JUSTIFICACION",(await Run(new(){Accion="cerrar",Id=s},supervisor)).Code);
        Assert.True((await Run(new(){Accion="cerrar",Id=s,Motivo="Faltante documentado"},supervisor)).Ok);
        Assert.Equal(EstadoSesion.Cerrada,db.Set<SesionCaja>().Single().Estado);
    }
    [Fact]public async Task No_se_compensan_diferencias_entre_medios(){var s=await Open();await Run(new(){Accion="arquear",Id=s,Denominaciones=new(){[100]=9},Tarjeta=100});Assert.Equal("JUSTIFICACION",(await Run(new(){Accion="cerrar",Id=s},supervisor)).Code);}
    [Fact]public async Task Cierre_propio_se_rechaza(){var s=await Open();await Run(new(){Accion="arquear",Id=s,Denominaciones=new(){[100]=10}});Assert.Equal("SEGREGACION",(await Run(new(){Accion="cerrar",Id=s},cashier with{Rol="SupervisorCaja"})).Code);}
    [Fact]public async Task Nota_total_requiere_otro_aprobador_y_conserva_original()
    {
        var s=await Open();var o=await Order();var f=await Run(new(){Accion="cobrar",SesionId=s,Id=o,Recibido=500});var n=await Run(new(){Accion="solicitar-nota",Id=f.Value,Motivo="Corrección"});
        Assert.True(n.Ok);Assert.False((await Run(new(){Accion="aprobar-nota",Id=n.Value,SesionId=s},cashier with{Rol="SupervisorCaja"})).Ok);
        Assert.True((await Run(new(){Accion="aprobar-nota",Id=n.Value,SesionId=s},supervisor)).Ok);Assert.Equal(2,db.Set<FacturaCaja>().Count());
        Assert.Equal(f.Value,db.Set<FacturaCaja>().Single(x=>x.Tipo==TipoECF.E34).FacturaReferenciaId);
    }
    [Fact]public async Task Cola_confirma_comprobante_y_no_repite_mensajes(){var s=await Open();var o=await Order();await Run(new(){Accion="cobrar",SesionId=s,Id=o,Recibido=500});var processor=new OutboxProcessor(new EfCajaStore(db),new DemoFiscal(),new DemoCore(),service,clock);Assert.Equal(2,await processor.Process());Assert.Equal(0,await processor.Process());Assert.Equal(EstadoDgii.Aceptado,db.Set<FacturaCaja>().Single().EstadoDgii);}
    [Fact]public async Task Auditoria_encadenada(){await Open();await Order();var events=db.Set<EventoAuditoria>().OrderBy(x=>x.Numero).ToList();Assert.Equal("GENESIS",events[0].Previo);Assert.Equal(events[0].Hash,events[1].Previo);foreach(var e in events)Assert.Equal(CajaService.Hash($"{e.Numero}|{e.Usuario}|{e.Accion}|{e.Fecha:O}|{e.Previo}"),e.Hash);}
    [Fact]public async Task Catalogo_no_admite_solapamientos(){var c=new CajaCommand{Accion="tasa",Codigo="DUPLICADO",Nombre="Duplicado",Precio=600,Cuenta="4101",Resolucion="R",Desde=new(2026,1,1),Hasta=new(2027,1,1)};Assert.Equal("VIGENCIA_SOLAPADA",(await Run(c,supervisor)).Code);}
    [Fact]public async Task Solo_supervisor_configura_tasas(){Assert.Equal("PERMISO",(await Run(new(){Accion="secuencia",Inicio=20,Fin=30,Hasta=new(2040,1,1)})).Code);}
    [Fact]public async Task No_se_eliminan_facturas(){var s=await Open();var o=await Order();await Run(new(){Accion="cobrar",SesionId=s,Id=o,Recibido=500});db.Remove(db.Set<FacturaCaja>().Single());await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());}
    [Fact]public void Secuencia_formato_y_agotamiento(){var s=new SecuenciaENcf(TipoECF.E32,1,1,new(2030,1,1));Assert.Equal("E320000000001",s.Consumir(new(2026,1,1)).Value);Assert.False(s.Consumir(new(2026,1,1)).Ok);}
    [Theory][InlineData(-1)][InlineData(0.001)]public void Monto_invalido(decimal amount)=>Assert.False(Money.Valid(amount));
    [Fact]public void Redondeo_bancario()=>Assert.Equal(1.00m,Money.Round(1.005m));
    [Fact]public void Denominacion_no_admitida()=>Assert.False(Money.Count(new Dictionary<decimal,int>{{3,2}}).Ok);
    [Fact]public async Task Firma_fallida_revierte_numero_y_orden()
    {
        var s=await Open();var o=await Order();service=new(new EfCajaStore(db),new InvalidFiscal(),new DemoPagos(),new DemoFirma(),clock);
        var r=await Run(new(){Accion="cobrar",SesionId=s,Id=o,Recibido=500});Assert.Equal("FIRMA_INVALIDA",r.Code);
        Assert.Equal(1,db.Set<SecuenciaENcf>().Single(x=>x.Tipo==TipoECF.E32).Siguiente);Assert.False(db.Set<OrdenCobro>().Single().Pagada);Assert.Empty(db.Set<FacturaCaja>());
    }
    [Fact]public async Task Dgii_caida_conserva_cobro_y_reintento_persistente()
    {
        var s=await Open();var o=await Order();await Run(new(){Accion="cobrar",SesionId=s,Id=o,Recibido=500});
        await new OutboxProcessor(new EfCajaStore(db),new InvalidFiscal(),new DemoCore(),service,clock).Process();
        Assert.Equal(EstadoDgii.Contingencia,db.Set<FacturaCaja>().Single().EstadoDgii);
        var msg=db.Set<Outbox>().Single(x=>x.Tipo=="EnviarECF");Assert.False(msg.Completado);Assert.Equal(1,msg.Intentos);Assert.True(msg.ProximoIntento>clock.GetUtcNow());Assert.True(db.Set<OrdenCobro>().Single().Pagada);
    }
    [Fact]public async Task Cierre_forzado_preserva_ausencia_de_firma_cajero()
    {
        var old=new SesionCaja("02","SEDE","ausente",new(2026,10,10),100,"{}");db.Add(old);await db.SaveChangesAsync();
        var r=await Run(new(){Accion="forzar-cierre",Id=old.Id,Motivo="Cajero ausente, conteo supervisado",Denominaciones=new(){[100]=1}},supervisor);
        Assert.True(r.Ok,r.Error);Assert.True(old.Forzado);Assert.Contains("AUSENTE",old.FirmaCajero);Assert.Equal(EstadoSesion.Cerrada,old.Estado);
    }
    [Fact]public async Task Servicio_gratuito_no_admite_tasa(){Assert.Equal("SERVICIO_GRATUITO",(await Run(new(){Accion="tasa",Codigo="CARNET_INICIAL",Nombre="Carnet",Precio=5,Cuenta="4101",Resolucion="X",Desde=new(2026,1,1),Hasta=new(2027,1,1)},supervisor)).Code);}
    sealed class InvalidFiscal:IFiscalGateway
    {
        public Task<bool> CertificadoVigente(CancellationToken c)=>Task.FromResult(true);
        public Task<bool> ValidarRnc(string d,CancellationToken c)=>Task.FromResult(false);
        public Task<DocumentoFiscal> Firmar(FacturaCaja f,CancellationToken c)=>Task.FromResult(new DocumentoFiscal("","","",true));
        public Task<RespuestaFiscal> EnviarOConsultar(FacturaCaja f,CancellationToken c)=>throw new HttpRequestException();
    }
    sealed class FixedClock:TimeProvider{public override DateTimeOffset GetUtcNow()=>new(2026,10,11,15,0,0,TimeSpan.Zero);}
}
