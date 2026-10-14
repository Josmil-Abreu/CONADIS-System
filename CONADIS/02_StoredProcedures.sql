USE CONADIS_CoreDB;
GO

-- SP Autenticar Usuario
CREATE OR ALTER PROCEDURE dbo.sp_AutenticarUsuario
    @Username VARCHAR(50),
    @PasswordHash VARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT u.UsuarioId, u.Username, u.NombreCompleto, u.Correo, r.NombreRol
    FROM dbo.Usuarios u
    INNER JOIN dbo.Roles r ON u.RolId = r.RolId
    WHERE u.Username = @Username 
      AND u.PasswordHash = @PasswordHash
      AND u.Activo = 1;
END;
GO

-- SP Insertar Logs (Log4Net)
CREATE OR ALTER PROCEDURE dbo.sp_InsertLog
    @log_date DATETIME,
    @thread VARCHAR(255),
    @level VARCHAR(50),
    @logger VARCHAR(255),
    @message VARCHAR(4000),
    @exception VARCHAR(2000)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Log4NetLog ([Date], [Thread], [Level], [Logger], [Message], [Exception])
    VALUES (@log_date, @thread, @level, @logger, @message, @exception);
END;
GO