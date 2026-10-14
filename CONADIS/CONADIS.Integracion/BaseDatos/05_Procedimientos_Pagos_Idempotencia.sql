/* =============================================================================
   CONADIS.Integracion - 05: Idempotencia y pagos recibidos
   ============================================================================= */
USE CONADIS_IntegracionDB;
GO

-- Necesarios para índices filtrados y JSON (Visual Studio ya los trae activos; sqlcmd no)
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* ------------------------------ IDEMPOTENCIA ------------------------------- */
-- La API lo llama ANTES de procesar una petición con encabezado Idempotency-Key.
-- @Resultado:
--   Nuevo      -> procesar la petición y luego llamar a ppIdempotencia_Completar
--   Repetido   -> NO procesar: devolver @CodigoHttp y @RespuestaJson guardados
--   EnProceso  -> la primera petición aún no termina: responder 409 y que reintente
--   Conflicto  -> misma clave con otro cuerpo: error del cliente, responder 422
CREATE OR ALTER PROCEDURE snd.ppIdempotencia_Iniciar
    @ClienteId     INT,
    @Clave         NVARCHAR(100),
    @HashSolicitud CHAR(64),
    @Resultado     NVARCHAR(15)  = NULL OUTPUT,
    @CodigoHttp    INT           = NULL OUTPUT,
    @RespuestaJson NVARCHAR(MAX) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Clave IS NULL OR LTRIM(RTRIM(@Clave)) = N''
        THROW 50201, N'La clave de idempotencia es obligatoria.', 1;

    DECLARE @Ahora DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @Horas INT = cat.fnParametroEntero(N'Idempotencia.HorasVigencia', 24);
    DECLARE @Hash CHAR(64), @Estado NVARCHAR(15), @Expira DATETIME2(3);

    BEGIN TRANSACTION;

    SELECT @Hash = HashSolicitud, @Estado = Estado, @Expira = FechaExpira,
           @CodigoHttp = CodigoHttp, @RespuestaJson = RespuestaJson
    FROM snd.Idempotencia WITH (UPDLOCK, HOLDLOCK)
    WHERE ClienteId = @ClienteId AND Clave = @Clave;

    IF @Estado IS NOT NULL AND @Expira < @Ahora
    BEGIN
        -- La clave venció: se trata como nueva.
        DELETE FROM snd.Idempotencia WHERE ClienteId = @ClienteId AND Clave = @Clave;
        SELECT @Estado = NULL, @CodigoHttp = NULL, @RespuestaJson = NULL;
    END

    IF @Estado IS NULL
    BEGIN
        INSERT INTO snd.Idempotencia (ClienteId, Clave, HashSolicitud, Estado, FechaExpira)
        VALUES (@ClienteId, @Clave, UPPER(@HashSolicitud), N'EnProceso', DATEADD(HOUR, @Horas, @Ahora));
        SET @Resultado = N'Nuevo';
    END
    ELSE IF @Hash <> UPPER(@HashSolicitud)
        SELECT @Resultado = N'Conflicto', @CodigoHttp = NULL, @RespuestaJson = NULL;
    ELSE IF @Estado = N'EnProceso'
        SET @Resultado = N'EnProceso';
    ELSE
        SET @Resultado = N'Repetido';

    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE snd.ppIdempotencia_Completar
    @ClienteId     INT,
    @Clave         NVARCHAR(100),
    @CodigoHttp    INT,
    @RespuestaJson NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE snd.Idempotencia
       SET Estado = N'Completado', CodigoHttp = @CodigoHttp, RespuestaJson = @RespuestaJson
     WHERE ClienteId = @ClienteId AND Clave = @Clave AND Estado = N'EnProceso';

    IF @@ROWCOUNT = 0
        THROW 50202, N'No hay una petición en proceso con esa clave de idempotencia.', 1;
END;
GO

-- Si el procesamiento falló por un error técnico, se libera la clave para que el
-- cliente pueda reintentar con la misma.
CREATE OR ALTER PROCEDURE snd.ppIdempotencia_Liberar
    @ClienteId INT,
    @Clave     NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM snd.Idempotencia
    WHERE ClienteId = @ClienteId AND Clave = @Clave AND Estado = N'EnProceso';
END;
GO

CREATE OR ALTER PROCEDURE snd.ppIdempotencia_Purgar
    @Borrados INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM snd.Idempotencia WHERE FechaExpira < SYSUTCDATETIME();
    SET @Borrados = @@ROWCOUNT;
END;
GO

/* ----------------------------- PAGOS RECIBIDOS ----------------------------- */
-- Recibe un cobro de la Caja o un pago en línea del portal.
-- En UNA transacción: guarda el pago y encola el mensaje "Pago.Registrar" para el
-- Core (patrón outbox). Así nunca queda un pago guardado sin su mensaje, ni un
-- mensaje sin su pago, aunque el Core esté caído.
-- @Resultado: Recibido (nuevo) | Duplicado (ya había llegado: no se registra otra vez)
CREATE OR ALTER PROCEDURE snd.ppPago_Registrar
    @ClienteId         INT,
    @TransaccionId     UNIQUEIDENTIFIER,
    @Origen            NVARCHAR(10),
    @Caja              NVARCHAR(60)  = NULL,
    @Cajero            NVARCHAR(100) = NULL,
    @NumeroComprobante NVARCHAR(30),
    @TasaCodigo        NVARCHAR(40),
    @Concepto          NVARCHAR(200),
    @Cantidad          INT,
    @MontoUnitario     DECIMAL(18,2),
    @Total             DECIMAL(18,2),
    @MedioPago         NVARCHAR(20),
    @Documento         NVARCHAR(11),
    @NombrePagador     NVARCHAR(200),
    @NumeroOrden       NVARCHAR(30)  = NULL,
    @Referencia        NVARCHAR(100) = NULL,
    @FechaCobro        DATETIME2(3),
    @CorrelacionId     UNIQUEIDENTIFIER = NULL,
    @PagoId            BIGINT = NULL OUTPUT,
    @Resultado         NVARCHAR(15) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Validaciones con mensajes claros (los CHECK de la tabla son la última defensa)
    IF @TransaccionId IS NULL
        THROW 50301, N'El identificador de la transacción es obligatorio.', 1;
    IF @Origen NOT IN (N'CAJA', N'WEB')
        THROW 50302, N'El origen del pago debe ser CAJA o WEB.', 1;
    IF @Origen = N'CAJA' AND (NULLIF(@Caja, N'') IS NULL OR NULLIF(@Cajero, N'') IS NULL)
        THROW 50303, N'Un cobro de caja debe indicar la caja y el cajero.', 1;
    IF @Cantidad IS NULL OR @Cantidad NOT BETWEEN 1 AND 1000
        THROW 50304, N'La cantidad debe estar entre 1 y 1000.', 1;
    IF @MontoUnitario IS NULL OR @MontoUnitario < 0 OR @Total IS NULL OR @Total <= 0
        THROW 50305, N'Los montos del pago no son válidos.', 1;
    IF @MedioPago NOT IN (N'Efectivo', N'Tarjeta', N'Transferencia', N'EnLinea')
        THROW 50306, N'El medio de pago no es válido.', 1;
    IF @Documento IS NULL OR @Documento LIKE '%[^0-9]%' OR LEN(@Documento) NOT IN (9, 11)
        THROW 50307, N'El documento debe ser una cédula (11 dígitos) o un RNC (9 dígitos).', 1;
    IF @FechaCobro IS NULL OR @FechaCobro > DATEADD(MINUTE, 10, SYSUTCDATETIME())
        THROW 50308, N'La fecha del cobro no puede estar en el futuro.', 1;

    DECLARE @TotalPrevio DECIMAL(18,2);

    BEGIN TRANSACTION;

    SELECT @PagoId = PagoId, @TotalPrevio = Total
    FROM snd.PagoRecibido WITH (UPDLOCK, HOLDLOCK)
    WHERE TransaccionId = @TransaccionId;

    IF @PagoId IS NOT NULL
    BEGIN
        COMMIT TRANSACTION;
        IF @TotalPrevio <> @Total
            THROW 50309, N'Ya existe un pago con ese identificador de transacción y un monto distinto.', 1;
        SET @Resultado = N'Duplicado';
        RETURN;
    END

    INSERT INTO snd.PagoRecibido (TransaccionId, ClienteId, Origen, Caja, Cajero, NumeroComprobante,
                                  TasaCodigo, Concepto, Cantidad, MontoUnitario, Total, MedioPago,
                                  Documento, NombrePagador, NumeroOrden, Referencia, FechaCobro)
    VALUES (@TransaccionId, @ClienteId, @Origen, @Caja, @Cajero, @NumeroComprobante,
            @TasaCodigo, @Concepto, @Cantidad, @MontoUnitario, @Total, @MedioPago,
            @Documento, @NombrePagador, @NumeroOrden, @Referencia, @FechaCobro);
    SET @PagoId = SCOPE_IDENTITY();

    -- Cuerpo del mensaje para el Core, armado desde la fila guardada
    DECLARE @Payload NVARCHAR(MAX) =
    (
        SELECT TransaccionId, Origen, NumeroComprobante, TasaCodigo, Cantidad, Total, MedioPago,
               Documento, NombrePagador, NumeroOrden, Referencia, Caja, Cajero,
               FechaCobro AS FechaPago
        FROM snd.PagoRecibido
        WHERE PagoId = @PagoId
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
    );

    DECLARE @MensajeId BIGINT, @Clave NVARCHAR(100) = CONVERT(NVARCHAR(36), @TransaccionId);

    EXEC snd.ppMensaje_Encolar
         @ClienteId = @ClienteId,
         @ServicioCodigo = N'CORE',
         @Tipo = N'Pago.Registrar',
         @Payload = @Payload,
         @ClaveIdempotencia = @Clave,
         @CorrelacionId = @CorrelacionId,
         @MensajeId = @MensajeId OUTPUT;

    UPDATE snd.PagoRecibido SET MensajeId = @MensajeId WHERE PagoId = @PagoId;

    COMMIT TRANSACTION;
    SET @Resultado = N'Recibido';
END;
GO

-- El Servicio de Windows lo llama cuando el Core confirma o rechaza el pago.
CREATE OR ALTER PROCEDURE snd.ppPago_MarcarAplicado
    @TransaccionId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE snd.PagoRecibido
       SET Estado = N'Aplicado', FechaAplicacion = SYSUTCDATETIME()
     WHERE TransaccionId = @TransaccionId AND Estado = N'Recibido';

    IF @@ROWCOUNT = 0 AND NOT EXISTS (SELECT 1 FROM snd.PagoRecibido WHERE TransaccionId = @TransaccionId)
        THROW 50310, N'No existe un pago con ese identificador de transacción.', 1;
END;
GO

CREATE OR ALTER PROCEDURE snd.ppPago_MarcarRechazado
    @TransaccionId UNIQUEIDENTIFIER,
    @Motivo        NVARCHAR(500)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE snd.PagoRecibido
       SET Estado = N'Rechazado', Observacion = @Motivo, FechaAplicacion = SYSUTCDATETIME()
     WHERE TransaccionId = @TransaccionId AND Estado = N'Recibido';

    IF @@ROWCOUNT = 0
        THROW 50311, N'El pago no existe o ya fue procesado.', 1;
END;
GO

CREATE OR ALTER PROCEDURE snd.ppPago_ObtenerPorTransaccion
    @TransaccionId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.PagoId, p.TransaccionId, c.Codigo AS ClienteCodigo, p.Origen, p.Caja, p.Cajero,
           p.NumeroComprobante, p.TasaCodigo, p.Concepto, p.Cantidad, p.MontoUnitario, p.Total,
           p.MedioPago, p.Documento, p.NombrePagador, p.NumeroOrden, p.Referencia,
           p.FechaCobro, p.FechaRecepcion, p.Estado, p.FechaAplicacion, p.Observacion,
           m.MensajeGuid, m.Estado AS EstadoMensaje
    FROM snd.PagoRecibido p
    INNER JOIN seg.ClienteApi c ON c.ClienteId = p.ClienteId
    LEFT  JOIN snd.Mensaje m    ON m.MensajeId = p.MensajeId
    WHERE p.TransaccionId = @TransaccionId;
END;
GO

CREATE OR ALTER PROCEDURE snd.ppPago_Listar
    @Desde  DATETIME2(3),
    @Hasta  DATETIME2(3),
    @Origen NVARCHAR(10) = NULL,
    @Estado NVARCHAR(15) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.PagoId, p.TransaccionId, p.Origen, p.Caja, p.Cajero, p.NumeroComprobante,
           p.TasaCodigo, p.Concepto, p.Cantidad, p.Total, p.MedioPago, p.Documento,
           p.NombrePagador, p.FechaCobro, p.FechaRecepcion, p.Estado
    FROM snd.PagoRecibido p
    WHERE p.FechaCobro >= @Desde AND p.FechaCobro < @Hasta
      AND (@Origen IS NULL OR p.Origen = @Origen)
      AND (@Estado IS NULL OR p.Estado = @Estado)
    ORDER BY p.FechaCobro DESC;
END;
GO

PRINT N'05 - Procedimientos de idempotencia y pagos listos.';
GO
