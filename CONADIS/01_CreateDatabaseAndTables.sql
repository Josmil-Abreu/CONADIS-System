USE master;
GO

-- Crear la Base de Datos si no existe
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'CONADIS_CoreDB')
BEGIN
    CREATE DATABASE CONADIS_CoreDB;
END
GO

USE CONADIS_CoreDB;
GO

-- 1. Tabla de Roles
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Roles]') AND type in (N'U'))
BEGIN
    CREATE TABLE dbo.Roles (
        RolId INT IDENTITY(1,1) PRIMARY KEY,
        NombreRol VARCHAR(50) NOT NULL UNIQUE
    );
END
GO

-- 2. Tabla de Usuarios
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Usuarios]') AND type in (N'U'))
BEGIN
    CREATE TABLE dbo.Usuarios (
        UsuarioId INT IDENTITY(1,1) PRIMARY KEY,
        Username VARCHAR(50) NOT NULL UNIQUE,
        PasswordHash VARCHAR(256) NOT NULL,
        NombreCompleto VARCHAR(100) NOT NULL,
        Correo VARCHAR(100) NOT NULL,
        RolId INT NOT NULL,
        Activo BIT DEFAULT 1 NOT NULL,
        FechaCreacion DATETIME DEFAULT GETDATE(),
        CONSTRAINT FK_Usuarios_Roles FOREIGN KEY (RolId) REFERENCES dbo.Roles(RolId)
    );
END
GO

-- 3. Tabla de Expedientes PCD
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Expedientes]') AND type in (N'U'))
BEGIN
    CREATE TABLE dbo.Expedientes (
        ExpedienteId INT IDENTITY(1,1) PRIMARY KEY,
        NumeroExpediente VARCHAR(20) NOT NULL UNIQUE,
        Cedula VARCHAR(11) NOT NULL,
        NombreSolicitante VARCHAR(100) NOT NULL,
        ApellidoSolicitante VARCHAR(100) NOT NULL,
        Estado VARCHAR(30) DEFAULT 'Solicitado' NOT NULL,
        FechaRegistro DATETIME DEFAULT GETDATE() NOT NULL
    );
END
GO

-- 4. Tabla para Log4Net (Exigencia del profesor)
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Log4NetLog]') AND type in (N'U'))
BEGIN
    CREATE TABLE dbo.Log4NetLog (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        [Date] DATETIME NOT NULL,
        [Thread] VARCHAR(255) NOT NULL,
        [Level] VARCHAR(50) NOT NULL,
        [Logger] VARCHAR(255) NOT NULL,
        [Message] VARCHAR(4000) NOT NULL,
        [Exception] VARCHAR(2000) NULL
    );
END
GO