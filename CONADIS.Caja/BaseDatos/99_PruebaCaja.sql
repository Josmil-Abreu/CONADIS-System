/* =============================================================================
   CONADIS.Caja - 99: Prueba del ciclo de caja SIN dejar datos (ROLLBACK al final)
   Apertura -> cobro en efectivo con cambio -> cobro con tarjeta -> nota de crédito
   -> arqueo ciego -> cierre por el supervisor -> acta de cierre.
   ============================================================================= */
USE CONADIS_CajaDB;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

DECLARE @Cajero INT = (SELECT UsuarioId FROM seg.Usuario WHERE Username = N'cajero2');
DECLARE @Supervisor INT = (SELECT UsuarioId FROM seg.Usuario WHERE Username = N'supervisor.caja');
DECLARE @Caja INT = (SELECT CajaId FROM caja.Caja WHERE Codigo = 'CAJA-02');

PRINT N'1) Apertura con fondo de RD$1,000';
EXEC caja.ppSesion_Abrir @CajaId = @Caja, @UsuarioId = @Cajero, @FondoInicial = 1000;
DECLARE @Sesion INT = (SELECT SesionId FROM caja.Sesion WHERE UsuarioId = @Cajero AND Estado = N'Abierta');

PRINT N'2) Cobro en efectivo: 2 copias certificadas (RD$600), recibe RD$1,000 -> cambio RD$400';
EXEC caja.ppFactura_Emitir @SesionId = @Sesion, @UsuarioId = @Cajero, @Tipo = '32', @DocumentoCliente = '40123456788',
     @NombreCliente = N'Cliente de Prueba', @Lineas = N'[{"tasaCodigo":"COPIA-CERT","cantidad":2}]',
     @MedioPago = N'Efectivo', @MontoRecibido = 1000;

PRINT N'3) Cobro con tarjeta: constancia de registro (RD$200)';
EXEC caja.ppFactura_Emitir @SesionId = @Sesion, @UsuarioId = @Cajero, @Tipo = '32', @DocumentoCliente = '40123456788',
     @NombreCliente = N'Cliente de Prueba', @Lineas = N'[{"tasaCodigo":"CONST-REG","cantidad":1}]',
     @MedioPago = N'Tarjeta', @Autorizacion = N'123456';

PRINT N'4) Nota de crédito (E34) que anula el cobro con tarjeta';
DECLARE @Tarjeta BIGINT = (SELECT MAX(FacturaId) FROM caja.Factura WHERE SesionId = @Sesion);
EXEC caja.ppFactura_EmitirNotaCredito @FacturaId = @Tarjeta, @SesionId = @Sesion, @SupervisorId = @Supervisor,
     @Motivo = N'Cobro duplicado por error del cliente';

PRINT N'5) Pendientes de sincronizar con Integración (solo las líneas de cobros, no la nota de crédito)';
EXEC caja.ppLinea_PendientesSincronizar;
EXEC caja.ppSincronizacion_Resumen;

PRINT N'6) Arqueo ciego: declara RD$1,600 en efectivo (esperado 1,000 + 600) y RD$0 en tarjeta (200 - 200)';
EXEC caja.ppSesion_Arquear @SesionId = @Sesion, @UsuarioId = @Cajero, @EfectivoDeclarado = 1600, @TarjetaDeclarado = 0, @TransferenciaDeclarado = 0;

PRINT N'7) Cierre aprobado por el supervisor (sin diferencias)';
EXEC caja.ppSesion_Cerrar @SesionId = @Sesion, @SupervisorId = @Supervisor, @Observaciones = N'Prueba';

PRINT N'8) Acta de cierre';
EXEC caja.ppRptCierreCaja @SesionId = @Sesion;
EXEC caja.ppSecuencia_Estado;

ROLLBACK TRANSACTION;
PRINT N'Prueba terminada. Todo se revirtió: la base quedó igual que antes.';
GO
