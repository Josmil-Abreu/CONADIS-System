/* =============================================================================
   CONADIS.Integracion - 08: Reportes de Integración (cada transacción tiene su reporte)
   Toda llamada que pasa por Integración (portal, caja, Core, JCE, MSP, DGII, pasarela, correo...)
   queda en integ.Bitacora. Aquí: listado por período, comprobante de una transacción,
   pagos recibidos frente a aplicados y disponibilidad de los servicios externos.
   Los reportes los pide el sistema interno del Core con su API key (ámbito "reportes").
   ============================================================================= */
USE CONADIS_IntegracionDB;
GO

-- El Core (sistema interno del personal) puede pedir los reportes de Integración
UPDATE seg.ClienteApi SET Ambitos = N'firma reportes'   -- separados por espacio
WHERE Nombre = N'Core' AND Ambitos NOT LIKE N'%reportes%';
GO

CREATE OR ALTER PROCEDURE integ.ppRptTransacciones
    @Desde     DATE,
    @Hasta     DATE,
    @Servicio  VARCHAR(20)   = NULL,
    @Cliente   NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Desde > @Hasta THROW 56040, N'La fecha inicial no puede ser mayor que la final.', 1;
    SELECT BitacoraId, CAST(Fecha AS DATETIME2(0)) AS Fecha, Servicio, Operacion, Direccion, ISNULL(Cliente, N'-') AS Cliente,
           ISNULL(Usuario, N'-') AS Usuario, CAST(CASE WHEN Exito = 1 THEN N'Éxito' ELSE N'Fallo' END AS NVARCHAR(10)) AS Resultado,
           ISNULL(CodigoRespuesta, '-') AS CodigoRespuesta, ISNULL(LatenciaMs, 0) AS LatenciaMs,
           CAST(CASE WHEN DesdeCache = 1 THEN N'Sí' ELSE N'No' END AS NVARCHAR(2)) AS DesdeCache, ISNULL(Detalle, N'') AS Detalle
    FROM integ.Bitacora
    WHERE CAST(Fecha AS DATE) BETWEEN @Desde AND @Hasta
      AND (@Servicio IS NULL OR Servicio = @Servicio)
      AND (@Cliente IS NULL OR Cliente = @Cliente)
    ORDER BY BitacoraId;
END;
GO

CREATE OR ALTER PROCEDURE integ.ppRptComprobanteTransaccion
    @BitacoraId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM integ.Bitacora WHERE BitacoraId = @BitacoraId)
        THROW 56041, N'La transacción indicada no existe.', 1;
    SELECT BitacoraId, CAST(Fecha AS DATETIME2(3)) AS Fecha, Servicio, Operacion, Direccion, ISNULL(Cliente, N'-') AS Cliente,
           ISNULL(Usuario, N'-') AS Usuario, ISNULL(Proposito, N'-') AS Proposito,
           ISNULL(ReferenciaHash, '-') AS ReferenciaHash,                    -- la cédula nunca se guarda: solo su huella
           CAST(CASE WHEN Exito = 1 THEN N'Éxito' ELSE N'Fallo' END AS NVARCHAR(10)) AS Resultado, ISNULL(Resultado, '-') AS Estado,
           ISNULL(CodigoRespuesta, '-') AS CodigoRespuesta, ISNULL(LatenciaMs, 0) AS LatenciaMs,
           CAST(CASE WHEN DesdeCache = 1 THEN N'Sí' ELSE N'No' END AS NVARCHAR(2)) AS DesdeCache,
           ISNULL(CorrelacionId, '-') AS CorrelacionId, ISNULL(Detalle, N'') AS Detalle
    FROM integ.Bitacora WHERE BitacoraId = @BitacoraId;
END;
GO

-- Pagos recibidos de la Caja y del portal, y si el Core ya los aplicó
CREATE OR ALTER PROCEDURE integ.ppRptPagos
    @Desde DATE,
    @Hasta DATE
AS
BEGIN
    SET NOCOUNT ON;
    IF @Desde > @Hasta THROW 56040, N'La fecha inicial no puede ser mayor que la final.', 1;
    SELECT CONVERT(VARCHAR(36), p.TransaccionId) AS TransaccionId, CAST(p.FechaRecepcion AS DATETIME2(0)) AS FechaRecepcion,
           p.Origen, c.Nombre AS Cliente, ISNULL(p.NumeroComprobante, '-') AS NumeroComprobante, ISNULL(p.NumeroOrden, '-') AS NumeroOrden,
           p.Total, p.Estado, CAST(p.FechaConfirmacion AS DATETIME2(0)) AS FechaConfirmacion, ISNULL(p.Detalle, N'') AS Detalle
    FROM integ.PagoRecibido p
    INNER JOIN seg.ClienteApi c ON c.ClienteId = p.ClienteId
    WHERE CAST(p.FechaRecepcion AS DATE) BETWEEN @Desde AND @Hasta
    ORDER BY p.FechaRecepcion;
END;
GO

-- Disponibilidad y tiempos de cada servicio externo (para el SLA de interoperabilidad)
CREATE OR ALTER PROCEDURE integ.ppRptServicios
    @Desde DATE,
    @Hasta DATE
AS
BEGIN
    SET NOCOUNT ON;
    IF @Desde > @Hasta THROW 56040, N'La fecha inicial no puede ser mayor que la final.', 1;
    SELECT Servicio, COUNT(*) AS Llamadas, SUM(CASE WHEN Exito = 1 THEN 1 ELSE 0 END) AS Exitosas,
           SUM(CASE WHEN Exito = 0 THEN 1 ELSE 0 END) AS Fallidas,
           CAST(100.0 * SUM(CASE WHEN Exito = 1 THEN 1 ELSE 0 END) / COUNT(*) AS DECIMAL(5,2)) AS PorcentajeExito,
           CAST(AVG(CAST(ISNULL(LatenciaMs, 0) AS DECIMAL(18,2))) AS DECIMAL(18,2)) AS LatenciaPromedioMs,
           ISNULL(MAX(LatenciaMs), 0) AS LatenciaMaximaMs,
           SUM(CASE WHEN DesdeCache = 1 THEN 1 ELSE 0 END) AS DesdeCache
    FROM integ.Bitacora
    WHERE CAST(Fecha AS DATE) BETWEEN @Desde AND @Hasta
    GROUP BY Servicio
    ORDER BY Servicio;
END;
GO

PRINT N'08 - Procedimientos de reportes de Integración creados.';
GO
