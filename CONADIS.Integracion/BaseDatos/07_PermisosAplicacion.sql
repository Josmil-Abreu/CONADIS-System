/* =============================================================================
   CONADIS.Integracion - 07: Usuario de base de datos de la aplicación (mínimo privilegio)
   Igual que en el Core: la aplicación SOLO puede ejecutar procedimientos almacenados.
   Ningún SELECT/INSERT/UPDATE/DELETE directo sobre las tablas.
   (En desarrollo con LocalDB se usa la autenticación de Windows; este usuario es para
   el despliegue en un SQL Server real.)
   ============================================================================= */
USE master;
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'conadis_integracion_app')
    CREATE LOGIN conadis_integracion_app
        WITH PASSWORD = N'IntegracionApp#2026!Cambiar', CHECK_POLICY = ON, DEFAULT_DATABASE = CONADIS_IntegracionDB;
GO

USE CONADIS_IntegracionDB;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'conadis_integracion_app')
    CREATE USER conadis_integracion_app FOR LOGIN conadis_integracion_app;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'rol_aplicacion_integracion' AND type = 'R')
    CREATE ROLE rol_aplicacion_integracion;
GO

GRANT EXECUTE ON SCHEMA::dbo   TO rol_aplicacion_integracion;
GRANT EXECUTE ON SCHEMA::seg   TO rol_aplicacion_integracion;
GRANT EXECUTE ON SCHEMA::cfg   TO rol_aplicacion_integracion;
GRANT EXECUTE ON SCHEMA::integ TO rol_aplicacion_integracion;
GO

ALTER ROLE rol_aplicacion_integracion ADD MEMBER conadis_integracion_app;
GO

PRINT N'07 - Usuario conadis_integracion_app creado con permiso solo de EXECUTE.';
GO
