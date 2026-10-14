/* =============================================================================
   CONADIS.Integracion - 01: Base de datos y esquemas
   Ejecutar primero. Se puede ejecutar varias veces sin dañar nada.
   Cada módulo tiene su propia base de datos: Integración NO lee ni escribe
   la base del Core; se comunica con él solo por su API REST.
   Esquemas:
     seg   = sistemas cliente (API keys, convenios) y operadores de la consola
     cfg   = parámetros (tiempos de espera, reintentos, cortacircuito, caché)
     integ = cola de mensajes (outbox), bitácora de interoperabilidad, caché,
             cortacircuitos, pagos recibidos, idempotencia y alertas (SRS 3.8)
     dbo   = tabla de Log4Net
   ============================================================================= */
USE master;
GO

IF DB_ID(N'CONADIS_IntegracionDB') IS NULL
BEGIN
    -- Misma intercalación que el Core: español, sin distinguir mayúsculas ni acentos
    CREATE DATABASE CONADIS_IntegracionDB COLLATE Modern_Spanish_CI_AI;
END
GO

USE CONADIS_IntegracionDB;
GO

IF SCHEMA_ID(N'seg')   IS NULL EXEC(N'CREATE SCHEMA seg AUTHORIZATION dbo;');
IF SCHEMA_ID(N'cfg')   IS NULL EXEC(N'CREATE SCHEMA cfg AUTHORIZATION dbo;');
IF SCHEMA_ID(N'integ') IS NULL EXEC(N'CREATE SCHEMA integ AUTHORIZATION dbo;');
GO

PRINT N'01 - Base de datos CONADIS_IntegracionDB y esquemas listos.';
GO
