/* =============================================================================
   CONADIS.Core - 11: Reportes de transacciones (cada transacción tiene su reporte)
   - Toda operación del Core queda en audit.Evento (bitácora encadenada con hash).
     ppRptComprobanteTransaccion imprime cualquiera de ellas; ppRptTransacciones las lista por período.
   - Documentos propios de las transacciones principales: acuse de solicitud, carnet emitido
     y recibo de pago (el informe de valoración ya está en el script 08).
   Las fechas se devuelven como DATETIME2 en hora local (el ReportViewer no formatea DATETIMEOFFSET).
   ============================================================================= */
USE CONADIS_CoreDB;
GO

-- Listado de transacciones por período (filtros opcionales por acción y usuario)
CREATE OR ALTER PROCEDURE core.ppRptTransacciones
    @Desde    DATE,
    @Hasta    DATE,
    @Accion   NVARCHAR(100) = NULL,
    @Usuario  NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Desde > @Hasta THROW 50090, N'La fecha inicial no puede ser mayor que la final.', 1;
    SELECT EventoId, CAST(Fecha AS DATETIME2(0)) AS Fecha, Usuario, ISNULL(Ip, N'-') AS Ip, Accion, Entidad,
           ISNULL(EntidadId, N'-') AS EntidadId, ISNULL(Detalle, N'') AS Detalle
    FROM audit.Evento
    WHERE CAST(Fecha AS DATE) BETWEEN @Desde AND @Hasta
      AND (@Accion IS NULL OR Accion = @Accion)
      AND (@Usuario IS NULL OR Usuario = @Usuario)
    ORDER BY EventoId;
END;
GO

-- Comprobante de UNA transacción, con su huella (hash) para demostrar que no fue alterada
CREATE OR ALTER PROCEDURE core.ppRptComprobanteTransaccion
    @EventoId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM audit.Evento WHERE EventoId = @EventoId)
        THROW 50091, N'La transacción indicada no existe.', 1;
    SELECT EventoId, CAST(Fecha AS DATETIME2(0)) AS Fecha, Usuario, ISNULL(Ip, N'-') AS Ip, Accion, Entidad,
           ISNULL(EntidadId, N'-') AS EntidadId, ISNULL(Detalle, N'') AS Detalle,
           CONVERT(VARCHAR(64), Hash, 2) AS Huella,
           ISNULL(CONVERT(VARCHAR(64), HashAnterior, 2), N'(primer registro)') AS HuellaAnterior
    FROM audit.Evento WHERE EventoId = @EventoId;
END;
GO

-- Acuse de recibo de una solicitud (registro presencial o por el portal)
CREATE OR ALTER PROCEDURE core.ppRptAcuseSolicitud
    @NumeroTramite VARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM core.Solicitud WHERE NumeroTramite = @NumeroTramite)
        THROW 50092, N'La solicitud indicada no existe.', 1;
    SELECT s.NumeroTramite, e.NumeroExpediente, s.Tipo, s.Canal, s.Estado AS EstadoSolicitud,
           CAST(s.FechaRegistro AS DATETIME2(0)) AS FechaRegistro, s.UsuarioRegistro,
           ISNULL(p.Cedula, CONCAT(N'Acta ', p.ActaNacimiento)) AS Documento,
           CONCAT(p.Nombres, N' ', p.Apellidos) AS NombreCompleto,
           ISNULL(p.Telefono, N'-') AS Telefono, ISNULL(p.Correo, N'-') AS Correo,
           CONCAT_WS(N', ', p.Direccion, p.Sector, p.Municipio, p.Provincia) AS Direccion,
           ISNULL(s.Establecimiento, N'-') AS Establecimiento, ISNULL(s.NumeroCertificadoMedico, N'-') AS CertificadoMedico,
           ISNULL(s.CodigoCie10, N'-') AS CodigoCie10, s.EstadoDiagnostico,
           ISNULL(c.Nombre, N'Por asignar') AS CentroPreferido,
           CAST(s.FechaCitaPreferida AS DATETIME2(0)) AS FechaCitaPreferida,
           ISNULL(s.Apoyos, N'Ninguno') AS Apoyos
    FROM core.Solicitud s
    INNER JOIN core.Expediente e ON e.ExpedienteId = s.ExpedienteId
    INNER JOIN core.Persona p ON p.PersonaId = e.PersonaId
    LEFT JOIN cat.CentroValoracion c ON c.CentroId = s.CentroPreferidoId
    WHERE s.NumeroTramite = @NumeroTramite;
END;
GO

-- Carnet / certificado emitido (con el código de verificación del QR)
CREATE OR ALTER PROCEDURE core.ppRptCarnetEmitido
    @CarnetId INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM core.Carnet WHERE CarnetId = @CarnetId)
        THROW 50093, N'El carnet indicado no existe.', 1;
    SELECT c.NumeroCarnet, c.NumeroCertificado, c.CodigoVerificacion, c.FechaEmision, c.FechaVencimiento,
           c.Estado, c.UsuarioEmite, ISNULL(c.MotivoAnulacion, N'') AS MotivoAnulacion,
           e.NumeroExpediente, ISNULL(p.Cedula, N'-') AS Cedula, CONCAT(p.Nombres, N' ', p.Apellidos) AS NombreCompleto,
           ISNULL(e.TipoDiscapacidad, N'-') AS TipoDiscapacidad, ISNULL(e.GradoDiscapacidad, N'-') AS GradoDiscapacidad,
           ISNULL(ce.Nombre, N'-') AS Centro
    FROM core.Carnet c
    INNER JOIN core.Expediente e ON e.ExpedienteId = c.ExpedienteId
    INNER JOIN core.Persona p ON p.PersonaId = e.PersonaId
    LEFT JOIN cat.CentroValoracion ce ON ce.CentroId = e.CentroId
    WHERE c.CarnetId = @CarnetId;
END;
GO

-- Recibo de una orden de cobro y de los pagos aplicados a ella
CREATE OR ALTER PROCEDURE core.ppRptReciboPago
    @NumeroOrden VARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM core.OrdenCobro WHERE NumeroOrden = @NumeroOrden)
        THROW 50094, N'La orden de cobro indicada no existe.', 1;
    SELECT o.NumeroOrden, t.Codigo AS TasaCodigo, t.Descripcion AS Concepto, o.Cantidad, o.MontoUnitario, o.Total,
           o.Documento, o.NombrePagador, ISNULL(o.Referencia, N'-') AS Referencia, o.Estado,
           CAST(o.FechaCreacion AS DATETIME2(0)) AS FechaCreacion, CAST(o.FechaPago AS DATETIME2(0)) AS FechaPago,
           ISNULL(o.NumeroComprobante, N'-') AS NumeroComprobante,
           ISNULL(pg.Origen, N'-') AS Origen, ISNULL(pg.MedioPago, N'-') AS MedioPago,
           ISNULL(CONVERT(VARCHAR(36), pg.TransaccionId), N'-') AS TransaccionId,
           ISNULL(pg.Caja, N'-') AS Caja, ISNULL(pg.Cajero, N'-') AS Cajero
    FROM core.OrdenCobro o
    INNER JOIN cat.Tasa t ON t.TasaId = o.TasaId
    OUTER APPLY (SELECT TOP (1) p.Origen, p.MedioPago, p.TransaccionId, p.Caja, p.Cajero FROM core.Pago p WHERE p.OrdenId = o.OrdenId ORDER BY p.PagoId DESC) pg
    WHERE o.NumeroOrden = @NumeroOrden;
END;
GO

PRINT N'11 - Procedimientos de reportes de transacciones creados.';
GO
