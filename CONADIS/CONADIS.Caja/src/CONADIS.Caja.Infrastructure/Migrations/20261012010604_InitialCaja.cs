using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CONADIS.Caja.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCaja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "caja");

            migrationBuilder.CreateTable(
                name: "EventoAuditoria",
                schema: "caja",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Numero = table.Column<long>(type: "bigint", nullable: false),
                    Usuario = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Accion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Fecha = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Previo = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Hash = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventoAuditoria", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Operacion",
                schema: "caja",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Clave = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Hash = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Respuesta = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Operacion", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Outbox",
                schema: "caja",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    RecursoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Intentos = table.Column<int>(type: "int", nullable: false),
                    ProximoIntento = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Completado = table.Column<bool>(type: "bit", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecuenciaENcf",
                schema: "caja",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    Desde = table.Column<long>(type: "bigint", nullable: false),
                    Hasta = table.Column<long>(type: "bigint", nullable: false),
                    Siguiente = table.Column<long>(type: "bigint", nullable: false),
                    Vence = table.Column<DateOnly>(type: "date", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecuenciaENcf", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SesionCaja",
                schema: "caja",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Caja = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Sucursal = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Cajero = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    Fondo = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DenominacionesApertura = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    DenominacionesCierre = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    DeclaradoEfectivo = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    DeclaradoTarjeta = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    DeclaradoTransferencia = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    DeclaradoEnLinea = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    DiferenciaEfectivo = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    DiferenciaTarjeta = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    DiferenciaTransferencia = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    DiferenciaEnLinea = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Justificacion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Supervisor = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CerradaEn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Forzado = table.Column<bool>(type: "bit", nullable: false),
                    FirmaCajero = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FirmaSupervisor = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SesionCaja", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tasa",
                schema: "caja",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Codigo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Precio = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Impuesto = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    Desde = table.Column<DateOnly>(type: "date", nullable: false),
                    Hasta = table.Column<DateOnly>(type: "date", nullable: false),
                    Cuenta = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Resolucion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasa", x => x.Id);
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "TasaHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "caja")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.CreateTable(
                name: "OrdenCobro",
                schema: "caja",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Numero = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Sucursal = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    TasaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Cantidad = table.Column<int>(type: "int", nullable: false),
                    Documento = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    Tramite = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Pagada = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrdenCobro", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrdenCobro_Tasa_TasaId",
                        column: x => x.TasaId,
                        principalSchema: "caja",
                        principalTable: "Tasa",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Factura",
                schema: "caja",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SesionCajaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrdenCobroId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    ENcf = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    Documento = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Cliente = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CodigoTasa = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Concepto = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Cuenta = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Cantidad = table.Column<int>(type: "int", nullable: false),
                    Precio = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Itbis = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Medio = table.Column<int>(type: "int", nullable: false),
                    Recibido = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Referencia = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Fecha = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FacturaReferenciaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    XmlFirmado = table.Column<string>(type: "nvarchar(max)", maxLength: 1000, nullable: false),
                    CodigoSeguridad = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    QrUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    QrPng = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    TrackId = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MensajeDgii = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EstadoDgii = table.Column<int>(type: "int", nullable: false),
                    EsPrueba = table.Column<bool>(type: "bit", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Factura", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Factura_Factura_FacturaReferenciaId",
                        column: x => x.FacturaReferenciaId,
                        principalSchema: "caja",
                        principalTable: "Factura",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Factura_OrdenCobro_OrdenCobroId",
                        column: x => x.OrdenCobroId,
                        principalSchema: "caja",
                        principalTable: "OrdenCobro",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Factura_SesionCaja_SesionCajaId",
                        column: x => x.SesionCajaId,
                        principalSchema: "caja",
                        principalTable: "SesionCaja",
                        principalColumn: "Id");
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "FacturaHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "caja")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.CreateTable(
                name: "NotaCredito",
                schema: "caja",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacturaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Solicitante = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Aprobador = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ComprobanteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotaCredito", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotaCredito_Factura_FacturaId",
                        column: x => x.FacturaId,
                        principalSchema: "caja",
                        principalTable: "Factura",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventoAuditoria_Numero",
                schema: "caja",
                table: "EventoAuditoria",
                column: "Numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Factura_ENcf",
                schema: "caja",
                table: "Factura",
                column: "ENcf",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Factura_FacturaReferenciaId",
                schema: "caja",
                table: "Factura",
                column: "FacturaReferenciaId");

            migrationBuilder.CreateIndex(
                name: "IX_Factura_OrdenCobroId",
                schema: "caja",
                table: "Factura",
                column: "OrdenCobroId",
                unique: true,
                filter: "[Tipo] <> 34");

            migrationBuilder.CreateIndex(
                name: "IX_Factura_Referencia",
                schema: "caja",
                table: "Factura",
                column: "Referencia",
                unique: true,
                filter: "[Referencia] IS NOT NULL AND [Referencia] <> '' AND [Tipo] <> 34");

            migrationBuilder.CreateIndex(
                name: "IX_Factura_SesionCajaId",
                schema: "caja",
                table: "Factura",
                column: "SesionCajaId");

            migrationBuilder.CreateIndex(
                name: "IX_NotaCredito_FacturaId",
                schema: "caja",
                table: "NotaCredito",
                column: "FacturaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Operacion_Clave",
                schema: "caja",
                table: "Operacion",
                column: "Clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrdenCobro_Numero",
                schema: "caja",
                table: "OrdenCobro",
                column: "Numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrdenCobro_TasaId",
                schema: "caja",
                table: "OrdenCobro",
                column: "TasaId");

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_Tipo_RecursoId",
                schema: "caja",
                table: "Outbox",
                columns: new[] { "Tipo", "RecursoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SesionCaja_Cajero",
                schema: "caja",
                table: "SesionCaja",
                column: "Cajero",
                unique: true,
                filter: "[Estado] <> 2");

            migrationBuilder.CreateIndex(
                name: "IX_SesionCaja_Sucursal_Caja",
                schema: "caja",
                table: "SesionCaja",
                columns: new[] { "Sucursal", "Caja" },
                unique: true,
                filter: "[Estado] <> 2");

            migrationBuilder.CreateIndex(
                name: "IX_Tasa_Codigo_Desde",
                schema: "caja",
                table: "Tasa",
                columns: new[] { "Codigo", "Desde" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventoAuditoria",
                schema: "caja");

            migrationBuilder.DropTable(
                name: "NotaCredito",
                schema: "caja");

            migrationBuilder.DropTable(
                name: "Operacion",
                schema: "caja");

            migrationBuilder.DropTable(
                name: "Outbox",
                schema: "caja");

            migrationBuilder.DropTable(
                name: "SecuenciaENcf",
                schema: "caja");

            migrationBuilder.DropTable(
                name: "Factura",
                schema: "caja")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "FacturaHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "caja")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.DropTable(
                name: "OrdenCobro",
                schema: "caja");

            migrationBuilder.DropTable(
                name: "SesionCaja",
                schema: "caja");

            migrationBuilder.DropTable(
                name: "Tasa",
                schema: "caja")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "TasaHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "caja")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");
        }
    }
}
