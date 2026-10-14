/* =============================================================================
   CONADIS.Integracion - 99: Prueba de punta a punta
   Ejecutar después de 01..09. Todo ocurre dentro de transacciones que al final
   se revierten: NO deja datos en la base.
   Si algo falla, el script se detiene con un mensaje "FALLO: ...".
   Si todo sale bien termina con "Prueba terminada. Todo se revirtió".

   Recorre:
     1. Validación de API key (válida, inválida).
     2. Cobro de caja -> pago guardado + mensaje en la cola (outbox), en una transacción.
     3. El mismo cobro reenviado -> Duplicado, sin segundo pago ni segundo mensaje.
     4. Idempotencia: Nuevo, EnProceso, Repetido y Conflicto.
     5. Trabajador: toma el lote, falla (reintento con espera), vuelve a tomar y entrega.
     6. Mensaje que agota sus intentos -> Muerto -> se reintenta a mano.
     7. Cortacircuito: Cerrado -> Abierto -> SemiAbierto -> Cerrado.
     8. Caché: vigente, vencida (solo con @PermitirVencido = 1).
     9. Bitácora y los 6 reportes.
    10. Errores esperados: pago con monto distinto (50309) y bitácora inmutable (50090).
   ============================================================================= */
USE CONADIS_IntegracionDB;
GO

-- Necesarios para índices filtrados y JSON (Visual Studio ya los trae activos; sqlcmd no)
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

SET NOCOUNT ON;
SET XACT_ABORT OFF;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @Ahora DATETIME2(3) = SYSUTCDATETIME();

    /* ---------------------------------------------------------------- 1 --- */
    DECLARE @Cliente TABLE (ClienteId INT, Codigo NVARCHAR(20), Nombre NVARCHAR(100));
    DECLARE @HashCaja CHAR(64) = CONVERT(CHAR(64), HASHBYTES('SHA2_256', CAST('demo-caja-conadis-2026' AS VARCHAR(100))), 2);

    INSERT INTO @Cliente EXEC seg.ppCliente_ValidarApiKey @ApiKeyHash = @HashCaja;
    DECLARE @ClienteCaja INT = (SELECT ClienteId FROM @Cliente WHERE Codigo = N'CAJA');
    IF @ClienteCaja IS NULL THROW 59999, N'FALLO: la API key de CAJA no se validó. ¿Se ejecutó 08_DatosIniciales.sql?', 1;

    DELETE FROM @Cliente;
    INSERT INTO @Cliente EXEC seg.ppCliente_ValidarApiKey @ApiKeyHash = '0000000000000000000000000000000000000000000000000000000000000000';
    IF EXISTS (SELECT 1 FROM @Cliente) THROW 59999, N'FALLO: una API key inválida fue aceptada.', 1;
    PRINT N'OK 1 - API key válida aceptada e inválida rechazada.';

    /* ---------------------------------------------------------------- 2 --- */
    DECLARE @Tx UNIQUEIDENTIFIER = NEWID(), @PagoId BIGINT, @Resultado NVARCHAR(15);

    EXEC snd.ppPago_Registrar
         @ClienteId = @ClienteCaja, @TransaccionId = @Tx, @Origen = N'CAJA',
         @Caja = N'Caja 01 - Sede Central', @Cajero = N'cajero.prueba',
         @NumeroComprobante = N'E320000000001', @TasaCodigo = N'DUP-CARNET',
         @Concepto = N'Duplicado de carnet', @Cantidad = 1, @MontoUnitario = 500.00, @Total = 500.00,
         @MedioPago = N'Efectivo', @Documento = N'00112345678', @NombrePagador = N'Persona de Prueba',
         @FechaCobro = @Ahora, @PagoId = @PagoId OUTPUT, @Resultado = @Resultado OUTPUT;

    IF @Resultado <> N'Recibido' THROW 59999, N'FALLO: el primer cobro no quedó como Recibido.', 1;

    DECLARE @MensajePago BIGINT = (SELECT MensajeId FROM snd.PagoRecibido WHERE PagoId = @PagoId);
    IF @MensajePago IS NULL
       OR NOT EXISTS (SELECT 1 FROM snd.Mensaje m INNER JOIN snd.Servicio s ON s.ServicioId = m.ServicioDestinoId
                      WHERE m.MensajeId = @MensajePago AND m.Estado = N'Pendiente'
                        AND m.Tipo = N'Pago.Registrar' AND s.Codigo = N'CORE'
                        AND JSON_VALUE(m.Payload, '$.Total') = '500.00')
        THROW 59999, N'FALLO: el cobro no encoló su mensaje Pago.Registrar para el Core.', 1;
    PRINT N'OK 2 - Cobro guardado y mensaje para el Core encolado en la misma transacción.';

    /* ---------------------------------------------------------------- 3 --- */
    DECLARE @PagoId2 BIGINT;
    EXEC snd.ppPago_Registrar
         @ClienteId = @ClienteCaja, @TransaccionId = @Tx, @Origen = N'CAJA',
         @Caja = N'Caja 01 - Sede Central', @Cajero = N'cajero.prueba',
         @NumeroComprobante = N'E320000000001', @TasaCodigo = N'DUP-CARNET',
         @Concepto = N'Duplicado de carnet', @Cantidad = 1, @MontoUnitario = 500.00, @Total = 500.00,
         @MedioPago = N'Efectivo', @Documento = N'00112345678', @NombrePagador = N'Persona de Prueba',
         @FechaCobro = @Ahora, @PagoId = @PagoId2 OUTPUT, @Resultado = @Resultado OUTPUT;

    IF @Resultado <> N'Duplicado' OR @PagoId2 <> @PagoId
        THROW 59999, N'FALLO: el cobro reenviado no se detectó como duplicado.', 1;
    IF (SELECT COUNT(*) FROM snd.PagoRecibido WHERE TransaccionId = @Tx) <> 1
       OR (SELECT COUNT(*) FROM snd.Mensaje WHERE ClaveIdempotencia = CONVERT(NVARCHAR(36), @Tx)) <> 1
        THROW 59999, N'FALLO: el cobro reenviado creó un segundo pago o un segundo mensaje.', 1;
    PRINT N'OK 3 - Cobro reenviado: Duplicado, sin repetir pago ni mensaje.';

    /* ---------------------------------------------------------------- 4 --- */
    DECLARE @Clave NVARCHAR(100) = CONVERT(NVARCHAR(36), NEWID());
    DECLARE @HashA CHAR(64) = REPLICATE('A', 64), @HashB CHAR(64) = REPLICATE('B', 64);
    DECLARE @Idem NVARCHAR(15), @Http INT, @Resp NVARCHAR(MAX);

    EXEC snd.ppIdempotencia_Iniciar @ClienteCaja, @Clave, @HashA, @Idem OUTPUT, @Http OUTPUT, @Resp OUTPUT;
    IF @Idem <> N'Nuevo' THROW 59999, N'FALLO: idempotencia, la primera petición no es Nuevo.', 1;

    EXEC snd.ppIdempotencia_Iniciar @ClienteCaja, @Clave, @HashA, @Idem OUTPUT, @Http OUTPUT, @Resp OUTPUT;
    IF @Idem <> N'EnProceso' THROW 59999, N'FALLO: idempotencia, la petición simultánea no es EnProceso.', 1;

    EXEC snd.ppIdempotencia_Completar @ClienteCaja, @Clave, 201, N'{"numero":"OC-2026-0000001"}';

    EXEC snd.ppIdempotencia_Iniciar @ClienteCaja, @Clave, @HashA, @Idem OUTPUT, @Http OUTPUT, @Resp OUTPUT;
    IF @Idem <> N'Repetido' OR @Http <> 201 OR JSON_VALUE(@Resp, '$.numero') <> N'OC-2026-0000001'
        THROW 59999, N'FALLO: idempotencia, la petición repetida no devolvió la respuesta guardada.', 1;

    EXEC snd.ppIdempotencia_Iniciar @ClienteCaja, @Clave, @HashB, @Idem OUTPUT, @Http OUTPUT, @Resp OUTPUT;
    IF @Idem <> N'Conflicto' THROW 59999, N'FALLO: idempotencia, misma clave con otro cuerpo no dio Conflicto.', 1;
    PRINT N'OK 4 - Idempotencia: Nuevo, EnProceso, Repetido y Conflicto.';

    /* ---------------------------------------------------------------- 5 --- */
    DECLARE @Lote TABLE (MensajeId BIGINT, MensajeGuid UNIQUEIDENTIFIER, ServicioCodigo NVARCHAR(20),
                         Tipo NVARCHAR(60), Payload NVARCHAR(MAX), Intentos INT, MaxIntentos INT,
                         ClaveIdempotencia NVARCHAR(100), CorrelacionId UNIQUEIDENTIFIER, FechaCreacion DATETIME2(3));

    INSERT INTO @Lote EXEC snd.ppMensaje_TomarLote @Trabajador = N'prueba-99', @Tamano = 500;
    IF NOT EXISTS (SELECT 1 FROM @Lote WHERE MensajeId = @MensajePago)
        THROW 59999, N'FALLO: el trabajador no tomó el mensaje del pago.', 1;
    IF (SELECT Estado FROM snd.Mensaje WHERE MensajeId = @MensajePago) <> N'EnProceso'
        THROW 59999, N'FALLO: el mensaje tomado no quedó EnProceso.', 1;

    -- Mientras está EnProceso, otro trabajador no lo puede tomar
    DELETE FROM @Lote;
    INSERT INTO @Lote EXEC snd.ppMensaje_TomarLote @Trabajador = N'prueba-99-b', @Tamano = 500;
    IF EXISTS (SELECT 1 FROM @Lote WHERE MensajeId = @MensajePago)
        THROW 59999, N'FALLO: dos trabajadores tomaron el mismo mensaje.', 1;

    -- Falla la entrega: vuelve a Pendiente con espera
    DECLARE @EstadoMsg NVARCHAR(15);
    EXEC snd.ppMensaje_MarcarFallido @MensajePago, N'Core no disponible (prueba)', @EstadoMsg OUTPUT;
    IF @EstadoMsg <> N'Pendiente'
       OR (SELECT ProximoIntento FROM snd.Mensaje WHERE MensajeId = @MensajePago) <= SYSUTCDATETIME()
        THROW 59999, N'FALLO: el mensaje fallido no se reprogramó para más tarde.', 1;

    -- Antes de su hora no se vuelve a tomar
    DELETE FROM @Lote;
    INSERT INTO @Lote EXEC snd.ppMensaje_TomarLote @Trabajador = N'prueba-99', @Tamano = 500;
    IF EXISTS (SELECT 1 FROM @Lote WHERE MensajeId = @MensajePago)
        THROW 59999, N'FALLO: el mensaje se tomó antes de su hora de reintento.', 1;

    -- Simula que pasó el tiempo de espera, se toma y se entrega
    UPDATE snd.Mensaje SET ProximoIntento = DATEADD(SECOND, -1, SYSUTCDATETIME()) WHERE MensajeId = @MensajePago;
    DELETE FROM @Lote;
    INSERT INTO @Lote EXEC snd.ppMensaje_TomarLote @Trabajador = N'prueba-99', @Tamano = 500;
    IF NOT EXISTS (SELECT 1 FROM @Lote WHERE MensajeId = @MensajePago AND Intentos = 1)
        THROW 59999, N'FALLO: el mensaje no se volvió a tomar después de la espera.', 1;

    EXEC snd.ppMensaje_MarcarEntregado @MensajePago, N'{"estado":"Aplicado"}';
    EXEC snd.ppPago_MarcarAplicado @Tx;
    IF (SELECT Estado FROM snd.Mensaje WHERE MensajeId = @MensajePago) <> N'Entregado'
       OR (SELECT Estado FROM snd.PagoRecibido WHERE PagoId = @PagoId) <> N'Aplicado'
        THROW 59999, N'FALLO: el mensaje no quedó Entregado o el pago no quedó Aplicado.', 1;
    PRINT N'OK 5 - Trabajador: tomar, fallar, esperar, reintentar y entregar.';

    /* ---------------------------------------------------------------- 6 --- */
    DECLARE @MsgMuerto BIGINT;
    EXEC snd.ppMensaje_Encolar @ClienteId = @ClienteCaja, @ServicioCodigo = N'DGII',
         @Tipo = N'Ecf.Enviar', @Payload = N'{"ncf":"E310000000099"}', @MensajeId = @MsgMuerto OUTPUT;
    UPDATE snd.Mensaje SET MaxIntentos = 2 WHERE MensajeId = @MsgMuerto;

    DECLARE @i INT = 1;
    WHILE @i <= 2
    BEGIN
        UPDATE snd.Mensaje SET ProximoIntento = DATEADD(SECOND, -1, SYSUTCDATETIME()) WHERE MensajeId = @MsgMuerto;
        DELETE FROM @Lote;
        INSERT INTO @Lote EXEC snd.ppMensaje_TomarLote @Trabajador = N'prueba-99', @Tamano = 500;
        EXEC snd.ppMensaje_MarcarFallido @MsgMuerto, N'DGII rechazó (prueba)', @EstadoMsg OUTPUT;
        SET @i += 1;
    END
    IF @EstadoMsg <> N'Muerto' THROW 59999, N'FALLO: el mensaje que agotó sus intentos no quedó Muerto.', 1;

    EXEC snd.ppMensaje_Reintentar @MsgMuerto;
    IF (SELECT Estado FROM snd.Mensaje WHERE MensajeId = @MsgMuerto) <> N'Pendiente'
        THROW 59999, N'FALLO: el mensaje muerto no volvió a la cola al reintentarlo.', 1;
    PRINT N'OK 6 - Mensaje Muerto tras agotar intentos y reintento manual.';

    /* ---------------------------------------------------------------- 7 --- */
    DECLARE @Permitido BIT, @EstadoCircuito NVARCHAR(15);
    EXEC snd.ppServicio_RegistrarExito N'JCE';

    SET @i = 1;
    WHILE @i <= (SELECT UmbralFallos FROM snd.Servicio WHERE Codigo = N'JCE')
    BEGIN
        EXEC snd.ppServicio_RegistrarFallo N'JCE', @EstadoCircuito OUTPUT;
        SET @i += 1;
    END
    EXEC snd.ppServicio_PuedeLlamar N'JCE', @Permitido OUTPUT, @EstadoCircuito OUTPUT;
    IF @EstadoCircuito <> N'Abierto' OR @Permitido <> 0
        THROW 59999, N'FALLO: el cortacircuito no se abrió al llegar al umbral de fallos.', 1;

    UPDATE snd.Servicio SET AbiertoHasta = DATEADD(SECOND, -1, SYSUTCDATETIME()) WHERE Codigo = N'JCE';
    EXEC snd.ppServicio_PuedeLlamar N'JCE', @Permitido OUTPUT, @EstadoCircuito OUTPUT;
    IF @EstadoCircuito <> N'SemiAbierto' OR @Permitido <> 1
        THROW 59999, N'FALLO: el cortacircuito no pasó a SemiAbierto al terminar la espera.', 1;

    EXEC snd.ppServicio_RegistrarFallo N'JCE', @EstadoCircuito OUTPUT;
    IF @EstadoCircuito <> N'Abierto'
        THROW 59999, N'FALLO: un fallo en SemiAbierto no volvió a abrir el circuito.', 1;

    UPDATE snd.Servicio SET AbiertoHasta = DATEADD(SECOND, -1, SYSUTCDATETIME()) WHERE Codigo = N'JCE';
    EXEC snd.ppServicio_PuedeLlamar N'JCE', @Permitido OUTPUT, @EstadoCircuito OUTPUT;
    EXEC snd.ppServicio_RegistrarExito N'JCE';
    IF (SELECT EstadoCircuito FROM snd.Servicio WHERE Codigo = N'JCE') <> N'Cerrado'
        THROW 59999, N'FALLO: un éxito en SemiAbierto no cerró el circuito.', 1;
    PRINT N'OK 7 - Cortacircuito: Cerrado, Abierto, SemiAbierto y Cerrado.';

    /* ---------------------------------------------------------------- 8 --- */
    DECLARE @Cache TABLE (Clave NVARCHAR(200), ServicioCodigo NVARCHAR(20), ValorJson NVARCHAR(MAX),
                          FechaActualizacion DATETIME2(3), FechaExpira DATETIME2(3), Vencido BIT);

    EXEC snd.ppCache_Guardar N'jce:00112345678', N'JCE', N'{"nombres":"Persona","valida":true}', 60;
    INSERT INTO @Cache EXEC snd.ppCache_Obtener N'jce:00112345678';
    IF NOT EXISTS (SELECT 1 FROM @Cache WHERE Vencido = 0 AND JSON_VALUE(ValorJson, '$.nombres') = N'Persona')
        THROW 59999, N'FALLO: la caché no devolvió el valor vigente.', 1;

    UPDATE snd.Cache SET FechaActualizacion = DATEADD(HOUR, -2, SYSUTCDATETIME()),
                         FechaExpira = DATEADD(HOUR, -1, SYSUTCDATETIME())
    WHERE Clave = N'jce:00112345678';

    DELETE FROM @Cache;
    INSERT INTO @Cache EXEC snd.ppCache_Obtener N'jce:00112345678', 0;
    IF EXISTS (SELECT 1 FROM @Cache) THROW 59999, N'FALLO: la caché devolvió un valor vencido sin pedirlo.', 1;

    INSERT INTO @Cache EXEC snd.ppCache_Obtener N'jce:00112345678', 1;
    IF NOT EXISTS (SELECT 1 FROM @Cache WHERE Vencido = 1)
        THROW 59999, N'FALLO: la caché no devolvió el valor vencido con @PermitirVencido = 1.', 1;
    PRINT N'OK 8 - Caché: vigente, vencida oculta y vencida permitida.';

    /* ---------------------------------------------------------------- 9 --- */
    DECLARE @Corr UNIQUEIDENTIFIER = NEWID();
    EXEC snd.ppBitacora_Registrar @Corr, @ClienteCaja, NULL,   N'Entrante', N'POST /api/v1/pagos/caja', 201, 1, 35;
    EXEC snd.ppBitacora_Registrar @Corr, NULL,        N'CORE', N'Saliente', N'POST /api/v1/pagos',      503, 0, 5000, N'Core caído';
    EXEC snd.ppBitacora_Registrar @Corr, NULL,        N'CORE', N'Saliente', N'POST /api/v1/pagos',      201, 1, 80;

    IF (SELECT COUNT(*) FROM snd.Bitacora WHERE CorrelacionId = @Corr) <> 3
        THROW 59999, N'FALLO: la bitácora no registró las 3 llamadas.', 1;

    DECLARE @Desde DATETIME2(3) = DATEADD(DAY, -1, @Ahora), @Hasta DATETIME2(3) = DATEADD(DAY, 1, SYSUTCDATETIME());

    DECLARE @RptDisp TABLE (Servicio NVARCHAR(20), Nombre NVARCHAR(100), Llamadas INT, Exitosas INT, Fallidas INT,
                            PorcentajeDisponibilidad DECIMAL(5,2), PromedioMs DECIMAL(18,2), MaximoMs INT, EstadoActual NVARCHAR(15));
    INSERT INTO @RptDisp EXEC snd.ppRpt_Disponibilidad @Desde, @Hasta;
    IF NOT EXISTS (SELECT 1 FROM @RptDisp WHERE Servicio = N'CORE' AND Llamadas >= 2 AND Fallidas >= 1)
        THROW 59999, N'FALLO: el reporte de disponibilidad no refleja las llamadas al Core.', 1;

    DECLARE @RptPagos TABLE (Fecha DATE, Origen NVARCHAR(10), MedioPago NVARCHAR(20), Estado NVARCHAR(15), Cantidad INT, Total DECIMAL(18,2));
    INSERT INTO @RptPagos EXEC snd.ppRpt_PagosRecibidos @Desde, @Hasta, N'CAJA';
    IF NOT EXISTS (SELECT 1 FROM @RptPagos WHERE MedioPago = N'Efectivo' AND Estado = N'Aplicado' AND Total >= 500)
        THROW 59999, N'FALLO: el reporte de pagos no incluye el cobro de prueba.', 1;

    -- Los otros reportes solo deben ejecutarse sin error
    DECLARE @Rpt1 TABLE (Servicio NVARCHAR(20), Tipo NVARCHAR(60), Estado NVARCHAR(15), Cantidad INT,
                         PromedioIntentos DECIMAL(10,2), PromedioSegundosEntrega DECIMAL(18,2));
    INSERT INTO @Rpt1 EXEC snd.ppRpt_MensajesPorEstado @Desde, @Hasta;

    DECLARE @Rpt2 TABLE (MensajeId BIGINT, MensajeGuid UNIQUEIDENTIFIER, Servicio NVARCHAR(20), Cliente NVARCHAR(20),
                         Tipo NVARCHAR(60), Intentos INT, FechaCreacion DATETIME2(3), UltimoError NVARCHAR(MAX));
    INSERT INTO @Rpt2 EXEC snd.ppRpt_MensajesMuertos;

    DECLARE @Rpt3 TABLE (Servicio NVARCHAR(20), Direccion NVARCHAR(10), Operacion NVARCHAR(100), CodigoResultado INT,
                         Cantidad INT, Primero DATETIME2(3), Ultimo DATETIME2(3));
    INSERT INTO @Rpt3 EXEC snd.ppRpt_ErroresPorServicio @Desde, @Hasta;
    IF NOT EXISTS (SELECT 1 FROM @Rpt3 WHERE Servicio = N'CORE' AND CodigoResultado = 503)
        THROW 59999, N'FALLO: el reporte de errores no incluye el 503 del Core.', 1;

    DECLARE @Rpt4 TABLE (Cliente NVARCHAR(20), Nombre NVARCHAR(100), Peticiones INT, Errores INT, PromedioMs DECIMAL(18,2));
    INSERT INTO @Rpt4 EXEC snd.ppRpt_UsoPorCliente @Desde, @Hasta;
    IF NOT EXISTS (SELECT 1 FROM @Rpt4 WHERE Cliente = N'CAJA' AND Peticiones >= 1)
        THROW 59999, N'FALLO: el reporte de uso por cliente no incluye la petición de la Caja.', 1;
    PRINT N'OK 9 - Bitácora y reportes.';

    /* --------------------------------------------------------------- 10 --- */
    -- Error esperado: el mismo TransaccionId con otro monto. Va al final porque
    -- un THROW dentro de un SP con XACT_ABORT deja la transacción sin poder confirmarse.
    BEGIN TRY
        EXEC snd.ppPago_Registrar
             @ClienteId = @ClienteCaja, @TransaccionId = @Tx, @Origen = N'CAJA',
             @Caja = N'Caja 01 - Sede Central', @Cajero = N'cajero.prueba',
             @NumeroComprobante = N'E320000000001', @TasaCodigo = N'DUP-CARNET',
             @Concepto = N'Duplicado de carnet', @Cantidad = 2, @MontoUnitario = 500.00, @Total = 1000.00,
             @MedioPago = N'Efectivo', @Documento = N'00112345678', @NombrePagador = N'Persona de Prueba',
             @FechaCobro = @Ahora;
        THROW 59998, N'FALLO: un cobro con el mismo TransaccionId y otro monto fue aceptado.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 50309 THROW;
        PRINT N'OK 10a - Mismo TransaccionId con otro monto rechazado (50309).';
    END CATCH

    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

-- La bitácora no se puede modificar (en su propia transacción, por la misma razón)
BEGIN TRY
    BEGIN TRANSACTION;
    BEGIN TRY
        UPDATE snd.Bitacora SET Operacion = N'alterado' WHERE 1 = 0;
        THROW 59998, N'FALLO: la bitácora permitió un UPDATE.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 50090 THROW;
        PRINT N'OK 10b - La bitácora rechaza UPDATE/DELETE (50090).';
    END CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

PRINT N'Prueba terminada. Todo se revirtió: la base quedó como estaba.';
GO
