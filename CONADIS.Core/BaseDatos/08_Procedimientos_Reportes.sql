/* =============================================================================
   CONADIS.Core - 08: Procedimientos que alimentan los reportes RDLC
   Cada reporte tiene su propio procedimiento (como ppGetData en clase):
   el código C# lo ejecuta, llena un DataTable y se lo entrega al ReportViewer.
   ============================================================================= */
USE CONADIS_CoreDB;
GO

-- Reporte 1: listado operativo de expedientes por estado
CREATE OR ALTER PROCEDURE core.ppRptExpedientesPorEstado
    @Desde   DATE,
    @Hasta   DATE,
    @Estado  NVARCHAR(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT e.NumeroExpediente, p.Cedula, CONCAT(p.Nombres, N' ', p.Apellidos) AS NombreCompleto,
           e.Estado, ISNULL(e.TipoDiscapacidad, N'-') AS TipoDiscapacidad, ISNULL(e.GradoDiscapacidad, N'-') AS GradoDiscapacidad,
           ISNULL(p.Provincia, N'Sin dato') AS Provincia, ISNULL(c.Nombre, N'Sin asignar') AS Centro,
           CAST(e.FechaCreacion AS DATE) AS FechaCreacion, CAST(e.FechaActualizacion AS DATE) AS FechaActualizacion
    FROM core.Expediente e
    INNER JOIN core.Persona p ON p.PersonaId = e.PersonaId
    LEFT JOIN cat.CentroValoracion c ON c.CentroId = e.CentroId
    WHERE CAST(e.FechaCreacion AS DATE) BETWEEN @Desde AND @Hasta
      AND (@Estado IS NULL OR e.Estado = @Estado)
    ORDER BY e.Estado, e.NumeroExpediente;
END;
GO

-- Reporte 2: estadística anonimizada (ROT-01). Celdas con menos de 10 personas se agrupan en "Otros".
CREATE OR ALTER PROCEDURE core.ppRptEstadisticaAnonimizada
    @Desde  DATE,
    @Hasta  DATE
AS
BEGIN
    SET NOCOUNT ON;
    WITH Base AS
    (
        SELECT ISNULL(p.Provincia, N'Sin dato') AS Provincia,
               CASE p.Sexo WHEN 1 THEN N'Masculino' WHEN 2 THEN N'Femenino' ELSE N'Sin dato' END AS Sexo,
               CASE WHEN p.FechaNacimiento IS NULL THEN N'Sin dato'
                    WHEN DATEDIFF(YEAR, p.FechaNacimiento, @Hasta) < 18 THEN N'0-17'
                    WHEN DATEDIFF(YEAR, p.FechaNacimiento, @Hasta) < 30 THEN N'18-29'
                    WHEN DATEDIFF(YEAR, p.FechaNacimiento, @Hasta) < 45 THEN N'30-44'
                    WHEN DATEDIFF(YEAR, p.FechaNacimiento, @Hasta) < 60 THEN N'45-59'
                    ELSE N'60+' END AS RangoEdad,
               e.TipoDiscapacidad, e.GradoDiscapacidad
        FROM core.Expediente e
        INNER JOIN core.Persona p ON p.PersonaId = e.PersonaId
        WHERE e.GradoDiscapacidad IS NOT NULL
          AND CAST(e.FechaCreacion AS DATE) BETWEEN @Desde AND @Hasta
    ),
    Celdas AS
    (
        SELECT Provincia, Sexo, RangoEdad, TipoDiscapacidad, GradoDiscapacidad, COUNT(*) AS Cantidad
        FROM Base
        GROUP BY Provincia, Sexo, RangoEdad, TipoDiscapacidad, GradoDiscapacidad
    )
    SELECT Provincia, Sexo, RangoEdad, TipoDiscapacidad, GradoDiscapacidad, Cantidad
    FROM Celdas WHERE Cantidad >= 10
    UNION ALL
    SELECT N'Otros (celdas con menos de 10 personas)', N'-', N'-', N'-', N'-', SUM(Cantidad)
    FROM Celdas WHERE Cantidad < 10
    HAVING SUM(Cantidad) > 0
    ORDER BY Cantidad DESC;
END;
GO

-- Reporte 3: ingresos recibidos (Caja y portal)
CREATE OR ALTER PROCEDURE core.ppRptIngresos
    @Desde  DATE,
    @Hasta  DATE
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CAST(pg.FechaPago AS DATE) AS Fecha, pg.Origen, pg.TasaCodigo, ISNULL(t.Descripcion, pg.TasaCodigo) AS Concepto,
           pg.MedioPago, COUNT(*) AS Transacciones, SUM(pg.Cantidad) AS Cantidad, SUM(pg.Total) AS Total
    FROM core.Pago pg
    LEFT JOIN cat.Tasa t ON t.Codigo = pg.TasaCodigo
    WHERE CAST(pg.FechaPago AS DATE) BETWEEN @Desde AND @Hasta
    GROUP BY CAST(pg.FechaPago AS DATE), pg.Origen, pg.TasaCodigo, ISNULL(t.Descripcion, pg.TasaCodigo), pg.MedioPago
    ORDER BY Fecha, Concepto;
END;
GO

-- Reporte 4: carnets emitidos en el período
CREATE OR ALTER PROCEDURE core.ppRptCarnetsEmitidos
    @Desde  DATE,
    @Hasta  DATE
AS
BEGIN
    SET NOCOUNT ON;
    SELECT c.NumeroCarnet, c.NumeroCertificado, e.NumeroExpediente, CONCAT(p.Nombres, N' ', p.Apellidos) AS NombreCompleto,
           ISNULL(e.TipoDiscapacidad, N'-') AS TipoDiscapacidad, ISNULL(e.GradoDiscapacidad, N'-') AS GradoDiscapacidad,
           c.FechaEmision, c.FechaVencimiento, c.Estado, ISNULL(ce.Nombre, N'-') AS Centro
    FROM core.Carnet c
    INNER JOIN core.Expediente e ON e.ExpedienteId = c.ExpedienteId
    INNER JOIN core.Persona p ON p.PersonaId = e.PersonaId
    LEFT JOIN cat.CentroValoracion ce ON ce.CentroId = e.CentroId
    WHERE c.FechaEmision BETWEEN @Desde AND @Hasta
    ORDER BY c.FechaEmision, c.NumeroCarnet;
END;
GO

-- Reporte 5: informe de valoración para el comité (salida de RF-CORE-02)
CREATE OR ALTER PROCEDURE core.ppRptInformeValoracion
    @EvaluacionId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT e.NumeroExpediente, CONCAT(p.Nombres, N' ', p.Apellidos) AS NombreCompleto, p.Cedula,
           ev.FechaEvaluacion, ev.Estado AS EstadoEvaluacion, ev.WhodasCognicion, ev.WhodasMovilidad,
           ev.WhodasAutocuidado, ev.WhodasRelaciones, ev.WhodasActividades, ev.WhodasParticipacion,
           ev.PuntajeWhodas, ISNULL(ev.GradoPropuesto, N'-') AS GradoPropuesto, ISNULL(ev.TipoPropuesto, N'-') AS TipoPropuesto,
           ISNULL(ev.VersionBaremo, N'-') AS VersionBaremo, ISNULL(ev.Observaciones, N'') AS Observaciones,
           ISNULL(c.Codigo, N'-') AS Codigo, ISNULL(cc.Descripcion, N'Sin codificaciones') AS DescripcionCodigo,
           ISNULL(c.Disciplina, N'-') AS Disciplina,
           CASE WHEN c.Codigo IS NULL THEN N''
                WHEN cc.Componente = 'e' THEN CONCAT(CASE WHEN c.EsFacilitador = 1 THEN N'+' ELSE N'.' END, c.Calificador1)
                ELSE CONCAT(N'.', c.Calificador1, CASE WHEN c.Calificador2 IS NOT NULL THEN CONCAT(N'.', c.Calificador2) ELSE N'' END)
           END AS Calificadores,
           ISNULL(c.Observacion, N'') AS ObservacionCodigo
    FROM core.EvaluacionCIF ev
    INNER JOIN core.Expediente e ON e.ExpedienteId = ev.ExpedienteId
    INNER JOIN core.Persona p ON p.PersonaId = e.PersonaId
    LEFT JOIN core.CodificacionCIF c ON c.EvaluacionId = ev.EvaluacionId
    LEFT JOIN cat.CodigoCIF cc ON cc.Codigo = c.Codigo
    WHERE ev.EvaluacionId = @EvaluacionId
    ORDER BY c.Disciplina, c.Codigo;
END;
GO

-- Reporte 6: indicadores de gestión del trámite (ROT-06, Ley 200-04)
CREATE OR ALTER PROCEDURE core.ppRptIndicadoresTramite
    @Desde  DATE,
    @Hasta  DATE
AS
BEGIN
    SET NOCOUNT ON;
    WITH Solicitudes AS
    (
        SELECT FORMAT(s.FechaRegistro, 'yyyy-MM') AS Mes, COUNT(*) AS Recibidas
        FROM core.Solicitud s
        WHERE CAST(s.FechaRegistro AS DATE) BETWEEN @Desde AND @Hasta
        GROUP BY FORMAT(s.FechaRegistro, 'yyyy-MM')
    ),
    Emisiones AS
    (
        SELECT FORMAT(c.FechaEmision, 'yyyy-MM') AS Mes, COUNT(*) AS CarnetsEmitidos,
               AVG(CAST(DATEDIFF(DAY, e.FechaCreacion, c.FechaEmision) AS DECIMAL(10,2))) AS PromedioDias
        FROM core.Carnet c
        INNER JOIN core.Expediente e ON e.ExpedienteId = c.ExpedienteId
        WHERE c.FechaEmision BETWEEN @Desde AND @Hasta
        GROUP BY FORMAT(c.FechaEmision, 'yyyy-MM')
    )
    SELECT COALESCE(s.Mes, em.Mes) AS Mes, ISNULL(s.Recibidas, 0) AS SolicitudesRecibidas,
           ISNULL(em.CarnetsEmitidos, 0) AS CarnetsEmitidos, ISNULL(em.PromedioDias, 0) AS PromedioDiasHastaCarnet
    FROM Solicitudes s
    FULL OUTER JOIN Emisiones em ON em.Mes = s.Mes
    ORDER BY Mes;
END;
GO

PRINT N'08 - Procedimientos de reportes creados.';
GO