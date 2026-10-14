/* =============================================================================
   CONADIS.Web - 05: Usuario de base de datos del portal (mínimo privilegio)
   El portal SOLO puede ejecutar procedimientos almacenados.
   ============================================================================= */
USE master;
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'conadis_web_app')
    CREATE LOGIN conadis_web_app WITH PASSWORD = N'WebApp#2026!Cambiar', CHECK_POLICY = ON, DEFAULT_DATABASE = CONADIS_WebDB;
GO

USE CONADIS_WebDB;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'conadis_web_app')
    CREATE USER conadis_web_app FOR LOGIN conadis_web_app;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'rol_aplicacion_web' AND type = 'R')
    CREATE ROLE rol_aplicacion_web;
GO

GRANT EXECUTE ON SCHEMA::dbo TO rol_aplicacion_web;
GRANT EXECUTE ON SCHEMA::web TO rol_aplicacion_web;
GO

ALTER ROLE rol_aplicacion_web ADD MEMBER conadis_web_app;
GO

PRINT N'05 - Usuario conadis_web_app creado con permiso solo de EXECUTE.';
GO
