/* =============================================================================
   CONADIS.Web - 01: Base de datos y esquemas del Portal Ciudadano
   Ejecutar primero. Se puede ejecutar varias veces sin dañar nada.
   El portal guarda aquí SOLO lo propio del portal (SRS 3.8, esquema "web"):
   cuentas de los ciudadanos, preferencias de accesibilidad, borradores de
   solicitudes y eventos de seguridad. Los trámites viven en el Core y se
   consultan a través de Integración.
     web = cuentas, preferencias, borradores, recuperación de contraseña, eventos
     dbo = tabla de Log4Net
   ============================================================================= */
USE master;
GO

IF DB_ID(N'CONADIS_WebDB') IS NULL
BEGIN
    CREATE DATABASE CONADIS_WebDB COLLATE Modern_Spanish_CI_AI;
END
GO

USE CONADIS_WebDB;
GO

IF SCHEMA_ID(N'web') IS NULL EXEC(N'CREATE SCHEMA web AUTHORIZATION dbo;');
GO

PRINT N'01 - Base de datos CONADIS_WebDB y esquemas listos.';
GO
