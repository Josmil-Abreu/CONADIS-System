/* =============================================================================
   CONADIS.Integracion - 06: Servicios (cortacircuito), caché y bitácora

   Cortacircuito (circuit breaker), por servicio:
     Cerrado     -> se llama normal. Cada fallo suma; al llegar a UmbralFallos se abre.
     Abierto     -> no se llama (se responde desde caché o se encola) hasta AbiertoHasta.
     SemiAbierto -> pasó el tiempo: se deja pasar una llamada de prueba.
                    Si sale bien se cierra; si falla se abre otra vez.
   ============================================================================= */
USE CONADIS_IntegracionDB;
GO

-- Necesarios para índices filtrados y JSON (Visual Studio ya los trae activos; sqlcmd no)
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* -------------------------------- SERVICIOS -------------------------------- */
CREATE OR ALTER PROCEDURE snd.ppServicio_Listar
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ServicioId, Codigo, Nombre, UrlBase, EsSimulado, EstadoCircuito, FallosConsecutivos,
           UmbralFallos, SegundosApertura, AbiertoHasta, UltimoExito, UltimoFallo, Activo
    FROM snd.Servicio
    ORDER BY Codigo;
END;
GO

CREATE OR ALTER PROCEDURE snd.ppServicio_Obtener
    @Codigo NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ServicioId, Codigo, Nombre, UrlBase, EsSimulado, EstadoCircuito, FallosConsecutivos,
           UmbralFallos, SegundosApertura, AbiertoHasta, UltimoExito, UltimoFallo, Activo
    FROM snd.Servicio
    WHERE Codigo = @Codigo;
END;
GO

-- Se llama ANTES de cada llamada a un servicio. @Permitido = 0 significa
-- "no llames: usa la caché o encola".
CREATE OR ALTER PROCEDURE snd.ppServicio_PuedeLlamar
    @Codigo    NVARCHAR(20),
    @Permitido BIT = NULL OUTPUT,
    @Estado    NVARCHAR(15) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Ahora DATETIME2(3) = SYSUTCDATETIME();

    -- Si el tiempo de apertura ya pasó, pasa a SemiAbierto (deja una llamada de prueba)
    UPDATE snd.Servicio
       SET EstadoCircuito = N'SemiAbierto'
     WHERE Codigo = @Codigo AND EstadoCircuito = N'Abierto' AND AbiertoHasta <= @Ahora;

    SELECT @Estado = EstadoCircuito,
           @Permitido = CASE WHEN Activo = 1 AND EstadoCircuito <> N'Abierto' THEN 1 ELSE 0 END
    FROM snd.Servicio
    WHERE Codigo = @Codigo;

    IF @Estado IS NULL
        THROW 50401, N'El servicio no existe.', 1;
END;
GO

CREATE OR ALTER PROCEDURE snd.ppServicio_RegistrarExito
    @Codigo NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE snd.Servicio
       SET EstadoCircuito = N'Cerrado', FallosConsecutivos = 0, AbiertoHasta = NULL,
           UltimoExito = SYSUTCDATETIME()
     WHERE Codigo = @Codigo;

    IF @@ROWCOUNT = 0
        THROW 50401, N'El servicio no existe.', 1;
END;
GO

CREATE OR ALTER PROCEDURE snd.ppServicio_RegistrarFallo
    @Codigo NVARCHAR(20),
    @Estado NVARCHAR(15) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Ahora DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @Resultado TABLE (Estado NVARCHAR(15));

    UPDATE snd.Servicio
       SET FallosConsecutivos = FallosConsecutivos + 1,
           UltimoFallo = @Ahora,
           -- Un fallo en SemiAbierto, o llegar al umbral, abre el circuito
           EstadoCircuito = CASE WHEN EstadoCircuito = N'SemiAbierto'
                                   OR FallosConsecutivos + 1 >= UmbralFallos
                                 THEN N'Abierto' ELSE EstadoCircuito END,
           AbiertoHasta   = CASE WHEN EstadoCircuito = N'SemiAbierto'
                                   OR FallosConsecutivos + 1 >= UmbralFallos
                                 THEN DATEADD(SECOND, SegundosApertura, @Ahora) ELSE AbiertoHasta END
    OUTPUT inserted.EstadoCircuito INTO @Resultado
     WHERE Codigo = @Codigo;

    IF @@ROWCOUNT = 0
        THROW 50401, N'El servicio no existe.', 1;

    SELECT @Estado = Estado FROM @Resultado;
END;
GO

CREATE OR ALTER PROCEDURE snd.ppServicio_Configurar
    @Codigo           NVARCHAR(20),
    @UrlBase          NVARCHAR(300) = NULL,
    @EsSimulado       BIT,
    @UmbralFallos     INT,
    @SegundosApertura INT,
    @Activo           BIT
AS
BEGIN
    SET NOCOUNT ON;
    IF @EsSimulado = 0 AND NULLIF(@UrlBase, N'') IS NULL
        THROW 50402, N'Un servicio real necesita su dirección (UrlBase).', 1;

    UPDATE snd.Servicio
       SET UrlBase = NULLIF(@UrlBase, N''), EsSimulado = @EsSimulado, UmbralFallos = @UmbralFallos,
           SegundosApertura = @SegundosApertura, Activo = @Activo
     WHERE Codigo = @Codigo;

    IF @@ROWCOUNT = 0
        THROW 50401, N'El servicio no existe.', 1;
END;
GO

/* ---------------------------------- CACHÉ ---------------------------------- */
-- @PermitirVencido = 1 se usa cuando el servicio está caído: mejor un dato
-- de hace un rato (marcado como vencido) que ningún dato.
CREATE OR ALTER PROCEDURE snd.ppCache_Obtener
    @Clave           NVARCHAR(200),
    @PermitirVencido BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Ahora DATETIME2(3) = SYSUTCDATETIME();

    SELECT c.Clave, s.Codigo AS ServicioCodigo, c.ValorJson, c.FechaActualizacion, c.FechaExpira,
           CAST(CASE WHEN c.FechaExpira < @Ahora THEN 1 ELSE 0 END AS BIT) AS Vencido
    FROM snd.Cache c
    INNER JOIN snd.Servicio s ON s.ServicioId = c.ServicioId
    WHERE c.Clave = @Clave
      AND (@PermitirVencido = 1 OR c.FechaExpira >= @Ahora);
END;
GO

CREATE OR ALTER PROCEDURE snd.ppCache_Guardar
    @Clave            NVARCHAR(200),
    @ServicioCodigo   NVARCHAR(20),
    @ValorJson        NVARCHAR(MAX),
    @SegundosVigencia INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ServicioId INT = (SELECT ServicioId FROM snd.Servicio WHERE Codigo = @ServicioCodigo);
    IF @ServicioId IS NULL
        THROW 50401, N'El servicio no existe.', 1;
    IF @ValorJson IS NULL OR ISJSON(@ValorJson) <> 1
        THROW 50403, N'El valor a guardar en caché debe ser JSON válido.', 1;

    DECLARE @Ahora DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @Expira DATETIME2(3) = DATEADD(SECOND,
        ISNULL(@SegundosVigencia, cat.fnParametroEntero(N'Cache.SegundosVigencia', 900)), @Ahora);

    BEGIN TRANSACTION;

    UPDATE snd.Cache WITH (UPDLOCK, HOLDLOCK)
       SET ServicioId = @ServicioId, ValorJson = @ValorJson,
           FechaActualizacion = @Ahora, FechaExpira = @Expira
     WHERE Clave = @Clave;

    IF @@ROWCOUNT = 0
        INSERT INTO snd.Cache (Clave, ServicioId, ValorJson, FechaActualizacion, FechaExpira)
        VALUES (@Clave, @ServicioId, @ValorJson, @Ahora, @Expira);

    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE snd.ppCache_Invalidar
    @Clave NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM snd.Cache WHERE Clave = @Clave;
END;
GO

-- Borra entradas vencidas hace más de Cache.DiasRetencion días.
CREATE OR ALTER PROCEDURE snd.ppCache_Purgar
    @Borrados INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM snd.Cache
    WHERE FechaExpira < DATEADD(DAY, -cat.fnParametroEntero(N'Cache.DiasRetencion', 7), SYSUTCDATETIME());
    SET @Borrados = @@ROWCOUNT;
END;
GO

/* --------------------------------- BITÁCORA -------------------------------- */
CREATE OR ALTER PROCEDURE snd.ppBitacora_Registrar
    @CorrelacionId   UNIQUEIDENTIFIER = NULL,
    @ClienteId       INT = NULL,
    @ServicioCodigo  NVARCHAR(20) = NULL,
    @Direccion       NVARCHAR(10),
    @Operacion       NVARCHAR(100),
    @CodigoResultado INT = NULL,
    @Exitoso         BIT,
    @DuracionMs      INT,
    @Detalle         NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO snd.Bitacora (CorrelacionId, ClienteId, ServicioId, Direccion, Operacion,
                              CodigoResultado, Exitoso, DuracionMs, Detalle)
    VALUES (@CorrelacionId, @ClienteId,
            (SELECT ServicioId FROM snd.Servicio WHERE Codigo = @ServicioCodigo),
            @Direccion, LEFT(@Operacion, 100), @CodigoResultado, @Exitoso,
            CASE WHEN @DuracionMs < 0 THEN 0 ELSE @DuracionMs END, @Detalle);
END;
GO

CREATE OR ALTER PROCEDURE snd.ppBitacora_Listar
    @Desde          DATETIME2(3),
    @Hasta          DATETIME2(3),
    @ServicioCodigo NVARCHAR(20) = NULL,
    @SoloErrores    BIT = 0,
    @CorrelacionId  UNIQUEIDENTIFIER = NULL,
    @Top            INT = 500
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (CASE WHEN @Top BETWEEN 1 AND 5000 THEN @Top ELSE 500 END)
           b.BitacoraId, b.Fecha, b.CorrelacionId, c.Codigo AS ClienteCodigo, s.Codigo AS ServicioCodigo,
           b.Direccion, b.Operacion, b.CodigoResultado, b.Exitoso, b.DuracionMs, b.Detalle
    FROM snd.Bitacora b
    LEFT JOIN seg.ClienteApi c ON c.ClienteId  = b.ClienteId
    LEFT JOIN snd.Servicio  s ON s.ServicioId = b.ServicioId
    WHERE b.Fecha >= @Desde AND b.Fecha < @Hasta
      AND (@ServicioCodigo IS NULL OR s.Codigo = @ServicioCodigo)
      AND (@SoloErrores = 0 OR b.Exitoso = 0)
      AND (@CorrelacionId IS NULL OR b.CorrelacionId = @CorrelacionId)
    ORDER BY b.BitacoraId DESC;
END;
GO

PRINT N'06 - Procedimientos de servicios, caché y bitácora listos.';
GO
