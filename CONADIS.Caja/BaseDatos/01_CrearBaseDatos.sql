/* =============================================================================
   CONADIS.Caja - 01: Base de datos local de la Caja
   La Caja es una aplicación Windows para los empleados que DEBE funcionar sin
   conexión (notas de clase): todo cobro se guarda primero aquí y luego se
   sincroniza con Integración. Si la red o el Core se caen, se sigue cobrando.
     seg  = cajeros y supervisores de caja
     caja = tasas (copia local), cajas, sesiones, facturas e-CF, pagos, arqueos
     dbo  = tabla de Log4Net
   ============================================================================= */
USE master;
GO

IF DB_ID(N'CONADIS_CajaDB') IS NULL
BEGIN
    CREATE DATABASE CONADIS_CajaDB COLLATE Modern_Spanish_CI_AI;
END
GO

USE CONADIS_CajaDB;
GO

IF SCHEMA_ID(N'seg')  IS NULL EXEC(N'CREATE SCHEMA seg AUTHORIZATION dbo;');
IF SCHEMA_ID(N'caja') IS NULL EXEC(N'CREATE SCHEMA caja AUTHORIZATION dbo;');
GO

PRINT N'01 - Base de datos CONADIS_CajaDB y esquemas listos.';
GO
