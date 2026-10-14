/* =============================================================================
   CONADIS.Caja - 98: Datos de demostración (SÍ se guardan; no hay ROLLBACK)
   Llena las pestañas de la app de Caja (comprobantes de la sesión, reportes, cierres, bitácora):

     Sesión 1  CAJA-01 / cajero    : cobros en efectivo, tarjeta y transferencia, una nota de crédito (E34),
                                      arqueo exacto y cierre del supervisor.
     Sesión 2  CAJA-02 / cajero2   : un cobro en efectivo y un arqueo con faltante de RD$10, cerrado con justificación.

   Deja los cobros "Pendientes" de sincronizar y los e-CF sin enviar a la DGII: al abrir la app de Caja (con
   Integración y el Core arriba) el sincronizador los envía solo y pasan a "Aplicado" / "Aceptado".

   Requisitos: scripts 01 al 07 de esta carpeta ya ejecutados. Se puede repetir; cada vez agrega otras dos sesiones
   (consume números de e-NCF de las secuencias). Si algún cajero tiene la caja abierta, el script no hace nada.
   ============================================================================= */
USE CONADIS_CajaDB;
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Cajero1 INT = (SELECT UsuarioId FROM seg.Usuario WHERE Username = N'cajero');
DECLARE @Cajero2 INT = (SELECT UsuarioId FROM seg.Usuario WHERE Username = N'cajero2');
DECLARE @Supervisor INT = (SELECT UsuarioId FROM seg.Usuario WHERE Username = N'supervisor.caja');
DECLARE @Caja1 INT = (SELECT CajaId FROM caja.Caja WHERE Codigo = 'CAJA-01');
DECLARE @Caja2 INT = (SELECT CajaId FROM caja.Caja WHERE Codigo = 'CAJA-02');

IF @Cajero1 IS NULL OR @Cajero2 IS NULL OR @Supervisor IS NULL OR @Caja1 IS NULL OR @Caja2 IS NULL
BEGIN
    PRINT N'Faltan usuarios o cajas: ejecute antes los scripts 01 al 07.';
    RETURN;
END;

IF EXISTS (SELECT 1 FROM caja.Sesion WHERE UsuarioId IN (@Cajero1, @Cajero2) AND Estado IN (N'Abierta', N'EnArqueo'))
BEGIN
    PRINT N'Hay una caja abierta o en arqueo para cajero o cajero2. Ciérrela desde la app (o con el supervisor) y vuelva a ejecutar.';
    RETURN;
END;

/* ------------------------------- SESIÓN 1 ---------------------------------------------------------------------- */
PRINT N'Sesión 1: CAJA-01 con cajero, fondo RD$2,000';
EXEC caja.ppSesion_Abrir @CajaId = @Caja1, @UsuarioId = @Cajero1, @FondoInicial = 2000;
DECLARE @S1 INT = (SELECT SesionId FROM caja.Sesion WHERE UsuarioId = @Cajero1 AND Estado = N'Abierta');

-- Efectivo: 2 duplicados de carnet (RD$1,000), recibe RD$1,000
EXEC caja.ppFactura_Emitir @SesionId = @S1, @UsuarioId = @Cajero1, @Tipo = '32', @DocumentoCliente = '40123456788',
     @NombreCliente = N'María Fernández', @Lineas = N'[{"tasaCodigo":"DUP-CARNET","cantidad":2}]',
     @MedioPago = N'Efectivo', @MontoRecibido = 1000;

-- Tarjeta: copia certificada (RD$300). Más adelante se anula con una nota de crédito.
EXEC caja.ppFactura_Emitir @SesionId = @S1, @UsuarioId = @Cajero1, @Tipo = '32', @DocumentoCliente = '40234567890',
     @NombreCliente = N'Juan Pérez', @Lineas = N'[{"tasaCodigo":"COPIA-CERT","cantidad":1}]',
     @MedioPago = N'Tarjeta', @Autorizacion = N'A10001';
DECLARE @Anular BIGINT = (SELECT MAX(FacturaId) FROM caja.Factura WHERE SesionId = @S1);

-- Tarjeta: constancia de registro (RD$200), esta sí queda
EXEC caja.ppFactura_Emitir @SesionId = @S1, @UsuarioId = @Cajero1, @Tipo = '32', @DocumentoCliente = '40345678901',
     @NombreCliente = N'Ana Rodríguez', @Lineas = N'[{"tasaCodigo":"CONST-REG","cantidad":1}]',
     @MedioPago = N'Tarjeta', @Autorizacion = N'A10002';

-- Transferencia: 2 constancias de registro (RD$400)
EXEC caja.ppFactura_Emitir @SesionId = @S1, @UsuarioId = @Cajero1, @Tipo = '32', @DocumentoCliente = '40456789012',
     @NombreCliente = N'Luis Martínez', @Lineas = N'[{"tasaCodigo":"CONST-REG","cantidad":2}]',
     @MedioPago = N'Transferencia', @Referencia = N'TRF-DEMO-0001';

PRINT N'Nota de crédito (E34) autorizada por el supervisor sobre el cobro de RD$300 con tarjeta';
EXEC caja.ppFactura_EmitirNotaCredito @FacturaId = @Anular, @SesionId = @S1, @SupervisorId = @Supervisor,
     @Motivo = N'Cobro duplicado por error del cliente';

-- Esperado: efectivo 2,000 + 1,000 = 3,000 · tarjeta 300 + 200 - 300 = 200 · transferencia 400
EXEC caja.ppSesion_Arquear @SesionId = @S1, @UsuarioId = @Cajero1, @EfectivoDeclarado = 3000, @TarjetaDeclarado = 200, @TransferenciaDeclarado = 400;
EXEC caja.ppSesion_Cerrar @SesionId = @S1, @SupervisorId = @Supervisor, @Observaciones = N'Cierre de demostración sin diferencias';

/* ------------------------------- SESIÓN 2 ---------------------------------------------------------------------- */
PRINT N'Sesión 2: CAJA-02 con cajero2, fondo RD$500';
EXEC caja.ppSesion_Abrir @CajaId = @Caja2, @UsuarioId = @Cajero2, @FondoInicial = 500;
DECLARE @S2 INT = (SELECT SesionId FROM caja.Sesion WHERE UsuarioId = @Cajero2 AND Estado = N'Abierta');

EXEC caja.ppFactura_Emitir @SesionId = @S2, @UsuarioId = @Cajero2, @Tipo = '32', @DocumentoCliente = '40567890123',
     @NombreCliente = N'Carmen Díaz', @Lineas = N'[{"tasaCodigo":"COPIA-CERT","cantidad":1}]',
     @MedioPago = N'Efectivo', @MontoRecibido = 500;

-- Esperado en efectivo: 500 + 300 = 800. Declara 790: faltante de RD$10 que el supervisor debe justificar.
EXEC caja.ppSesion_Arquear @SesionId = @S2, @UsuarioId = @Cajero2, @EfectivoDeclarado = 790, @TarjetaDeclarado = 0, @TransferenciaDeclarado = 0;
EXEC caja.ppSesion_Cerrar @SesionId = @S2, @SupervisorId = @Supervisor,
     @Justificacion = N'Faltante de RD$10 por cambio mal entregado (demostración)', @Observaciones = N'Cierre de demostración con diferencia';

PRINT N'Listo: 2 sesiones cerradas, 5 comprobantes E32 y 1 nota de crédito E34. Abra la app de Caja para sincronizar.';
PRINT N'Revise: SELECT * FROM caja.Factura ORDER BY FacturaId DESC;  SELECT * FROM caja.Evento ORDER BY 1 DESC;';
