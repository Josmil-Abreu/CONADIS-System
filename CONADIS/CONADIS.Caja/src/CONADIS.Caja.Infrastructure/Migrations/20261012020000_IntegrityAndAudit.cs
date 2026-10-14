using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CONADIS.Caja.Infrastructure.Migrations;
[DbContext(typeof(CajaDbContext))]
[Migration("20261012020000_IntegrityAndAudit")]
public sealed class IntegrityAndAudit:Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.Sql("ALTER TABLE caja.Tasa ADD CONSTRAINT CK_Tasa_Montos CHECK (Precio>0 AND Impuesto>=0 AND Impuesto<=1 AND Desde<=Hasta);");
        m.Sql("ALTER TABLE caja.OrdenCobro ADD CONSTRAINT CK_Orden_Cantidad CHECK (Cantidad BETWEEN 1 AND 1000 AND Tipo IN (31,32,45));");
        m.Sql("ALTER TABLE caja.SesionCaja ADD CONSTRAINT CK_Sesion_Estado CHECK (Estado IN (0,1,2) AND Fondo>=0);");
        m.Sql("ALTER TABLE caja.Factura ADD CONSTRAINT CK_Factura_Montos CHECK (Subtotal>=0 AND Itbis>=0 AND Total=Subtotal+Itbis AND Tipo IN (31,32,34,45) AND Medio IN (0,1,2,3) AND EstadoDgii BETWEEN 0 AND 5);");
        m.Sql("ALTER TABLE caja.SecuenciaENcf ADD CONSTRAINT CK_Secuencia_Rango CHECK (Desde>=1 AND Hasta<=9999999999 AND Desde<=Hasta AND Siguiente>=Desde AND Siguiente<=Hasta+1);");
        m.Sql("EXEC(N'CREATE TRIGGER caja.TR_Auditoria_Inmutable ON caja.EventoAuditoria INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51001, ''La auditoría solo permite inserciones.'', 1; END');");
    }
    protected override void Down(MigrationBuilder m)
    {
        m.Sql("DROP TRIGGER caja.TR_Auditoria_Inmutable;");
        foreach(var item in new[]{("Tasa","CK_Tasa_Montos"),("OrdenCobro","CK_Orden_Cantidad"),("SesionCaja","CK_Sesion_Estado"),("Factura","CK_Factura_Montos"),("SecuenciaENcf","CK_Secuencia_Rango")})
            m.Sql($"ALTER TABLE caja.{item.Item1} DROP CONSTRAINT {item.Item2};");
    }
}
