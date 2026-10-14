/* =============================================================================
   CONADIS.Integracion - 03: Procedimientos comunes
   Log4Net, parámetros, sistemas cliente (API keys), operadores y alertas.
   Errores de negocio de Integración: THROW 56001..56999 (C# los convierte en
   ReglaNegocioException, igual que en el Core).
   ============================================================================= */
USE CONADIS_IntegracionDB;
GO

/* ----------------------------- LOG4NET ------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.ppInsertarLog
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
    VALUES (@log_date, LEFT(@thread, 255), LEFT(@level, 50), LEFT(@logger, 255), @message, @exception);
END;
GO

CREATE OR ALTER PROCEDURE dbo.ppLog_Listar
    @Desde  DATETIME2(3),
    @Hasta  DATETIME2(3),
    @Nivel  NVARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (1000) Id, [Date] AS Fecha, [Level] AS Nivel, [Logger] AS Origen, [Message] AS Mensaje, [Exception] AS Excepcion
    FROM dbo.Log4NetLog
    WHERE [Date] >= @Desde AND [Date] < @Hasta
      AND (@Nivel IS NULL OR [Level] = @Nivel)
    ORDER BY Id DESC;
END;
GO

/* ----------------------------- PARÁMETROS ---------------------------------- */
CREATE OR ALTER PROCEDURE cfg.ppParametro_Listar
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Clave, Valor, Descripcion, FechaCambio, UsuarioCambio
    FROM cfg.Parametro
    ORDER BY Clave;
END;
GO

CREATE OR ALTER PROCEDURE cfg.ppParametro_Actualizar
    @Clave    VARCHAR(60),
    @Valor    NVARCHAR(200),
    @Usuario  NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Valor IS NULL OR LTRIM(RTRIM(@Valor)) = N''
        THROW 56001, N'El valor del parámetro no puede quedar vacío.', 1;

    UPDATE cfg.Parametro
    SET Valor = LTRIM(RTRIM(@Valor)), FechaCambio = SYSDATETIMEOFFSET(), UsuarioCambio = @Usuario
    WHERE Clave = @Clave;

    IF @@ROWCOUNT = 0
        THROW 56002, N'El parámetro no existe.', 1;
END;
GO

/* ----------------------------- SISTEMAS CLIENTE ---------------------------- */
-- La API calcula el SHA-256 de la clave recibida y busca por él (la clave nunca llega a SQL).
CREATE OR ALTER PROCEDURE seg.ppCliente_ObtenerPorHash
    @HashClave CHAR(64)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ClienteId, Nombre, Tipo, Ambitos, VerTipoGrado, ConvenioHasta,
           CAST(CASE WHEN Tipo <> N'Institucion' OR ConvenioHasta >= CAST(SYSDATETIMEOFFSET() AS DATE)
                     THEN 1 ELSE 0 END AS BIT) AS ConvenioVigente
    FROM seg.ClienteApi
    WHERE HashClave = UPPER(@HashClave) AND Activo = 1;
END;
GO

CREATE OR ALTER PROCEDURE seg.ppCliente_Listar
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ClienteId, Nombre, Tipo, Ambitos, VerTipoGrado, ConvenioHasta, Activo, FechaCreacion
    FROM seg.ClienteApi
    ORDER BY Tipo, Nombre;
END;
GO

-- Crear (@ClienteId NULL) o editar. @HashClave es obligatorio al crear; al editar, NULL = no cambiar la clave.
CREATE OR ALTER PROCEDURE seg.ppCliente_Guardar
    @ClienteId      INT = NULL,
    @Nombre         NVARCHAR(100),
    @Tipo           NVARCHAR(20),
    @HashClave      CHAR(64) = NULL,
    @Ambitos        NVARCHAR(300),
    @VerTipoGrado   BIT = 0,
    @ConvenioHasta  DATE = NULL,
    @Activo         BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tipo = N'Institucion' AND @ConvenioHasta IS NULL
        THROW 56003, N'Una institución necesita la fecha de vencimiento de su convenio.', 1;

    IF @ClienteId IS NULL
    BEGIN
        IF @HashClave IS NULL
            THROW 56004, N'Falta la API key del nuevo cliente.', 1;
        IF EXISTS (SELECT 1 FROM seg.ClienteApi WHERE Nombre = @Nombre)
            THROW 56005, N'Ya existe un cliente con ese nombre.', 1;

        INSERT INTO seg.ClienteApi (Nombre, Tipo, HashClave, Ambitos, VerTipoGrado, ConvenioHasta, Activo)
        VALUES (@Nombre, @Tipo, UPPER(@HashClave), @Ambitos, @VerTipoGrado, @ConvenioHasta, @Activo);
        SELECT CAST(SCOPE_IDENTITY() AS INT) AS ClienteId;
    END
    ELSE
    BEGIN
        UPDATE seg.ClienteApi
        SET Nombre = @Nombre, Tipo = @Tipo, HashClave = COALESCE(UPPER(@HashClave), HashClave),
            Ambitos = @Ambitos, VerTipoGrado = @VerTipoGrado, ConvenioHasta = @ConvenioHasta, Activo = @Activo
        WHERE ClienteId = @ClienteId;

        IF @@ROWCOUNT = 0
            THROW 56006, N'El cliente no existe.', 1;
        SELECT @ClienteId AS ClienteId;
    END
END;
GO

/* ----------------------------- OPERADORES ---------------------------------- */
CREATE OR ALTER PROCEDURE seg.ppOperador_ObtenerParaLogin
    @Username NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT OperadorId, Username, PasswordHash, NombreCompleto, Rol, Activo, IntentosFallidos, BloqueadoHasta
    FROM seg.Operador
    WHERE Username = @Username;
END;
GO

-- 5 intentos fallidos = bloqueo de 15 minutos (valores en cfg.Parametro).
CREATE OR ALTER PROCEDURE seg.ppOperador_RegistrarIntentoFallido
    @OperadorId INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Max INT = ISNULL(TRY_CAST((SELECT Valor FROM cfg.Parametro WHERE Clave = 'LoginIntentosMaximos') AS INT), 5);
    DECLARE @Minutos INT = ISNULL(TRY_CAST((SELECT Valor FROM cfg.Parametro WHERE Clave = 'LoginMinutosBloqueo') AS INT), 15);

    UPDATE seg.Operador
    SET IntentosFallidos = CASE WHEN IntentosFallidos + 1 >= @Max THEN 0 ELSE IntentosFallidos + 1 END,
        BloqueadoHasta   = CASE WHEN IntentosFallidos + 1 >= @Max
                                THEN DATEADD(MINUTE, @Minutos, SYSDATETIMEOFFSET()) ELSE BloqueadoHasta END
    WHERE OperadorId = @OperadorId;

    SELECT BloqueadoHasta FROM seg.Operador WHERE OperadorId = @OperadorId;
END;
GO

CREATE OR ALTER PROCEDURE seg.ppOperador_RegistrarAccesoExitoso
    @OperadorId INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE seg.Operador
    SET IntentosFallidos = 0, BloqueadoHasta = NULL, UltimoAcceso = SYSDATETIMEOFFSET()
    WHERE OperadorId = @OperadorId;
END;
GO

/* ----------------------------- ALERTAS ------------------------------------- */
-- No repite una alerta idéntica sin atender de la última hora (evita inundar al administrador).
CREATE OR ALTER PROCEDURE integ.ppAlerta_Registrar
    @Severidad  VARCHAR(10),
    @Servicio   VARCHAR(20),
    @Mensaje    NVARCHAR(500)
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM integ.Alerta
                   WHERE Atendida = 0 AND Servicio = @Servicio AND Mensaje = @Mensaje
                     AND Fecha > DATEADD(HOUR, -1, SYSDATETIMEOFFSET()))
        INSERT INTO integ.Alerta (Severidad, Servicio, Mensaje) VALUES (@Severidad, @Servicio, LEFT(@Mensaje, 500));
END;
GO

CREATE OR ALTER PROCEDURE integ.ppAlerta_ListarPendientes
AS
BEGIN
    SET NOCOUNT ON;
    SELECT AlertaId, Fecha, Severidad, Servicio, Mensaje
    FROM integ.Alerta
    WHERE Atendida = 0
    ORDER BY CASE Severidad WHEN 'Alta' THEN 1 WHEN 'Media' THEN 2 ELSE 3 END, Fecha DESC;
END;
GO

CREATE OR ALTER PROCEDURE integ.ppAlerta_Atender
    @AlertaId    BIGINT,
    @Usuario     NVARCHAR(100),
    @Comentario  NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE integ.Alerta
    SET Atendida = 1, AtendidaPor = @Usuario, FechaAtencion = SYSDATETIMEOFFSET(), Comentario = @Comentario
    WHERE AlertaId = @AlertaId AND Atendida = 0;

    IF @@ROWCOUNT = 0
        THROW 56007, N'La alerta no existe o ya fue atendida.', 1;
END;
GO

PRINT N'03 - Procedimientos comunes creados.';
GO
