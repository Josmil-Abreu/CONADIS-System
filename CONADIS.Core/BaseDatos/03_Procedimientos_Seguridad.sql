/* =============================================================================
   CONADIS.Core - 03: Procedimientos de Log4Net, auditoría y seguridad
   Convención del curso: prefijo "pp" (no "sp", que SQL Server reserva para
   procedimientos del sistema y obliga a buscarlos primero en master).
   Errores de negocio: THROW 50001-59999 con mensaje para el usuario.
   ============================================================================= */
USE CONADIS_CoreDB;
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

/* ----------------------------- AUDITORÍA ----------------------------------- */
-- Inserta un evento encadenado con el hash del anterior. Cualquier alteración rompe la cadena.
CREATE OR ALTER PROCEDURE audit.ppEvento_Registrar
    @Usuario    NVARCHAR(100),
    @Accion     NVARCHAR(100),
    @Entidad    NVARCHAR(50),
    @EntidadId  NVARCHAR(50)   = NULL,
    @Detalle    NVARCHAR(2000) = NULL,
    @Ip         VARCHAR(45)    = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Fecha DATETIMEOFFSET(3) = SYSDATETIMEOFFSET();
    DECLARE @Anterior VARBINARY(32);
    DECLARE @Hash VARBINARY(32);

    BEGIN TRANSACTION;
        -- Bloquea el último evento para que dos inserciones simultáneas no usen el mismo "anterior"
        SELECT TOP (1) @Anterior = Hash
        FROM audit.Evento WITH (UPDLOCK, HOLDLOCK)
        ORDER BY EventoId DESC;

        SET @Hash = HASHBYTES('SHA2_256', CONCAT(
            CONVERT(VARCHAR(64), @Anterior, 2), N'|',
            CONVERT(NVARCHAR(40), @Fecha, 127), N'|',
            @Usuario, N'|', @Accion, N'|', @Entidad, N'|', @EntidadId, N'|', @Detalle));

        INSERT INTO audit.Evento (Fecha, Usuario, Ip, Accion, Entidad, EntidadId, Detalle, HashAnterior, Hash)
        VALUES (@Fecha, @Usuario, @Ip, @Accion, @Entidad, @EntidadId, @Detalle, @Anterior, @Hash);
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE audit.ppEvento_Listar
    @Desde    DATETIMEOFFSET(0),
    @Hasta    DATETIMEOFFSET(0),
    @Usuario  NVARCHAR(100) = NULL,
    @Entidad  NVARCHAR(50)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (2000) EventoId, Fecha, Usuario, Ip, Accion, Entidad, EntidadId, Detalle
    FROM audit.Evento
    WHERE Fecha >= @Desde AND Fecha < @Hasta
      AND (@Usuario IS NULL OR Usuario = @Usuario)
      AND (@Entidad IS NULL OR Entidad = @Entidad)
    ORDER BY EventoId DESC;
END;
GO

-- Recalcula la cadena y devuelve los eventos cuyo hash no coincide (vacío = bitácora íntegra)
CREATE OR ALTER PROCEDURE audit.ppEvento_VerificarCadena
AS
BEGIN
    SET NOCOUNT ON;
    WITH Cadena AS
    (
        SELECT EventoId, Fecha, Usuario, Accion, Entidad, EntidadId, Detalle, HashAnterior, Hash,
               LAG(Hash) OVER (ORDER BY EventoId) AS HashPrevioReal
        FROM audit.Evento
    )
    SELECT EventoId, Fecha, Usuario, Accion, N'Hash alterado o cadena rota' AS Problema
    FROM Cadena
    WHERE ISNULL(HashAnterior, 0x) <> ISNULL(HashPrevioReal, 0x)
       OR Hash <> HASHBYTES('SHA2_256', CONCAT(
              CONVERT(VARCHAR(64), HashAnterior, 2), N'|',
              CONVERT(NVARCHAR(40), Fecha, 127), N'|',
              Usuario, N'|', Accion, N'|', Entidad, N'|', EntidadId, N'|', Detalle))
    ORDER BY EventoId;
END;
GO

/* ----------------------------- ROLES Y USUARIOS ---------------------------- */
CREATE OR ALTER PROCEDURE seg.ppRol_Listar
AS
BEGIN
    SET NOCOUNT ON;
    SELECT RolId, Nombre, Descripcion FROM seg.Rol ORDER BY Nombre;
END;
GO

-- Devuelve el hash guardado: la contraseña se verifica en C# (PBKDF2 con salt por usuario).
CREATE OR ALTER PROCEDURE seg.ppUsuario_ObtenerParaLogin
    @Username NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT u.UsuarioId, u.Username, u.PasswordHash, u.NombreCompleto, u.Correo,
           r.Nombre AS Rol, u.Disciplina, u.CentroId, u.Activo, u.IntentosFallidos, u.BloqueadoHasta
    FROM seg.Usuario u
    INNER JOIN seg.Rol r ON r.RolId = u.RolId
    WHERE u.Username = @Username;
END;
GO

-- 5 intentos fallidos = bloqueo de 15 minutos (RNF-SEG-04). Los valores vienen de cat.Parametro.
CREATE OR ALTER PROCEDURE seg.ppUsuario_RegistrarIntentoFallido
    @UsuarioId       INT,
    @Ip              VARCHAR(45) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Max INT = ISNULL(TRY_CAST((SELECT Valor FROM cat.Parametro WHERE Clave = 'LoginIntentosMaximos') AS INT), 5);
    DECLARE @Minutos INT = ISNULL(TRY_CAST((SELECT Valor FROM cat.Parametro WHERE Clave = 'LoginMinutosBloqueo') AS INT), 15);
    DECLARE @Username NVARCHAR(50);
    DECLARE @Intentos INT;

    BEGIN TRANSACTION;
        UPDATE seg.Usuario
        SET @Intentos = IntentosFallidos = IntentosFallidos + 1,
            @Username = Username
        WHERE UsuarioId = @UsuarioId;

        IF @Intentos >= @Max
        BEGIN
            UPDATE seg.Usuario
            SET BloqueadoHasta = DATEADD(MINUTE, @Minutos, SYSDATETIMEOFFSET()),
                IntentosFallidos = 0
            WHERE UsuarioId = @UsuarioId;

            EXEC audit.ppEvento_Registrar @Usuario = @Username, @Accion = N'CuentaBloqueada',
                 @Entidad = N'Usuario', @EntidadId = @UsuarioId, @Detalle = N'Bloqueo por intentos fallidos.', @Ip = @Ip;
        END
    COMMIT TRANSACTION;

    SELECT BloqueadoHasta FROM seg.Usuario WHERE UsuarioId = @UsuarioId;
END;
GO

CREATE OR ALTER PROCEDURE seg.ppUsuario_RegistrarAccesoExitoso
    @UsuarioId  INT,
    @Ip         VARCHAR(45) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Username NVARCHAR(50);

    UPDATE seg.Usuario
    SET IntentosFallidos = 0, BloqueadoHasta = NULL, UltimoAcceso = SYSDATETIMEOFFSET(),
        @Username = Username
    WHERE UsuarioId = @UsuarioId;

    EXEC audit.ppEvento_Registrar @Usuario = @Username, @Accion = N'InicioSesion',
         @Entidad = N'Usuario', @EntidadId = @UsuarioId, @Ip = @Ip;
END;
GO

CREATE OR ALTER PROCEDURE seg.ppUsuario_Listar
    @Texto NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT u.UsuarioId, u.Username, u.NombreCompleto, u.Correo, r.Nombre AS Rol, u.Disciplina,
           c.Nombre AS Centro, u.Activo, u.BloqueadoHasta, u.UltimoAcceso
    FROM seg.Usuario u
    INNER JOIN seg.Rol r ON r.RolId = u.RolId
    LEFT JOIN cat.CentroValoracion c ON c.CentroId = u.CentroId
    WHERE @Texto IS NULL OR u.Username LIKE N'%' + @Texto + N'%' OR u.NombreCompleto LIKE N'%' + @Texto + N'%'
    ORDER BY u.NombreCompleto;
END;
GO

CREATE OR ALTER PROCEDURE seg.ppUsuario_Obtener
    @UsuarioId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT u.UsuarioId, u.Username, u.NombreCompleto, u.Correo, u.RolId, r.Nombre AS Rol,
           u.Disciplina, u.CentroId, u.Activo, u.BloqueadoHasta
    FROM seg.Usuario u
    INNER JOIN seg.Rol r ON r.RolId = u.RolId
    WHERE u.UsuarioId = @UsuarioId;
END;
GO

CREATE OR ALTER PROCEDURE seg.ppUsuario_Guardar
    @UsuarioId      INT = NULL,          -- NULL = crear
    @Username       NVARCHAR(50),
    @PasswordHash   VARCHAR(200) = NULL, -- obligatorio al crear
    @NombreCompleto NVARCHAR(150),
    @Correo         NVARCHAR(150),
    @RolId          INT,
    @Disciplina     NVARCHAR(20) = NULL,
    @CentroId       INT = NULL,
    @Activo         BIT = 1,
    @UsuarioAccion  NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Rol NVARCHAR(50) = (SELECT Nombre FROM seg.Rol WHERE RolId = @RolId);
    IF @Rol IS NULL
        THROW 51001, N'El rol seleccionado no existe.', 1;
    IF @Rol = N'Evaluador' AND (@Disciplina IS NULL OR @CentroId IS NULL)
        THROW 51002, N'Un evaluador debe tener disciplina y centro de valoración.', 1;
    IF @Rol <> N'Evaluador'
        SELECT @Disciplina = NULL, @CentroId = NULL;
    IF EXISTS (SELECT 1 FROM seg.Usuario WHERE Username = @Username AND (@UsuarioId IS NULL OR UsuarioId <> @UsuarioId))
        THROW 51003, N'Ya existe un usuario con ese nombre de usuario.', 1;

    BEGIN TRANSACTION;
        IF @UsuarioId IS NULL
        BEGIN
            IF @PasswordHash IS NULL
                THROW 51004, N'La contraseña es obligatoria al crear el usuario.', 1;

            INSERT INTO seg.Usuario (Username, PasswordHash, NombreCompleto, Correo, RolId, Disciplina, CentroId, Activo)
            VALUES (@Username, @PasswordHash, @NombreCompleto, @Correo, @RolId, @Disciplina, @CentroId, @Activo);
            SET @UsuarioId = SCOPE_IDENTITY();

            EXEC audit.ppEvento_Registrar @Usuario = @UsuarioAccion, @Accion = N'UsuarioCreado',
                 @Entidad = N'Usuario', @EntidadId = @UsuarioId, @Detalle = @Username;
        END
        ELSE
        BEGIN
            UPDATE seg.Usuario
            SET Username = @Username, NombreCompleto = @NombreCompleto, Correo = @Correo, RolId = @RolId,
                Disciplina = @Disciplina, CentroId = @CentroId, Activo = @Activo
            WHERE UsuarioId = @UsuarioId;

            IF @@ROWCOUNT = 0 THROW 51005, N'El usuario no existe.', 1;

            EXEC audit.ppEvento_Registrar @Usuario = @UsuarioAccion, @Accion = N'UsuarioModificado',
                 @Entidad = N'Usuario', @EntidadId = @UsuarioId, @Detalle = @Username;
        END
    COMMIT TRANSACTION;

    SELECT @UsuarioId AS UsuarioId;
END;
GO

CREATE OR ALTER PROCEDURE seg.ppUsuario_CambiarContrasena
    @UsuarioId      INT,
    @PasswordHash   VARCHAR(200),
    @UsuarioAccion  NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE seg.Usuario
    SET PasswordHash = @PasswordHash, IntentosFallidos = 0, BloqueadoHasta = NULL
    WHERE UsuarioId = @UsuarioId;

    IF @@ROWCOUNT = 0 THROW 51005, N'El usuario no existe.', 1;

    EXEC audit.ppEvento_Registrar @Usuario = @UsuarioAccion, @Accion = N'ContrasenaCambiada',
         @Entidad = N'Usuario', @EntidadId = @UsuarioId;
END;
GO

CREATE OR ALTER PROCEDURE seg.ppUsuario_Desbloquear
    @UsuarioId      INT,
    @UsuarioAccion  NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE seg.Usuario SET IntentosFallidos = 0, BloqueadoHasta = NULL WHERE UsuarioId = @UsuarioId;
    EXEC audit.ppEvento_Registrar @Usuario = @UsuarioAccion, @Accion = N'UsuarioDesbloqueado',
         @Entidad = N'Usuario', @EntidadId = @UsuarioId;
END;
GO

PRINT N'03 - Procedimientos de seguridad, log y auditoría creados.';
GO