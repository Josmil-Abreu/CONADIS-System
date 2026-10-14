/* =============================================================================
   CONADIS.Integracion - 07: Procedimientos de reportes (ppRpt...)
   Cada uno alimenta un reporte RDLC: devuelven filas planas, listas para un DataSet.
   Fechas: @Desde inclusiva, @Hasta exclusiva (UTC).
   ============================================================================= */
USE CONADIS_IntegracionDB;
GO

-- Necesarios para índices filtrados y JSON (Visual Studio ya los trae activos; sqlcmd no)
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- 1) Estado de la cola: cuántos mensajes hay por destino, tipo y estado.
CREATE OR ALTER PROCEDURE snd.ppRpt_MensajesPorEstado
    @Desde DATETIME2(3),
    @Hasta DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.Codigo AS Servicio, m.Tipo, m.Estado,
           COUNT(*)                   AS Cantidad,
           AVG(CAST(m.Intentos AS DECIMAL(10,2))) AS PromedioIntentos,
           AVG(CASE WHEN m.FechaEntrega IS NOT NULL
                    THEN CAST(DATEDIFF(SECOND, m.FechaCreacion, m.FechaEntrega) AS DECIMAL(18,2)) END)
                                      AS PromedioSegundosEntrega
    FROM snd.Mensaje m
    INNER JOIN snd.Servicio s ON s.ServicioId = m.ServicioDestinoId
    WHERE m.FechaCreacion >= @Desde AND m.FechaCreacion < @Hasta
    GROUP BY s.Codigo, m.Tipo, m.Estado
    ORDER BY s.Codigo, m.Tipo, m.Estado;
END;
GO

-- 2) Mensajes muertos: lo que necesita revisión manual.
CREATE OR ALTER PROCEDURE snd.ppRpt_MensajesMuertos
AS
BEGIN
    SET NOCOUNT ON;
    SELECT m.MensajeId, m.MensajeGuid, s.Codigo AS Servicio, c.Codigo AS Cliente, m.Tipo,
           m.Intentos, m.FechaCreacion, m.UltimoError
    FROM snd.Mensaje m
    INNER JOIN snd.Servicio s ON s.ServicioId = m.ServicioDestinoId
    LEFT  JOIN seg.ClienteApi c ON c.ClienteId = m.ClienteId
    WHERE m.Estado = N'Muerto'
    ORDER BY m.FechaCreacion;
END;
GO

-- 3) Pagos recibidos por día, origen y medio de pago.
CREATE OR ALTER PROCEDURE snd.ppRpt_PagosRecibidos
    @Desde  DATETIME2(3),
    @Hasta  DATETIME2(3),
    @Origen NVARCHAR(10) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CAST(p.FechaCobro AS DATE) AS Fecha, p.Origen, p.MedioPago, p.Estado,
           COUNT(*)     AS Cantidad,
           SUM(p.Total) AS Total
    FROM snd.PagoRecibido p
    WHERE p.FechaCobro >= @Desde AND p.FechaCobro < @Hasta
      AND (@Origen IS NULL OR p.Origen = @Origen)
    GROUP BY CAST(p.FechaCobro AS DATE), p.Origen, p.MedioPago, p.Estado
    ORDER BY Fecha, p.Origen, p.MedioPago, p.Estado;
END;
GO

-- 4) Disponibilidad de cada servicio según las llamadas salientes de la bitácora.
CREATE OR ALTER PROCEDURE snd.ppRpt_Disponibilidad
    @Desde DATETIME2(3),
    @Hasta DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.Codigo AS Servicio, s.Nombre,
           COUNT(b.BitacoraId)                                  AS Llamadas,
           SUM(CASE WHEN b.Exitoso = 1 THEN 1 ELSE 0 END)       AS Exitosas,
           SUM(CASE WHEN b.Exitoso = 0 THEN 1 ELSE 0 END)       AS Fallidas,
           CAST(CASE WHEN COUNT(b.BitacoraId) = 0 THEN NULL
                     ELSE 100.0 * SUM(CASE WHEN b.Exitoso = 1 THEN 1 ELSE 0 END) / COUNT(b.BitacoraId)
                END AS DECIMAL(5,2))                             AS PorcentajeDisponibilidad,
           AVG(CAST(b.DuracionMs AS DECIMAL(18,2)))              AS PromedioMs,
           MAX(b.DuracionMs)                                     AS MaximoMs,
           s.EstadoCircuito                                      AS EstadoActual
    FROM snd.Servicio s
    LEFT JOIN snd.Bitacora b
           ON b.ServicioId = s.ServicioId
          AND b.Direccion = N'Saliente'
          AND b.Fecha >= @Desde AND b.Fecha < @Hasta
    GROUP BY s.Codigo, s.Nombre, s.EstadoCircuito
    ORDER BY s.Codigo;
END;
GO

-- 5) Errores agrupados por servicio, operación y código de resultado.
CREATE OR ALTER PROCEDURE snd.ppRpt_ErroresPorServicio
    @Desde DATETIME2(3),
    @Hasta DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ISNULL(s.Codigo, N'(API)') AS Servicio, b.Direccion, b.Operacion, b.CodigoResultado,
           COUNT(*)    AS Cantidad,
           MIN(b.Fecha) AS Primero,
           MAX(b.Fecha) AS Ultimo
    FROM snd.Bitacora b
    LEFT JOIN snd.Servicio s ON s.ServicioId = b.ServicioId
    WHERE b.Exitoso = 0
      AND b.Fecha >= @Desde AND b.Fecha < @Hasta
    GROUP BY s.Codigo, b.Direccion, b.Operacion, b.CodigoResultado
    ORDER BY Cantidad DESC;
END;
GO

-- 6) Uso de la API por cliente (WEB, CAJA).
CREATE OR ALTER PROCEDURE snd.ppRpt_UsoPorCliente
    @Desde DATETIME2(3),
    @Hasta DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT c.Codigo AS Cliente, c.Nombre,
           COUNT(b.BitacoraId)                            AS Peticiones,
           SUM(CASE WHEN b.Exitoso = 0 THEN 1 ELSE 0 END) AS Errores,
           AVG(CAST(b.DuracionMs AS DECIMAL(18,2)))        AS PromedioMs
    FROM seg.ClienteApi c
    LEFT JOIN snd.Bitacora b
           ON b.ClienteId = c.ClienteId
          AND b.Direccion = N'Entrante'
          AND b.Fecha >= @Desde AND b.Fecha < @Hasta
    GROUP BY c.Codigo, c.Nombre
    ORDER BY c.Codigo;
END;
GO

PRINT N'07 - Procedimientos de reportes listos.';
GO
