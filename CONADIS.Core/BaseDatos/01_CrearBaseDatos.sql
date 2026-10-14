/* =============================================================================
   CONADIS.Core - 01: Base de datos y esquemas
   Ejecutar primero. Se puede ejecutar varias veces sin dañar nada.
   Esquemas (SRS 3.8):
     seg   = usuarios y roles (control de acceso)
     cat   = catálogos y parámetros (CIF, centros, baremo, tasas)
     core  = personas, expedientes, evaluaciones, dictámenes, carnets, órdenes
     audit = bitácora de auditoría inmutable
     dbo   = tabla de Log4Net
   ============================================================================= */
USE master;
GO

IF DB_ID(N'CONADIS_CoreDB') IS NULL
BEGIN
    -- Intercalación exigida por el SRS 3.8: español, sin distinguir mayúsculas ni acentos
    CREATE DATABASE CONADIS_CoreDB COLLATE Modern_Spanish_CI_AI;
END
GO

USE CONADIS_CoreDB;
GO

IF SCHEMA_ID(N'seg')   IS NULL EXEC(N'CREATE SCHEMA seg AUTHORIZATION dbo;');
IF SCHEMA_ID(N'cat')   IS NULL EXEC(N'CREATE SCHEMA cat AUTHORIZATION dbo;');
IF SCHEMA_ID(N'core')  IS NULL EXEC(N'CREATE SCHEMA core AUTHORIZATION dbo;');
IF SCHEMA_ID(N'audit') IS NULL EXEC(N'CREATE SCHEMA audit AUTHORIZATION dbo;');
GO

PRINT N'01 - Base de datos CONADIS_CoreDB y esquemas listos.';
GO