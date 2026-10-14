/* =============================================================================
   CONADIS.Caja - 07: Administración de usuarios de la caja
   Solo un usuario con rol Administrador puede crear, editar, desactivar o desbloquear
   cajeros y supervisores. Nadie puede cambiar su propio rol ni desactivarse a sí mismo
   (segregación de funciones). Los usuarios no se borran: se desactivan (RINV-04).
   La contraseña llega ya cifrada con PBKDF2 desde C#.
   ============================================================================= */
USE CONADIS_CajaDB;
GO

CREATE OR ALTER PROCEDURE seg.ppUsuario_Listar
AS
BEGIN
    SET NOCOUNT ON;
    SELECT UsuarioId, Username, NombreCompleto, Rol, Activo, BloqueadoHasta, UltimoAcceso
    FROM seg.Usuario ORDER BY Activo DESC, Rol, Username;
END;
GO

-- @UsuarioId NULL = crear (exige @PasswordHash). Devuelve el UsuarioId.
CREATE OR ALTER PROCEDURE seg.ppUsuario_Guardar
    @AdminId         INT,
    @UsuarioId       INT = NULL,
    @Username        NVARCHAR(50),
    @NombreCompleto  NVARCHAR(150),
    @Rol             NVARCHAR(20),
    @Activo          BIT = 1,
    @PasswordHash    VARCHAR(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Admin NVARCHAR(50) = (SELECT Username FROM seg.Usuario WHERE UsuarioId = @AdminId AND Rol = N'Administrador' AND Activo = 1);
    IF @Admin IS NULL THROW 58030, N'Solo el administrador de caja puede gestionar usuarios.', 1;
    IF @Rol NOT IN (N'Cajero', N'SupervisorCaja', N'Administrador') THROW 58031, N'Rol no válido.', 1;
    IF LEN(LTRIM(ISNULL(@Username, N''))) < 3 THROW 58032, N'El usuario debe tener al menos 3 caracteres.', 1;
    IF LEN(LTRIM(ISNULL(@NombreCompleto, N''))) < 3 THROW 58033, N'Escriba el nombre completo.', 1;
    IF @UsuarioId = @AdminId AND (@Activo = 0 OR @Rol <> N'Administrador')
        THROW 58034, N'No puede desactivarse ni quitarse el rol de administrador a sí mismo.', 1;

    DECLARE @Detalle NVARCHAR(500);
    BEGIN TRANSACTION;
        IF @UsuarioId IS NULL
        BEGIN
            IF @PasswordHash IS NULL THROW 58035, N'Indique la contraseña inicial.', 1;
            IF EXISTS (SELECT 1 FROM seg.Usuario WHERE Username = @Username) THROW 58036, N'Ya existe un usuario con ese nombre.', 1;
            INSERT INTO seg.Usuario (Username, PasswordHash, NombreCompleto, Rol, Activo)
            VALUES (LTRIM(RTRIM(@Username)), @PasswordHash, LTRIM(RTRIM(@NombreCompleto)), @Rol, @Activo);
            SET @UsuarioId = SCOPE_IDENTITY();
            SET @Detalle = CONCAT(N'Usuario ', @Username, N' creado con rol ', @Rol, N'.');
        END
        ELSE
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM seg.Usuario WHERE UsuarioId = @UsuarioId) THROW 58037, N'El usuario no existe.', 1;
            IF EXISTS (SELECT 1 FROM seg.Usuario WHERE Username = @Username AND UsuarioId <> @UsuarioId)
                THROW 58036, N'Ya existe un usuario con ese nombre.', 1;
            UPDATE seg.Usuario
            SET Username = LTRIM(RTRIM(@Username)), NombreCompleto = LTRIM(RTRIM(@NombreCompleto)), Rol = @Rol, Activo = @Activo,
                PasswordHash = COALESCE(@PasswordHash, PasswordHash),
                IntentosFallidos = CASE WHEN @PasswordHash IS NULL THEN IntentosFallidos ELSE 0 END,
                BloqueadoHasta = CASE WHEN @PasswordHash IS NULL THEN BloqueadoHasta ELSE NULL END
            WHERE UsuarioId = @UsuarioId;
            SET @Detalle = CONCAT(N'Usuario ', @Username, N' modificado (rol ', @Rol, N', ', IIF(@Activo = 1, N'activo', N'inactivo'),
                                  IIF(@PasswordHash IS NULL, N'', N', contraseña restablecida'), N').');
        END
        EXEC caja.ppEvento_Registrar @Usuario = @Admin, @Accion = N'UsuarioGuardado', @Detalle = @Detalle;
    COMMIT TRANSACTION;
    SELECT @UsuarioId AS UsuarioId;
END;
GO

CREATE OR ALTER PROCEDURE seg.ppUsuario_Desbloquear
    @AdminId   INT,
    @UsuarioId INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Admin NVARCHAR(50) = (SELECT Username FROM seg.Usuario WHERE UsuarioId = @AdminId AND Rol = N'Administrador' AND Activo = 1);
    IF @Admin IS NULL THROW 58030, N'Solo el administrador de caja puede gestionar usuarios.', 1;
    UPDATE seg.Usuario SET IntentosFallidos = 0, BloqueadoHasta = NULL WHERE UsuarioId = @UsuarioId;
    DECLARE @Detalle NVARCHAR(500) = CONCAT(N'Usuario ', @UsuarioId, N' desbloqueado.');
    EXEC caja.ppEvento_Registrar @Usuario = @Admin, @Accion = N'UsuarioDesbloqueado', @Detalle = @Detalle;
END;
GO

PRINT N'07 - Administración de usuarios de la caja creada.';
GO
