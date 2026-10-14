/* =============================================================================
   CONADIS.Core - 10: Usuario de base de datos de la aplicación (mínimo privilegio)
   Como se vio en clase: con consultas directas la aplicación necesitaría permisos de
   lectura/escritura sobre las tablas (y con ellos se podría borrar toda la base).
   Aquí la aplicación SOLO puede ejecutar procedimientos almacenados.
   Los procedimientos pertenecen a dbo igual que las tablas, así que por "encadenamiento
   de propiedad" pueden leer y escribir las tablas sin que el usuario tenga ese permiso.
   ============================================================================= */
USE master;
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'conadis_core_app')
    CREATE LOGIN conadis_core_app
        WITH PASSWORD = N'CoreApp#2026!Cambiar', CHECK_POLICY = ON, DEFAULT_DATABASE = CONADIS_CoreDB;
GO

USE CONADIS_CoreDB;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'conadis_core_app')
    CREATE USER conadis_core_app FOR LOGIN conadis_core_app;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'rol_aplicacion_core' AND type = 'R')
    CREATE ROLE rol_aplicacion_core;
GO

-- Solo EXECUTE. Ningún SELECT/INSERT/UPDATE/DELETE directo sobre tablas.
GRANT EXECUTE ON SCHEMA::dbo   TO rol_aplicacion_core;
GRANT EXECUTE ON SCHEMA::seg   TO rol_aplicacion_core;
GRANT EXECUTE ON SCHEMA::cat   TO rol_aplicacion_core;
GRANT EXECUTE ON SCHEMA::core  TO rol_aplicacion_core;
GRANT EXECUTE ON SCHEMA::audit TO rol_aplicacion_core;
GO

ALTER ROLE rol_aplicacion_core ADD MEMBER conadis_core_app;
GO

PRINT N'10 - Usuario conadis_core_app creado con permiso solo de EXECUTE.';
GO