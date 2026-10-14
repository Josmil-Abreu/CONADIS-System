/* =============================================================================
   CONADIS.Integracion - 01: Base de datos y esquemas
   Ejecutar primero. Se puede ejecutar varias veces sin dañar nada.

   La capa de Integración (SND - Sistema Nervioso Digital) tiene su PROPIA base
   de datos. Nunca lee ni escribe en la base de otro módulo: solo llama a sus APIs.

   Esquemas:
     seg = clientes autorizados (API keys de WEB y CAJA)
     cat = parámetros de configuración
     snd = cola de mensajes (outbox), idempotencia, pagos recibidos,
           estado de los servicios (cortacircuito), caché y bitácora
     dbo = tabla de Log4Net
   ============================================================================= */
USE master;
GO

IF DB_ID(N'CONADIS_IntegracionDB') IS NULL
BEGIN
    -- Español, sin distinguir mayúsculas ni acentos
    CREATE DATABASE CONADIS_IntegracionDB COLLATE Modern_Spanish_CI_AI;
END
GO

USE CONADIS_IntegracionDB;
GO

IF SCHEMA_ID(N'seg') IS NULL EXEC(N'CREATE SCHEMA seg AUTHORIZATION dbo;');
IF SCHEMA_ID(N'cat') IS NULL EXEC(N'CREATE SCHEMA cat AUTHORIZATION dbo;');
IF SCHEMA_ID(N'snd') IS NULL EXEC(N'CREATE SCHEMA snd AUTHORIZATION dbo;');
GO

PRINT N'01 - Base de datos CONADIS_IntegracionDB y esquemas listos.';
GO
