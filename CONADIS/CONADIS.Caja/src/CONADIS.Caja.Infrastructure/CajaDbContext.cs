using CONADIS.Caja.Domain;
using CONADIS.Caja.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage;
namespace CONADIS.Caja.Infrastructure;
public sealed class CajaDbContext(DbContextOptions<CajaDbContext> options):DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder m)
    {
        Configure<Tasa>(m,"Tasa",true);Configure<OrdenCobro>(m,"OrdenCobro");Configure<SesionCaja>(m,"SesionCaja");
        Configure<FacturaCaja>(m,"Factura",true);Configure<SecuenciaENcf>(m,"SecuenciaENcf");Configure<NotaCredito>(m,"NotaCredito");
        Configure<Outbox>(m,"Outbox");Configure<Operacion>(m,"Operacion");Configure<EventoAuditoria>(m,"EventoAuditoria");
        m.Entity<Tasa>().Property(x=>x.Codigo).HasMaxLength(40);
        m.Entity<OrdenCobro>().Property(x=>x.Numero).HasMaxLength(30);
        m.Entity<SesionCaja>().Property(x=>x.Cajero).HasMaxLength(100);m.Entity<SesionCaja>().Property(x=>x.Caja).HasMaxLength(60);m.Entity<SesionCaja>().Property(x=>x.Sucursal).HasMaxLength(100);
        m.Entity<FacturaCaja>().Property(x=>x.ENcf).HasMaxLength(13);m.Entity<FacturaCaja>().Property(x=>x.Referencia).HasMaxLength(100);m.Entity<Outbox>().Property(x=>x.Tipo).HasMaxLength(60);
        m.Entity<Tasa>().HasIndex(x=>new{x.Codigo,x.Desde}).IsUnique();
        m.Entity<OrdenCobro>().HasIndex(x=>x.Numero).IsUnique();
        m.Entity<OrdenCobro>().HasOne<Tasa>().WithMany().HasForeignKey(x=>x.TasaId).OnDelete(DeleteBehavior.NoAction);
        m.Entity<SesionCaja>().HasIndex(x=>x.Cajero).IsUnique().HasFilter("[Estado] <> 2");
        m.Entity<SesionCaja>().HasIndex(x=>new{x.Sucursal,x.Caja}).IsUnique().HasFilter("[Estado] <> 2");
        m.Entity<FacturaCaja>().Ignore(x=>x.Cambio);m.Entity<FacturaCaja>().HasIndex(x=>x.ENcf).IsUnique();
        m.Entity<FacturaCaja>().HasIndex(x=>x.OrdenCobroId).IsUnique().HasFilter("[Tipo] <> 34");
        m.Entity<FacturaCaja>().HasIndex(x=>x.Referencia).IsUnique().HasFilter("[Referencia] IS NOT NULL AND [Referencia] <> '' AND [Tipo] <> 34");
        m.Entity<FacturaCaja>().HasOne<SesionCaja>().WithMany().HasForeignKey(x=>x.SesionCajaId).OnDelete(DeleteBehavior.NoAction);
        m.Entity<FacturaCaja>().HasOne<OrdenCobro>().WithMany().HasForeignKey(x=>x.OrdenCobroId).OnDelete(DeleteBehavior.NoAction);
        m.Entity<FacturaCaja>().HasOne<FacturaCaja>().WithMany().HasForeignKey(x=>x.FacturaReferenciaId).OnDelete(DeleteBehavior.NoAction);
        m.Entity<NotaCredito>().HasIndex(x=>x.FacturaId).IsUnique();
        m.Entity<NotaCredito>().HasOne<FacturaCaja>().WithMany().HasForeignKey(x=>x.FacturaId).OnDelete(DeleteBehavior.NoAction);
        m.Entity<Operacion>().HasIndex(x=>x.Clave).IsUnique();m.Entity<Operacion>().Property(x=>x.Clave).HasMaxLength(300);
        m.Entity<EventoAuditoria>().HasIndex(x=>x.Numero).IsUnique();m.Entity<Outbox>().HasIndex(x=>new{x.Tipo,x.RecursoId}).IsUnique();
        m.Entity<FacturaCaja>().Property(x=>x.XmlFirmado).HasColumnType(Database.IsSqlServer()?"nvarchar(max)":"TEXT");
        m.Entity<SesionCaja>().Property(x=>x.DenominacionesApertura).HasMaxLength(4000);
        m.Entity<SesionCaja>().Property(x=>x.DenominacionesCierre).HasMaxLength(4000);
    }
    void Configure<T>(ModelBuilder m,string table,bool temporal=false) where T:Entity
    {
        var b=m.Entity<T>();b.HasKey(x=>x.Id);
        if(Database.IsSqlServer())
        {
            b.ToTable(table,"caja",tb=>{if(temporal)tb.IsTemporal();});b.Property(x=>x.RowVersion).IsRowVersion();
        }
        else {b.ToTable(table);b.Ignore(x=>x.RowVersion);}
        foreach(var property in typeof(T).GetProperties())
        {
            if(property.PropertyType==typeof(string))b.Property(property.Name).HasMaxLength(1000);
            if(property.PropertyType==typeof(decimal)||property.PropertyType==typeof(decimal?))b.Property(property.Name).HasPrecision(18,2);
        }
        if(typeof(T)==typeof(Tasa))b.Property("Impuesto").HasPrecision(9,6);
    }
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken=default)
    {
        if(ChangeTracker.Entries<Entity>().Any(e=>e.State==EntityState.Deleted) || ChangeTracker.Entries<EventoAuditoria>().Any(e=>e.State==EntityState.Modified))
            throw new InvalidOperationException("Los registros financieros y la auditoría no se eliminan ni se reescribe la auditoría.");
        return base.SaveChangesAsync(cancellationToken);
    }
}
public sealed class CajaDbFactory:IDesignTimeDbContextFactory<CajaDbContext>
{
    public CajaDbContext CreateDbContext(string[] args)=>new(new DbContextOptionsBuilder<CajaDbContext>().UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ConadisCajaDesign;Trusted_Connection=True;TrustServerCertificate=True").Options);
}
public sealed class EfCajaStore(CajaDbContext db):ICajaStore
{
    public IQueryable<T> Query<T>() where T:Entity => db.Set<T>();
    public void Add<T>(T entity) where T:Entity => db.Add(entity);
    public Task Save(CancellationToken ct)=>db.SaveChangesAsync(ct);
    public void Clear()=>db.ChangeTracker.Clear();
    public async Task<ITransaction> Begin(CancellationToken ct)
    {
        var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
        if(db.Database.IsSqlServer())
        {
            // Serializa escrituras del módulo y el encadenamiento de auditoría entre todas las instancias.
            await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource=N'CONADIS.Caja.Write', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @r < 0 THROW 51000, 'Caja busy', 1;",ct);
        }
        return new Tx(tx);
    }
    sealed class Tx(IDbContextTransaction tx):ITransaction
    {public Task Commit(CancellationToken ct)=>tx.CommitAsync(ct);public ValueTask DisposeAsync()=>tx.DisposeAsync();}
}
