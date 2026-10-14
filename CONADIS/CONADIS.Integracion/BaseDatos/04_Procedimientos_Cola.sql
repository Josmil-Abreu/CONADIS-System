/* =============================================================================
   CONADIS.Integracion - 04: Cola de mensajes (outbox)
   Ciclo de vida de un mensaje:
     Pendiente --(TomarLote)--> EnProceso --(MarcarEntregado)--> Entregado
                                    |
                                    +--(MarcarFallido)--> Pendiente (reintento con espera exponencial)
                                                     \--> Muerto    (agotó los intentos; se revisa a mano)
   Si un trabajador se cae con un mensaje EnProceso, al vencer BloqueadoHasta
   el mensaje vuelve a estar disponible para otro trabajador.
   ============================================================================= */
USE CONADIS_IntegracionDB;
GO

-- Necesarios para índices filtrados y JSON (Visual Studio ya los trae activos; sqlcmd no)
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- Lee un parámetro numérico; si no existe o no es número, usa el valor por defecto.
CREATE OR ALTER FUNCTION cat.fnParametroEntero (@Clave NVARCHAR(50), @Defecto INT)
RETURNS INT
AS
BEGIN
    DECLARE @Valor INT = (SELECT TRY_CAST(Valor AS INT) FROM cat.Parametro WHERE Clave = @Clave);
    RETURN ISNULL(@Valor, @Defecto);
END;
GO

/* --------------------------------- ENCOLAR --------------------------------- */
-- Si llega otra vez el mismo (destino, tipo, clave de idempotencia), no crea un
-- mensaje nuevo: devuelve el que ya existe (@EraDuplicado = 1).
-- No abre transacción propia: así puede formar parte de la transacción de quien
-- lo llama (por ejemplo, ppPago_Registrar guarda el pago y su mensaje juntos).
CREATE OR ALTER PROCEDURE snd.ppMensaje_Encolar
    @ClienteId         INT = NULL,
    @ServicioCodigo    NVARCHAR(20),
    @Tipo              NVARCHAR(60),
    @Payload           NVARCHAR(MAX),
    @ClaveIdempotencia NVARCHAR(100) = NULL,
    @CorrelacionId     UNIQUEIDENTIFIER = NULL,
    @MensajeId         BIGINT = NULL OUTPUT,
    @MensajeGuid       UNIQUEIDENTIFIER = NULL OUTPUT,
    @EraDuplicado      BIT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ServicioId INT = (SELECT ServicioId FROM snd.Servicio WHERE Codigo = @ServicioCodigo AND Activo = 1);

    IF @ServicioId IS NULL
        THROW 50101, N'El servicio de destino no existe o está inactivo.', 1;
    IF @Tipo IS NULL OR LTRIM(RTRIM(@Tipo)) = N''
        THROW 50102, N'El tipo de mensaje es obligatorio.', 1;
    IF @Payload IS NULL OR ISJSON(@Payload) <> 1
        THROW 50103, N'El contenido del mensaje debe ser JSON válido.', 1;

    SET @EraDuplicado = 0;
    SET @MensajeId = NULL;

    IF @ClaveIdempotencia IS NOT NULL
    BEGIN
        -- UPDLOCK + HOLDLOCK: dos peticiones simultáneas con la misma clave se
        -- ponen en fila aquí, y la segunda encuentra el mensaje de la primera.
        SELECT @MensajeId = MensajeId, @MensajeGuid = MensajeGuid
        FROM snd.Mensaje WITH (UPDLOCK, HOLDLOCK)
        WHERE ServicioDestinoId = @ServicioId
          AND Tipo = @Tipo
          AND ClaveIdempotencia = @ClaveIdempotencia;

        IF @MensajeId IS NOT NULL
        BEGIN
            SET @EraDuplicado = 1;
            RETURN;
        END
    END

    DECLARE @Nuevo TABLE (MensajeId BIGINT, MensajeGuid UNIQUEIDENTIFIER);

    INSERT INTO snd.Mensaje (ClienteId, ServicioDestinoId, Tipo, ClaveIdempotencia, Payload,
                             MaxIntentos, CorrelacionId)
    OUTPUT inserted.MensajeId, inserted.MensajeGuid INTO @Nuevo
    VALUES (@ClienteId, @ServicioId, @Tipo, @ClaveIdempotencia, @Payload,
            cat.fnParametroEntero(N'Cola.MaxIntentos', 8), @CorrelacionId);

    SELECT @MensajeId = MensajeId, @MensajeGuid = MensajeGuid FROM @Nuevo;
END;
GO

/* ------------------------------- TOMAR LOTE -------------------------------- */
-- El Servicio de Windows llama a este SP en cada vuelta.
-- READPAST hace que dos trabajadores a la vez nunca tomen el mismo mensaje:
-- cada uno salta las filas que el otro ya bloqueó.
-- No toma mensajes de servicios con el cortacircuito abierto.
CREATE OR ALTER PROCEDURE snd.ppMensaje_TomarLote
    @Trabajador      NVARCHAR(100),
    @Tamano          INT = 20,
    @SegundosBloqueo INT = 120
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Ahora DATETIME2(3) = SYSUTCDATETIME();
    IF @Tamano NOT BETWEEN 1 AND 500 SET @Tamano = 20;
    IF @SegundosBloqueo NOT BETWEEN 10 AND 3600 SET @SegundosBloqueo = 120;

    DECLARE @Tomados TABLE (MensajeId BIGINT PRIMARY KEY);

    WITH Disponibles AS
    (
        SELECT TOP (@Tamano) m.MensajeId, m.Estado, m.BloqueadoHasta, m.ProcesadoPor
        FROM snd.Mensaje m WITH (UPDLOCK, READPAST, ROWLOCK)
        INNER JOIN snd.Servicio s ON s.ServicioId = m.ServicioDestinoId
        WHERE s.Activo = 1
          AND (s.EstadoCircuito <> N'Abierto' OR s.AbiertoHasta <= @Ahora)
          AND (   (m.Estado = N'Pendiente' AND m.ProximoIntento <= @Ahora)
               OR (m.Estado = N'EnProceso' AND m.BloqueadoHasta  <  @Ahora))
        ORDER BY m.ProximoIntento, m.MensajeId
    )
    UPDATE Disponibles
       SET Estado = N'EnProceso',
           BloqueadoHasta = DATEADD(SECOND, @SegundosBloqueo, @Ahora),
           ProcesadoPor = @Trabajador
    OUTPUT inserted.MensajeId INTO @Tomados;

    SELECT m.MensajeId, m.MensajeGuid, s.Codigo AS ServicioCodigo, m.Tipo, m.Payload,
           m.Intentos, m.MaxIntentos, m.ClaveIdempotencia, m.CorrelacionId, m.FechaCreacion
    FROM @Tomados t
    INNER JOIN snd.Mensaje  m ON m.MensajeId = t.MensajeId
    INNER JOIN snd.Servicio s ON s.ServicioId = m.ServicioDestinoId
    ORDER BY m.ProximoIntento, m.MensajeId;
END;
GO

/* ------------------------------ MARCAR ENTREGADO --------------------------- */
CREATE OR ALTER PROCEDURE snd.ppMensaje_MarcarEntregado
    @MensajeId     BIGINT,
    @RespuestaJson NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @RespuestaJson IS NOT NULL AND ISJSON(@RespuestaJson) <> 1
        SET @RespuestaJson = (SELECT @RespuestaJson AS texto FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);

    UPDATE snd.Mensaje
       SET Estado = N'Entregado',
           Intentos = Intentos + 1,
           FechaEntrega = SYSUTCDATETIME(),
           RespuestaJson = @RespuestaJson,
           BloqueadoHasta = NULL,
           UltimoError = NULL
     WHERE MensajeId = @MensajeId
       AND Estado = N'EnProceso';

    IF @@ROWCOUNT = 0
        THROW 50110, N'El mensaje no existe o no estaba en proceso.', 1;
END;
GO

/* ------------------------------- MARCAR FALLIDO ---------------------------- */
-- Espera exponencial: base * 2^(intentos-1), con tope. Ej. base 30 s:
-- 30 s, 1 min, 2 min, 4 min, 8 min... hasta Cola.EsperaMaximaSegundos.
CREATE OR ALTER PROCEDURE snd.ppMensaje_MarcarFallido
    @MensajeId   BIGINT,
    @Error       NVARCHAR(MAX),
    @EstadoFinal NVARCHAR(15) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Base INT = cat.fnParametroEntero(N'Cola.EsperaBaseSegundos', 30);
    DECLARE @Tope INT = cat.fnParametroEntero(N'Cola.EsperaMaximaSegundos', 3600);
    DECLARE @Resultado TABLE (Estado NVARCHAR(15));

    UPDATE snd.Mensaje
       SET Intentos = Intentos + 1,
           UltimoError = @Error,
           BloqueadoHasta = NULL,
           Estado = CASE WHEN Intentos + 1 >= MaxIntentos THEN N'Muerto' ELSE N'Pendiente' END,
           -- POWER con FLOAT y tope de exponente para que nunca desborde
           ProximoIntento = DATEADD(SECOND,
                CAST(CASE WHEN @Base * POWER(CAST(2 AS FLOAT), CASE WHEN Intentos > 20 THEN 20 ELSE Intentos END) > @Tope
                          THEN @Tope
                          ELSE @Base * POWER(CAST(2 AS FLOAT), CASE WHEN Intentos > 20 THEN 20 ELSE Intentos END)
                     END AS INT),
                SYSUTCDATETIME())
    OUTPUT inserted.Estado INTO @Resultado
     WHERE MensajeId = @MensajeId
       AND Estado = N'EnProceso';

    IF @@ROWCOUNT = 0
        THROW 50111, N'El mensaje no existe o no estaba en proceso.', 1;

    SELECT @EstadoFinal = Estado FROM @Resultado;
END;
GO

/* ------------------------------- REINTENTAR -------------------------------- */
-- Para la pantalla de administración: devuelve a la cola un mensaje Muerto.
CREATE OR ALTER PROCEDURE snd.ppMensaje_Reintentar
    @MensajeId BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE snd.Mensaje
       SET Estado = N'Pendiente',
           Intentos = 0,
           ProximoIntento = SYSUTCDATETIME(),
           BloqueadoHasta = NULL
     WHERE MensajeId = @MensajeId
       AND Estado = N'Muerto';

    IF @@ROWCOUNT = 0
        THROW 50112, N'Solo se pueden reintentar mensajes en estado Muerto.', 1;
END;
GO

/* -------------------------------- CONSULTAS -------------------------------- */
CREATE OR ALTER PROCEDURE snd.ppMensaje_Obtener
    @MensajeGuid UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT m.MensajeId, m.MensajeGuid, s.Codigo AS ServicioCodigo, m.Tipo, m.Estado,
           m.Intentos, m.MaxIntentos, m.ProximoIntento, m.UltimoError, m.RespuestaJson,
           m.CorrelacionId, m.FechaCreacion, m.FechaEntrega
    FROM snd.Mensaje m
    INNER JOIN snd.Servicio s ON s.ServicioId = m.ServicioDestinoId
    WHERE m.MensajeGuid = @MensajeGuid;
END;
GO

CREATE OR ALTER PROCEDURE snd.ppMensaje_Listar
    @Estado NVARCHAR(15) = NULL,
    @Desde  DATETIME2(3) = NULL,
    @Hasta  DATETIME2(3) = NULL,
    @Top    INT = 200
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (CASE WHEN @Top BETWEEN 1 AND 5000 THEN @Top ELSE 200 END)
           m.MensajeId, m.MensajeGuid, s.Codigo AS ServicioCodigo, c.Codigo AS ClienteCodigo,
           m.Tipo, m.Estado, m.Intentos, m.MaxIntentos, m.ProximoIntento, m.UltimoError,
           m.FechaCreacion, m.FechaEntrega
    FROM snd.Mensaje m
    INNER JOIN snd.Servicio s ON s.ServicioId = m.ServicioDestinoId
    LEFT  JOIN seg.ClienteApi c ON c.ClienteId = m.ClienteId
    WHERE (@Estado IS NULL OR m.Estado = @Estado)
      AND (@Desde IS NULL OR m.FechaCreacion >= @Desde)
      AND (@Hasta IS NULL OR m.FechaCreacion <  @Hasta)
    ORDER BY m.MensajeId DESC;
END;
GO

-- Borra mensajes entregados viejos (los muertos se conservan para revisarlos).
CREATE OR ALTER PROCEDURE snd.ppMensaje_Purgar
    @Borrados INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Dias INT = cat.fnParametroEntero(N'Cola.DiasRetencion', 30);
    DECLARE @Limite DATETIME2(3) = DATEADD(DAY, -@Dias, SYSUTCDATETIME());

    -- Los pagos guardan la referencia a su mensaje: se suelta antes de borrar.
    UPDATE p SET MensajeId = NULL
    FROM snd.PagoRecibido p
    INNER JOIN snd.Mensaje m ON m.MensajeId = p.MensajeId
    WHERE m.Estado = N'Entregado' AND m.FechaEntrega < @Limite;

    DELETE FROM snd.Mensaje
    WHERE Estado = N'Entregado' AND FechaEntrega < @Limite;

    SET @Borrados = @@ROWCOUNT;
END;
GO

PRINT N'04 - Procedimientos de la cola de mensajes listos.';
GO
