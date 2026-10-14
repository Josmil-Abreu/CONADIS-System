/* =============================================================================
   CONADIS.Web - 03: Procedimientos del Portal Ciudadano
   Errores de negocio del portal: THROW 57001..57999 (C# los convierte en ReglaNegocioException).
   ============================================================================= */
USE CONADIS_WebDB;
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

/* ----------------------------- EVENTOS DE SEGURIDAD ------------------------ */
CREATE OR ALTER PROCEDURE web.ppEvento_Registrar
    @CuentaId  INT = NULL,
    @Evento    NVARCHAR(50),
    @Ip        VARCHAR(45) = NULL,
    @Detalle   NVARCHAR(300) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO web.EventoSeguridad (CuentaId, Evento, Ip, Detalle) VALUES (@CuentaId, @Evento, @Ip, LEFT(@Detalle, 300));
END;
GO

CREATE OR ALTER PROCEDURE web.ppEvento_ListarPorCuenta
    @CuentaId INT,
    @Maximo   INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Maximo) Fecha, Evento, Ip, Detalle
    FROM web.EventoSeguridad
    WHERE CuentaId = @CuentaId
    ORDER BY EventoId DESC;
END;
GO

/* ----------------------------- CUENTAS ------------------------------------- */
CREATE OR ALTER PROCEDURE web.ppCuenta_Crear
    @Cedula            CHAR(11),
    @Correo            NVARCHAR(150),
    @Telefono          NVARCHAR(20) = NULL,
    @NombreCompleto    NVARCHAR(200),
    @PasswordHash      VARCHAR(200),
    @EstadoIdentidad   NVARCHAR(20),
    @TokenVerificacion CHAR(64),
    @HorasToken        INT = 48,
    @Ip                VARCHAR(45) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM web.Cuenta WHERE Cedula = @Cedula)
        THROW 57001, N'Ya existe una cuenta con esta cédula. Inicie sesión o recupere su contraseña.', 1;
    IF EXISTS (SELECT 1 FROM web.Cuenta WHERE Correo = @Correo)
        THROW 57002, N'Ya existe una cuenta con este correo electrónico.', 1;

    DECLARE @CuentaId INT;
    BEGIN TRANSACTION;
        INSERT INTO web.Cuenta (Cedula, Correo, Telefono, NombreCompleto, PasswordHash, EstadoIdentidad,
                                TokenVerificacion, VenceVerificacion)
        VALUES (@Cedula, @Correo, @Telefono, @NombreCompleto, @PasswordHash, @EstadoIdentidad,
                UPPER(@TokenVerificacion), DATEADD(HOUR, @HorasToken, SYSDATETIMEOFFSET()));
        SET @CuentaId = SCOPE_IDENTITY();

        INSERT INTO web.Preferencia (CuentaId) VALUES (@CuentaId);
        EXEC web.ppEvento_Registrar @CuentaId = @CuentaId, @Evento = N'CuentaCreada', @Ip = @Ip;
    COMMIT TRANSACTION;

    SELECT @CuentaId AS CuentaId;
END;
GO

-- El ciudadano puede entrar con su cédula o con su correo.
CREATE OR ALTER PROCEDURE web.ppCuenta_ObtenerParaLogin
    @Usuario NVARCHAR(150)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT c.CuentaId, c.Cedula, c.Correo, c.Telefono, c.NombreCompleto, c.PasswordHash, c.EstadoIdentidad,
           c.CorreoVerificado, c.Activa, c.IntentosFallidos, c.BloqueadoHasta,
           ISNULL(p.AltoContraste, 0) AS AltoContraste, ISNULL(p.TamanoTexto, 100) AS TamanoTexto
    FROM web.Cuenta c
    LEFT JOIN web.Preferencia p ON p.CuentaId = c.CuentaId
    WHERE c.Cedula = REPLACE(REPLACE(@Usuario, '-', ''), ' ', '') OR c.Correo = @Usuario;
END;
GO

-- 5 intentos fallidos = bloqueo de 15 minutos (web.Parametro).
CREATE OR ALTER PROCEDURE web.ppCuenta_RegistrarIntentoFallido
    @CuentaId INT,
    @Ip       VARCHAR(45) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Max INT = ISNULL(TRY_CAST((SELECT Valor FROM web.Parametro WHERE Clave = 'LoginIntentosMaximos') AS INT), 5);
    DECLARE @Minutos INT = ISNULL(TRY_CAST((SELECT Valor FROM web.Parametro WHERE Clave = 'LoginMinutosBloqueo') AS INT), 15);
    DECLARE @Bloquea BIT;

    BEGIN TRANSACTION;
        SELECT @Bloquea = CASE WHEN IntentosFallidos + 1 >= @Max THEN 1 ELSE 0 END
        FROM web.Cuenta WITH (UPDLOCK) WHERE CuentaId = @CuentaId;

        UPDATE web.Cuenta
        SET IntentosFallidos = CASE WHEN @Bloquea = 1 THEN 0 ELSE IntentosFallidos + 1 END,
            BloqueadoHasta   = CASE WHEN @Bloquea = 1 THEN DATEADD(MINUTE, @Minutos, SYSDATETIMEOFFSET()) ELSE BloqueadoHasta END
        WHERE CuentaId = @CuentaId;

        EXEC web.ppEvento_Registrar @CuentaId = @CuentaId, @Evento = N'ContrasenaIncorrecta', @Ip = @Ip;
        IF @Bloquea = 1
            EXEC web.ppEvento_Registrar @CuentaId = @CuentaId, @Evento = N'CuentaBloqueada', @Ip = @Ip;
    COMMIT TRANSACTION;

    SELECT BloqueadoHasta FROM web.Cuenta WHERE CuentaId = @CuentaId;
END;
GO

CREATE OR ALTER PROCEDURE web.ppCuenta_RegistrarAccesoExitoso
    @CuentaId INT,
    @Ip       VARCHAR(45) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE web.Cuenta SET IntentosFallidos = 0, BloqueadoHasta = NULL, UltimoAcceso = SYSDATETIMEOFFSET()
    WHERE CuentaId = @CuentaId;
    EXEC web.ppEvento_Registrar @CuentaId = @CuentaId, @Evento = N'InicioSesion', @Ip = @Ip;
END;
GO

CREATE OR ALTER PROCEDURE web.ppCuenta_Obtener
    @CuentaId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT c.CuentaId, c.Cedula, c.Correo, c.Telefono, c.NombreCompleto, c.EstadoIdentidad, c.CorreoVerificado,
           c.UltimoAcceso, c.FechaCreacion, p.AltoContraste, p.TamanoTexto
    FROM web.Cuenta c
    INNER JOIN web.Preferencia p ON p.CuentaId = c.CuentaId
    WHERE c.CuentaId = @CuentaId AND c.Activa = 1;
END;
GO

-- Enlace de verificación del correo (se guarda el SHA-256 del token, no el token).
CREATE OR ALTER PROCEDURE web.ppCuenta_VerificarCorreo
    @TokenHash CHAR(64)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @CuentaId INT = (SELECT CuentaId FROM web.Cuenta
                             WHERE TokenVerificacion = UPPER(@TokenHash) AND VenceVerificacion > SYSDATETIMEOFFSET());
    IF @CuentaId IS NULL
        THROW 57003, N'El enlace de verificación no es válido o ya venció. Solicite uno nuevo.', 1;

    UPDATE web.Cuenta SET CorreoVerificado = 1, TokenVerificacion = NULL, VenceVerificacion = NULL WHERE CuentaId = @CuentaId;
    EXEC web.ppEvento_Registrar @CuentaId = @CuentaId, @Evento = N'CorreoVerificado';
    SELECT @CuentaId AS CuentaId;
END;
GO

CREATE OR ALTER PROCEDURE web.ppCuenta_ActualizarContacto
    @CuentaId  INT,
    @Correo    NVARCHAR(150),
    @Telefono  NVARCHAR(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM web.Cuenta WHERE Correo = @Correo AND CuentaId <> @CuentaId)
        THROW 57002, N'Ya existe una cuenta con este correo electrónico.', 1;

    UPDATE web.Cuenta
    SET CorreoVerificado = CASE WHEN Correo = @Correo THEN CorreoVerificado ELSE 0 END,
        Correo = @Correo, Telefono = @Telefono
    WHERE CuentaId = @CuentaId;
    EXEC web.ppEvento_Registrar @CuentaId = @CuentaId, @Evento = N'ContactoActualizado';
END;
GO

CREATE OR ALTER PROCEDURE web.ppCuenta_ObtenerHash
    @CuentaId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT PasswordHash FROM web.Cuenta WHERE CuentaId = @CuentaId AND Activa = 1;
END;
GO

CREATE OR ALTER PROCEDURE web.ppCuenta_CambiarContrasena
    @CuentaId      INT,
    @PasswordHash  VARCHAR(200),
    @Ip            VARCHAR(45) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE web.Cuenta SET PasswordHash = @PasswordHash, IntentosFallidos = 0, BloqueadoHasta = NULL WHERE CuentaId = @CuentaId;
    IF @@ROWCOUNT = 0 THROW 57005, N'La cuenta no existe.', 1;
    EXEC web.ppEvento_Registrar @CuentaId = @CuentaId, @Evento = N'ContrasenaCambiada', @Ip = @Ip;
END;
GO

/* ----------------------------- RECUPERACIÓN DE CONTRASEÑA ------------------ */
-- Si el correo no existe no devuelve nada (y la aplicación responde lo mismo: no revela qué correos existen).
CREATE OR ALTER PROCEDURE web.ppRecuperacion_Crear
    @Correo     NVARCHAR(150),
    @TokenHash  CHAR(64),
    @Minutos    INT = 30
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @CuentaId INT = (SELECT CuentaId FROM web.Cuenta WHERE Correo = @Correo AND Activa = 1);
    IF @CuentaId IS NULL RETURN;

    -- Un solo enlace vigente por cuenta
    UPDATE web.Recuperacion SET Usado = 1 WHERE CuentaId = @CuentaId AND Usado = 0;
    INSERT INTO web.Recuperacion (TokenHash, CuentaId, VenceEn) VALUES (UPPER(@TokenHash), @CuentaId, DATEADD(MINUTE, @Minutos, SYSDATETIMEOFFSET()));
    EXEC web.ppEvento_Registrar @CuentaId = @CuentaId, @Evento = N'RecuperacionSolicitada';
    SELECT @CuentaId AS CuentaId, NombreCompleto FROM web.Cuenta WHERE CuentaId = @CuentaId;
END;
GO

CREATE OR ALTER PROCEDURE web.ppRecuperacion_Usar
    @TokenHash     CHAR(64),
    @PasswordHash  VARCHAR(200),
    @Ip            VARCHAR(45) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @CuentaId INT;

    BEGIN TRANSACTION;
        SELECT @CuentaId = CuentaId FROM web.Recuperacion WITH (UPDLOCK)
        WHERE TokenHash = UPPER(@TokenHash) AND Usado = 0 AND VenceEn > SYSDATETIMEOFFSET();

        IF @CuentaId IS NULL
            THROW 57004, N'El enlace para restablecer la contraseña no es válido o ya venció.', 1;

        UPDATE web.Recuperacion SET Usado = 1 WHERE TokenHash = UPPER(@TokenHash);
        EXEC web.ppCuenta_CambiarContrasena @CuentaId = @CuentaId, @PasswordHash = @PasswordHash, @Ip = @Ip;
    COMMIT TRANSACTION;
END;
GO

/* ----------------------------- PREFERENCIAS -------------------------------- */
CREATE OR ALTER PROCEDURE web.ppPreferencia_Guardar
    @CuentaId       INT,
    @AltoContraste  BIT,
    @TamanoTexto    INT
AS
BEGIN
    SET NOCOUNT ON;
    IF @TamanoTexto NOT BETWEEN 100 AND 200
        THROW 57006, N'El tamaño de texto debe estar entre 100 % y 200 %.', 1;

    UPDATE web.Preferencia SET AltoContraste = @AltoContraste, TamanoTexto = @TamanoTexto, FechaCambio = SYSDATETIMEOFFSET()
    WHERE CuentaId = @CuentaId;
    IF @@ROWCOUNT = 0
        INSERT INTO web.Preferencia (CuentaId, AltoContraste, TamanoTexto) VALUES (@CuentaId, @AltoContraste, @TamanoTexto);
END;
GO

/* ----------------------------- BORRADORES ---------------------------------- */
-- Guarda el paso actual y lo escrito. La clave de idempotencia se crea con el primer guardado y se mantiene.
CREATE OR ALTER PROCEDURE web.ppBorrador_Guardar
    @CuentaId   INT,
    @Tipo       NVARCHAR(30),
    @Paso       INT,
    @Datos      NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF ISJSON(@Datos) = 0 THROW 57007, N'Los datos del borrador no son válidos.', 1;

    BEGIN TRANSACTION;
        UPDATE web.Borrador WITH (UPDLOCK, HOLDLOCK)
        SET Paso = @Paso, Datos = @Datos, FechaActualizacion = SYSDATETIMEOFFSET()
        WHERE CuentaId = @CuentaId AND Tipo = @Tipo;

        IF @@ROWCOUNT = 0
            INSERT INTO web.Borrador (CuentaId, Tipo, Paso, Datos, ClaveIdempotencia)
            VALUES (@CuentaId, @Tipo, @Paso, @Datos, CONCAT('portal-', @CuentaId, '-', LOWER(REPLACE(CONVERT(VARCHAR(36), NEWID()), '-', ''))));
    COMMIT TRANSACTION;

    SELECT Paso, Datos, ClaveIdempotencia, FechaActualizacion FROM web.Borrador WHERE CuentaId = @CuentaId AND Tipo = @Tipo;
END;
GO

CREATE OR ALTER PROCEDURE web.ppBorrador_Obtener
    @CuentaId INT,
    @Tipo     NVARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Paso, Datos, ClaveIdempotencia, FechaActualizacion FROM web.Borrador WHERE CuentaId = @CuentaId AND Tipo = @Tipo;
END;
GO

CREATE OR ALTER PROCEDURE web.ppBorrador_Eliminar
    @CuentaId INT,
    @Tipo     NVARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM web.Borrador WHERE CuentaId = @CuentaId AND Tipo = @Tipo;
END;
GO

PRINT N'03 - Procedimientos del Portal creados.';
GO
