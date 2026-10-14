/* =============================================================================
   CONADIS.Integracion - 03: Procedimientos de logs, clientes y parámetros
   Convención de errores: los SP lanzan THROW 50001..59999 cuando una regla de
   negocio rechaza la operación. El mensaje está pensado para mostrarse al usuario.
   ============================================================================= */
USE CONADIS_IntegracionDB;
GO

-- Necesarios para índices filtrados y JSON (Visual Studio ya los trae activos; sqlcmd no)
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* --------------------------------- LOG4NET --------------------------------- */
-- Lo llama el AdoNetAppender de log4net.config. Recorta los textos a lo que
-- cabe en cada columna para que un log largo nunca haga fallar el registro.
CREATE OR ALTER PROCEDURE dbo.ppLog_Insertar
    @log_date  DATETIME2(3),
    @thread    NVARCHAR(255),
    @level     NVARCHAR(50),
    @logger    NVARCHAR(255),
    @message   NVARCHAR(MAX),
    @exception NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Log4NetLog ([Date], [Thread], [Level], [Logger], [Message], [Exception])
    VALUES (ISNULL(@log_date, SYSUTCDATETIME()),
            LEFT(ISNULL(@thread, N'?'), 255),
            LEFT(ISNULL(@level, N'INFO'), 50),
            LEFT(ISNULL(@logger, N'?'), 255),
            ISNULL(@message, N''),
            NULLIF(@exception, N''));
END;
GO

CREATE OR ALTER PROCEDURE dbo.ppLog_Listar
    @Desde DATETIME2(3) = NULL,
    @Hasta DATETIME2(3) = NULL,
    @Nivel NVARCHAR(50) = NULL,
    @Top   INT = 200
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (CASE WHEN @Top BETWEEN 1 AND 5000 THEN @Top ELSE 200 END)
           Id, [Date], [Thread], [Level], [Logger], [Message], [Exception]
    FROM dbo.Log4NetLog
    WHERE (@Desde IS NULL OR [Date] >= @Desde)
      AND (@Hasta IS NULL OR [Date] <  @Hasta)
      AND (@Nivel IS NULL OR [Level] = @Nivel)
    ORDER BY Id DESC;
END;
GO

/* -------------------------------- CLIENTES --------------------------------- */
-- La API calcula el SHA-256 de la clave recibida en X-Api-Key y llama a este SP.
-- Devuelve una fila si la clave es válida y el cliente está activo; ninguna si no.
CREATE OR ALTER PROCEDURE seg.ppCliente_ValidarApiKey
    @ApiKeyHash CHAR(64)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Cliente TABLE (ClienteId INT, Codigo NVARCHAR(20), Nombre NVARCHAR(100));

    UPDATE seg.ClienteApi
       SET UltimoUso = SYSUTCDATETIME()
    OUTPUT inserted.ClienteId, inserted.Codigo, inserted.Nombre INTO @Cliente
     WHERE ApiKeyHash = UPPER(@ApiKeyHash)
       AND Activo = 1;

    SELECT ClienteId, Codigo, Nombre FROM @Cliente;
END;
GO

-- Da de alta un cliente o le cambia la clave (rotación). Solo se recibe el hash.
CREATE OR ALTER PROCEDURE seg.ppCliente_Registrar
    @Codigo     NVARCHAR(20),
    @Nombre     NVARCHAR(100),
    @ApiKeyHash CHAR(64),
    @ClienteId  INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Codigo IS NULL OR LTRIM(RTRIM(@Codigo)) = N''
        THROW 50001, N'El código del cliente es obligatorio.', 1;
    IF @ApiKeyHash IS NULL OR LEN(@ApiKeyHash) <> 64 OR UPPER(@ApiKeyHash) LIKE '%[^0-9A-F]%'
        THROW 50002, N'El hash de la API key debe ser un SHA-256 en hexadecimal (64 caracteres).', 1;

    BEGIN TRANSACTION;

    SELECT @ClienteId = ClienteId
    FROM seg.ClienteApi WITH (UPDLOCK, HOLDLOCK)
    WHERE Codigo = UPPER(@Codigo);

    IF @ClienteId IS NULL
    BEGIN
        INSERT INTO seg.ClienteApi (Codigo, Nombre, ApiKeyHash)
        VALUES (UPPER(@Codigo), @Nombre, UPPER(@ApiKeyHash));
        SET @ClienteId = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE seg.ClienteApi
           SET Nombre = @Nombre, ApiKeyHash = UPPER(@ApiKeyHash), Activo = 1
         WHERE ClienteId = @ClienteId;
    END

    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE seg.ppCliente_CambiarEstado
    @Codigo NVARCHAR(20),
    @Activo BIT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE seg.ClienteApi SET Activo = @Activo WHERE Codigo = @Codigo;
    IF @@ROWCOUNT = 0
        THROW 50003, N'No existe un cliente con ese código.', 1;
END;
GO

CREATE OR ALTER PROCEDURE seg.ppCliente_Listar
AS
BEGIN
    SET NOCOUNT ON;
    -- Nunca se devuelve el hash: no le sirve a ninguna pantalla.
    SELECT ClienteId, Codigo, Nombre, Activo, FechaCreacion, UltimoUso
    FROM seg.ClienteApi
    ORDER BY Codigo;
END;
GO

/* ------------------------------- PARÁMETROS -------------------------------- */
CREATE OR ALTER PROCEDURE cat.ppParametro_Listar
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Clave, Valor, Descripcion FROM cat.Parametro ORDER BY Clave;
END;
GO

CREATE OR ALTER PROCEDURE cat.ppParametro_Obtener
    @Clave NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Clave, Valor, Descripcion FROM cat.Parametro WHERE Clave = @Clave;
END;
GO

CREATE OR ALTER PROCEDURE cat.ppParametro_Actualizar
    @Clave NVARCHAR(50),
    @Valor NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE cat.Parametro SET Valor = @Valor WHERE Clave = @Clave;
    IF @@ROWCOUNT = 0
        THROW 50004, N'No existe un parámetro con esa clave.', 1;
END;
GO

PRINT N'03 - Procedimientos de logs, clientes y parámetros listos.';
GO
