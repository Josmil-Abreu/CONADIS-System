/* =============================================================================
   CONADIS.Caja - 05: Usuario de base de datos de la Caja (mínimo privilegio)
   ============================================================================= */
USE master;
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'conadis_caja_app')
    CREATE LOGIN conadis_caja_app WITH PASSWORD = N'CajaApp#2026!Cambiar', CHECK_POLICY = ON, DEFAULT_DATABASE = CONADIS_CajaDB;
GO

USE CONADIS_CajaDB;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'conadis_caja_app')
    CREATE USER conadis_caja_app FOR LOGIN conadis_caja_app;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'rol_aplicacion_caja' AND type = 'R')
    CREATE ROLE rol_aplicacion_caja;
GO

GRANT EXECUTE ON SCHEMA::dbo  TO rol_aplicacion_caja;
GRANT EXECUTE ON SCHEMA::seg  TO rol_aplicacion_caja;
GRANT EXECUTE ON SCHEMA::caja TO rol_aplicacion_caja;
GO

ALTER ROLE rol_aplicacion_caja ADD MEMBER conadis_caja_app;
GO

PRINT N'05 - Usuario conadis_caja_app creado con permiso solo de EXECUTE.';
GO
