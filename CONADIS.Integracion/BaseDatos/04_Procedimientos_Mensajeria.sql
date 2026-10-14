/* =============================================================================
   CONADIS.Integracion - 04: Mensajería tolerante a fallos
   - Cola de mensajes (outbox transaccional, RIC-07): encolar, tomar, completar,
     reintentar con espera creciente y pasar a "Fallido" al agotar los intentos.
   - Pagos recibidos de la Caja: se guardan junto con su mensaje al Core en UNA
     transacción, así ningún cobro se pierde aunque el Core esté caído.
   - Idempotencia (RIC-05): un POST repetido con la misma Idempotency-Key devuelve
     la respuesta guardada en vez de ejecutarse dos veces.
   ============================================================================= */
USE CONADIS_IntegracionDB;
GO

/* ----------------------------- COLA DE MENSAJES ---------------------------- */
-- Versión interna (devuelve el resultado por parámetros OUTPUT, sin SELECT), para poder
-- llamarla desde otros procedimientos dentro de su misma transacción.
CREATE OR ALTER PROCEDURE integ.ppMensaje_EncolarInterno
    @Tipo           VARCHAR(60),
    @Destino        VARCHAR(20),
    @Carga          NVARCHAR(MAX),
    @ClaveUnica     VARCHAR(100) = NULL,
    @CorrelacionId  VARCHAR(64)  = NULL,
    @MensajeId      BIGINT OUTPUT,
    @Encolado       BIT    OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Carga IS NULL OR ISJSON(@Carga) = 0
        THROW 56010, N'La carga del mensaje debe ser un JSON válido.', 1;

    DECLARE @Max INT = ISNULL(TRY_CAST((SELECT Valor FROM cfg.Parametro WHERE Clave = 'OutboxIntentosMaximos') AS INT), 8);
    SET @MensajeId = NULL;
    SET @Encolado = 0;

    BEGIN TRANSACTION;
        -- Si ya hay un mensaje con la misma clave, no se encola otro (el bloqueo evita carreras)
        IF @ClaveUnica IS NOT NULL
            SELECT @MensajeId = MensajeId
            FROM integ.Mensaje WITH (UPDLOCK, HOLDLOCK)
            WHERE ClaveUnica = @ClaveUnica;

        IF @MensajeId IS NULL
        BEGIN
            INSERT INTO integ.Mensaje (Tipo, Destino, Carga, ClaveUnica, MaxIntentos, CorrelacionId)
            VALUES (@Tipo, @Destino, @Carga, @ClaveUnica, @Max, @CorrelacionId);
            SET @MensajeId = SCOPE_IDENTITY();
            SET @Encolado = 1;
        END
    COMMIT TRANSACTION;
END;
GO

-- Versión pública (la usa C#): devuelve MensajeId y Encolado (0 = ya existía con esa clave).
CREATE OR ALTER PROCEDURE integ.ppMensaje_Encolar
    @Tipo           VARCHAR(60),
    @Destino        VARCHAR(20),
    @Carga          NVARCHAR(MAX),
    @ClaveUnica     VARCHAR(100) = NULL,
    @CorrelacionId  VARCHAR(64)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @MensajeId BIGINT, @Encolado BIT;

    EXEC integ.ppMensaje_EncolarInterno @Tipo = @Tipo, @Destino = @Destino, @Carga = @Carga,
         @ClaveUnica = @ClaveUnica, @CorrelacionId = @CorrelacionId,
         @MensajeId = @MensajeId OUTPUT, @Encolado = @Encolado OUTPUT;

    SELECT @MensajeId AS MensajeId, @Encolado AS Encolado;
END;
GO

-- El servicio de Windows toma un lote de mensajes listos y los marca "EnProceso" a su nombre.
-- READPAST: si dos trabajadores piden a la vez, cada uno recibe mensajes distintos.
-- Si un trabajador muere, su bloqueo vence y otro trabajador retoma el mensaje.
CREATE OR ALTER PROCEDURE integ.ppMensaje_Tomar
    @Trabajador       NVARCHAR(100),
    @Maximo           INT = 20,
    @SegundosBloqueo  INT = 120,
    @Destino          VARCHAR(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Ahora DATETIMEOFFSET(0) = SYSDATETIMEOFFSET();

    -- Mensajes abandonados que ya agotaron sus intentos: pasan a Fallido en lugar de reintentarse sin fin
    UPDATE integ.Mensaje
    SET Estado = 'Fallido', BloqueadoPor = NULL, BloqueadoHasta = NULL,
        UltimoError = N'El procesamiento se interrumpió demasiadas veces.'
    WHERE Estado = 'EnProceso' AND BloqueadoHasta < @Ahora AND Intentos >= MaxIntentos;

    ;WITH Siguientes AS
    (
        SELECT TOP (@Maximo) MensajeId, Tipo, Destino, Carga, Estado, Intentos, CorrelacionId,
                             BloqueadoPor, BloqueadoHasta, ProximoIntento
        FROM integ.Mensaje WITH (ROWLOCK, READPAST, UPDLOCK)
        WHERE ((Estado = 'Pendiente' AND ProximoIntento <= @Ahora)
            OR (Estado = 'EnProceso' AND BloqueadoHasta < @Ahora))
          AND (@Destino IS NULL OR Destino = @Destino)
        ORDER BY ProximoIntento, MensajeId
    )
    UPDATE Siguientes
    SET Estado = 'EnProceso',
        BloqueadoPor = @Trabajador,
        BloqueadoHasta = DATEADD(SECOND, @SegundosBloqueo, @Ahora),
        Intentos = Intentos + 1
    OUTPUT inserted.MensajeId, inserted.Tipo, inserted.Destino, inserted.Carga,
           inserted.Intentos, inserted.CorrelacionId;
END;
GO

CREATE OR ALTER PROCEDURE integ.ppMensaje_Completar
    @MensajeId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE integ.Mensaje
    SET Estado = 'Completado', FechaCompletado = SYSDATETIMEOFFSET(),
        BloqueadoPor = NULL, BloqueadoHasta = NULL
    WHERE MensajeId = @MensajeId AND Estado = 'EnProceso';

    IF @@ROWCOUNT = 0
        THROW 56011, N'El mensaje no existe o no está en proceso.', 1;
END;
GO

-- Fallo al entregar: se reprograma con espera creciente (30 s, 60 s, 120 s... hasta 1 hora).
-- @Permanente = 1 (ej.: el destino rechazó los datos) o intentos agotados -> Fallido + alerta.
-- @Transitorio = 1 (el destino está caído): NUNCA pasa a Fallido y la espera no supera
--   OutboxSegundosMaximoTransitorio (30 s): en cuanto el destino vuelve, el mensaje se entrega solo.
CREATE OR ALTER PROCEDURE integ.ppMensaje_RegistrarFallo
    @MensajeId   BIGINT,
    @Error       NVARCHAR(1000),
    @Permanente  BIT = 0,
    @Transitorio BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Intentos INT, @MaxIntentos INT, @Tipo VARCHAR(60), @Destino VARCHAR(20);
    DECLARE @Base INT = ISNULL(TRY_CAST((SELECT Valor FROM cfg.Parametro WHERE Clave = 'OutboxSegundosBase') AS INT), 30);
    DECLARE @Tope INT = ISNULL(TRY_CAST((SELECT Valor FROM cfg.Parametro WHERE Clave = 'OutboxSegundosMaximo') AS INT), 3600);
    IF @Transitorio = 1
        SET @Tope = ISNULL(TRY_CAST((SELECT Valor FROM cfg.Parametro WHERE Clave = 'OutboxSegundosMaximoTransitorio') AS INT), 30);

    SELECT @Intentos = Intentos, @MaxIntentos = MaxIntentos, @Tipo = Tipo, @Destino = Destino
    FROM integ.Mensaje
    WHERE MensajeId = @MensajeId AND Estado = 'EnProceso';

    IF @Intentos IS NULL
        THROW 56011, N'El mensaje no existe o no está en proceso.', 1;

    BEGIN TRANSACTION;
        IF @Permanente = 1 OR (@Intentos >= @MaxIntentos AND @Transitorio = 0)
        BEGIN
            UPDATE integ.Mensaje
            SET Estado = 'Fallido', UltimoError = LEFT(@Error, 1000), BloqueadoPor = NULL, BloqueadoHasta = NULL
            WHERE MensajeId = @MensajeId;

            DECLARE @Aviso NVARCHAR(500) = CONCAT(N'El mensaje ', @MensajeId, N' (', @Tipo, N') no se pudo entregar tras ',
                                                  @Intentos, N' intento(s): ', LEFT(@Error, 300));
            EXEC integ.ppAlerta_Registrar @Severidad = 'Alta', @Servicio = @Destino, @Mensaje = @Aviso;
        END
        ELSE
        BEGIN
            -- 2^(intentos-1) * base, sin pasar del tope (y sin desbordar el entero)
            DECLARE @Espera INT = CASE WHEN @Intentos >= 17 THEN @Tope ELSE @Base * POWER(2, @Intentos - 1) END;
            IF @Espera > @Tope SET @Espera = @Tope;

            UPDATE integ.Mensaje
            SET Estado = 'Pendiente', UltimoError = LEFT(@Error, 1000), BloqueadoPor = NULL, BloqueadoHasta = NULL,
                ProximoIntento = DATEADD(SECOND, @Espera, SYSDATETIMEOFFSET())
            WHERE MensajeId = @MensajeId;
        END
    COMMIT TRANSACTION;

    SELECT Estado, Intentos, ProximoIntento FROM integ.Mensaje WHERE MensajeId = @MensajeId;
END;
GO

-- Desde la consola: devolver a la cola un mensaje Fallido (por ejemplo, tras corregir el problema).
CREATE OR ALTER PROCEDURE integ.ppMensaje_Reintentar
    @MensajeId  BIGINT,
    @Usuario    NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE integ.Mensaje
    SET Estado = 'Pendiente', Intentos = 0, ProximoIntento = SYSDATETIMEOFFSET(),
        UltimoError = LEFT(CONCAT(N'[Reintento manual por ', @Usuario, N'] ', UltimoError), 1000)
    WHERE MensajeId = @MensajeId AND Estado = 'Fallido';

    IF @@ROWCOUNT = 0
        THROW 56012, N'Solo se pueden reintentar mensajes en estado Fallido.', 1;
END;
GO

-- Lista para la consola (sin la carga, que puede tener datos personales).
CREATE OR ALTER PROCEDURE integ.ppMensaje_Listar
    @Estado   VARCHAR(15) = NULL,
    @Destino  VARCHAR(20) = NULL,
    @Maximo   INT = 200
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Maximo) MensajeId, Tipo, Destino, Estado, Intentos, MaxIntentos, ProximoIntento,
           BloqueadoPor, UltimoError, CorrelacionId, FechaCreacion, FechaCompletado
    FROM integ.Mensaje
    WHERE (@Estado IS NULL OR Estado = @Estado)
      AND (@Destino IS NULL OR Destino = @Destino)
    ORDER BY MensajeId DESC;
END;
GO

CREATE OR ALTER PROCEDURE integ.ppMensaje_Obtener
    @MensajeId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT MensajeId, Tipo, Destino, Carga, ClaveUnica, Estado, Intentos, MaxIntentos, ProximoIntento,
           BloqueadoPor, BloqueadoHasta, UltimoError, CorrelacionId, FechaCreacion, FechaCompletado
    FROM integ.Mensaje
    WHERE MensajeId = @MensajeId;
END;
GO

-- Tablero: cuántos mensajes hay por destino y estado, y la edad del más antiguo.
CREATE OR ALTER PROCEDURE integ.ppMensaje_Resumen
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Destino, Estado, COUNT(*) AS Cantidad, MIN(FechaCreacion) AS MasAntiguo
    FROM integ.Mensaje
    WHERE Estado <> 'Completado' OR FechaCompletado > DATEADD(DAY, -1, SYSDATETIMEOFFSET())
    GROUP BY Destino, Estado
    ORDER BY Destino, Estado;
END;
GO

-- Limpieza: borra los completados antiguos (los ligados a un pago se conservan como evidencia).
CREATE OR ALTER PROCEDURE integ.ppMensaje_Purgar
    @DiasCompletados INT = 30
AS
BEGIN
    SET NOCOUNT ON;
    DELETE m
    FROM integ.Mensaje m
    WHERE m.Estado = 'Completado'
      AND m.FechaCompletado < DATEADD(DAY, -@DiasCompletados, SYSDATETIMEOFFSET())
      AND NOT EXISTS (SELECT 1 FROM integ.PagoRecibido p WHERE p.MensajeId = m.MensajeId);

    SELECT @@ROWCOUNT AS Eliminados;
END;
GO

/* ----------------------------- PAGOS RECIBIDOS ----------------------------- */
-- Outbox transaccional: el pago y su mensaje al Core se guardan juntos o no se guarda nada.
-- Mismo TransaccionId con el mismo contenido -> devuelve el pago ya recibido (EsNuevo = 0).
-- Mismo TransaccionId con OTRO contenido   -> error 56020 (posible error de la Caja).
CREATE OR ALTER PROCEDURE integ.ppPago_Recibir
    @TransaccionId      UNIQUEIDENTIFIER,
    @ClienteId          INT,
    @Origen             VARCHAR(10),
    @NumeroOrden        VARCHAR(20) = NULL,
    @NumeroComprobante  VARCHAR(20),
    @Total              DECIMAL(18,2),
    @HashCarga          CHAR(64),
    @Carga              NVARCHAR(MAX),
    @CorrelacionId      VARCHAR(64) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @HashExistente CHAR(64), @MensajeId BIGINT, @Encolado BIT, @EsNuevo BIT = 0;
    DECLARE @Clave VARCHAR(100) = CONCAT('PAGO:', @TransaccionId);

    BEGIN TRANSACTION;
        SELECT @HashExistente = HashCarga
        FROM integ.PagoRecibido WITH (UPDLOCK, HOLDLOCK)
        WHERE TransaccionId = @TransaccionId;

        IF @HashExistente IS NOT NULL AND @HashExistente <> UPPER(@HashCarga)
            THROW 56020, N'Ya se recibió un pago con ese identificador de transacción pero con datos distintos. Genere un identificador nuevo.', 1;

        IF @HashExistente IS NULL
        BEGIN
            EXEC integ.ppMensaje_EncolarInterno @Tipo = 'Core.RegistrarPago', @Destino = 'Core', @Carga = @Carga,
                 @ClaveUnica = @Clave, @CorrelacionId = @CorrelacionId,
                 @MensajeId = @MensajeId OUTPUT, @Encolado = @Encolado OUTPUT;

            INSERT INTO integ.PagoRecibido (TransaccionId, ClienteId, Origen, NumeroOrden, NumeroComprobante, Total, HashCarga, MensajeId)
            VALUES (@TransaccionId, @ClienteId, @Origen, @NumeroOrden, @NumeroComprobante, @Total, UPPER(@HashCarga), @MensajeId);

            SET @EsNuevo = 1;
        END
    COMMIT TRANSACTION;

    SELECT TransaccionId, Origen, NumeroOrden, NumeroComprobante, Total, Estado, Detalle,
           FechaRecepcion, FechaConfirmacion, @EsNuevo AS EsNuevo
    FROM integ.PagoRecibido
    WHERE TransaccionId = @TransaccionId;
END;
GO

-- Cada cliente solo ve sus propios pagos.
CREATE OR ALTER PROCEDURE integ.ppPago_Obtener
    @TransaccionId  UNIQUEIDENTIFIER,
    @ClienteId      INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TransaccionId, Origen, NumeroOrden, NumeroComprobante, Total, Estado, Detalle,
           FechaRecepcion, FechaConfirmacion
    FROM integ.PagoRecibido
    WHERE TransaccionId = @TransaccionId AND ClienteId = @ClienteId;
END;
GO

-- Lo que respondió el Core al entregarle el pago. Solo cambia pagos aún "Recibido" (es idempotente).
CREATE OR ALTER PROCEDURE integ.ppPago_RegistrarRespuestaCore
    @TransaccionId  UNIQUEIDENTIFIER,
    @Estado         VARCHAR(15),
    @Detalle        NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Estado NOT IN ('Aplicado', 'Duplicado', 'Rechazado')
        THROW 56021, N'Estado de respuesta del Core no válido.', 1;

    UPDATE integ.PagoRecibido
    SET Estado = @Estado, Detalle = @Detalle, FechaConfirmacion = SYSDATETIMEOFFSET()
    WHERE TransaccionId = @TransaccionId AND Estado = 'Recibido';
END;
GO

-- Conciliación: pagos de un periodo (de un cliente o de todos).
CREATE OR ALTER PROCEDURE integ.ppPago_Listar
    @Desde      DATETIMEOFFSET(0),
    @Hasta      DATETIMEOFFSET(0),
    @ClienteId  INT = NULL,
    @Estado     VARCHAR(15) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.TransaccionId, c.Nombre AS Cliente, p.Origen, p.NumeroOrden, p.NumeroComprobante, p.Total,
           p.Estado, p.Detalle, p.FechaRecepcion, p.FechaConfirmacion, m.Estado AS EstadoMensaje, m.Intentos
    FROM integ.PagoRecibido p
    INNER JOIN seg.ClienteApi c ON c.ClienteId = p.ClienteId
    INNER JOIN integ.Mensaje m ON m.MensajeId = p.MensajeId
    WHERE p.FechaRecepcion >= @Desde AND p.FechaRecepcion < @Hasta
      AND (@ClienteId IS NULL OR p.ClienteId = @ClienteId)
      AND (@Estado IS NULL OR p.Estado = @Estado)
    ORDER BY p.FechaRecepcion DESC;
END;
GO

/* ----------------------------- IDEMPOTENCIA -------------------------------- */
-- Resultado:
--   Nueva     -> primera vez: procesar la petición y luego llamar a ppIdempotencia_Completar
--   Repetida  -> ya se procesó: devolver CodigoHttp y Respuesta guardados, sin ejecutar nada
--   EnProceso -> la primera petición aún se está procesando: responder 409
--   Conflicto -> misma clave con otro contenido u otra ruta: responder 422
CREATE OR ALTER PROCEDURE integ.ppIdempotencia_Iniciar
    @ClienteId     INT,
    @Clave         VARCHAR(100),
    @Ruta          NVARCHAR(200),
    @HashPeticion  CHAR(64),
    @Horas         INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Vigencia INT = COALESCE(@Horas,
        TRY_CAST((SELECT Valor FROM cfg.Parametro WHERE Clave = 'IdempotenciaHorasVigencia') AS INT), 24);
    DECLARE @Ahora DATETIMEOFFSET(0) = SYSDATETIMEOFFSET();
    DECLARE @Estado VARCHAR(15), @HashGuardado CHAR(64), @RutaGuardada NVARCHAR(200),
            @VenceEn DATETIMEOFFSET(0), @Creada DATETIMEOFFSET(0), @Codigo INT, @Respuesta NVARCHAR(MAX),
            @Resultado VARCHAR(15);

    BEGIN TRANSACTION;
        SELECT @Estado = Estado, @HashGuardado = HashPeticion, @RutaGuardada = Ruta, @VenceEn = VenceEn,
               @Creada = FechaCreacion, @Codigo = CodigoHttp, @Respuesta = Respuesta
        FROM integ.Idempotencia WITH (UPDLOCK, HOLDLOCK)
        WHERE ClienteId = @ClienteId AND Clave = @Clave;

        -- Vencida, o "EnProceso" abandonada (más de 5 minutos): se trata como nueva
        IF @Estado IS NOT NULL AND (@VenceEn <= @Ahora OR (@Estado = 'EnProceso' AND @Creada < DATEADD(MINUTE, -5, @Ahora)))
        BEGIN
            DELETE FROM integ.Idempotencia WHERE ClienteId = @ClienteId AND Clave = @Clave;
            SET @Estado = NULL;
        END

        IF @Estado IS NULL
        BEGIN
            INSERT INTO integ.Idempotencia (ClienteId, Clave, Ruta, HashPeticion, Estado, VenceEn)
            VALUES (@ClienteId, @Clave, @Ruta, UPPER(@HashPeticion), 'EnProceso', DATEADD(HOUR, @Vigencia, @Ahora));
            SET @Resultado = 'Nueva';
        END
        ELSE IF @HashGuardado <> UPPER(@HashPeticion) OR @RutaGuardada <> @Ruta
            SET @Resultado = 'Conflicto';
        ELSE IF @Estado = 'EnProceso'
            SET @Resultado = 'EnProceso';
        ELSE
            SET @Resultado = 'Repetida';
    COMMIT TRANSACTION;

    SELECT @Resultado AS Resultado,
           CASE WHEN @Resultado = 'Repetida' THEN @Codigo END AS CodigoHttp,
           CASE WHEN @Resultado = 'Repetida' THEN @Respuesta END AS Respuesta;
END;
GO

CREATE OR ALTER PROCEDURE integ.ppIdempotencia_Completar
    @ClienteId   INT,
    @Clave       VARCHAR(100),
    @CodigoHttp  INT,
    @Respuesta   NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE integ.Idempotencia
    SET Estado = 'Completada', CodigoHttp = @CodigoHttp, Respuesta = @Respuesta
    WHERE ClienteId = @ClienteId AND Clave = @Clave AND Estado = 'EnProceso';
END;
GO

-- Si la petición falló por un error del servidor, se libera la clave para que el cliente pueda reintentar.
CREATE OR ALTER PROCEDURE integ.ppIdempotencia_Liberar
    @ClienteId  INT,
    @Clave      VARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM integ.Idempotencia
    WHERE ClienteId = @ClienteId AND Clave = @Clave AND Estado = 'EnProceso';
END;
GO

CREATE OR ALTER PROCEDURE integ.ppIdempotencia_Purgar
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM integ.Idempotencia WHERE VenceEn <= SYSDATETIMEOFFSET();
    SELECT @@ROWCOUNT AS Eliminados;
END;
GO

PRINT N'04 - Procedimientos de mensajería creados.';
GO
