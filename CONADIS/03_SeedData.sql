USE CONADIS_CoreDB;
GO

-- Roles Iniciales
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE NombreRol = 'Administrador')
    INSERT INTO dbo.Roles (NombreRol) VALUES ('Administrador');

IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE NombreRol = 'Evaluador')
    INSERT INTO dbo.Roles (NombreRol) VALUES ('Evaluador');

IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE NombreRol = 'AnalistaRegistro')
    INSERT INTO dbo.Roles (NombreRol) VALUES ('AnalistaRegistro');

-- Usuario Admin por defecto
IF NOT EXISTS (SELECT 1 FROM dbo.Usuarios WHERE Username = 'admin')
BEGIN
    INSERT INTO dbo.Usuarios (Username, PasswordHash, NombreCompleto, Correo, RolId, Activo)
    VALUES ('admin', 'admin123', 'Administrador del Sistema', 'admin@conadis.gob.do', 1, 1);
END;
GO