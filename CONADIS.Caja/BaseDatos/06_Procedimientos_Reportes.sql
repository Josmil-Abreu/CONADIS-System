/* =============================================================================
   CONADIS.Caja - 06: Reportes (cada transacción de la caja tiene su reporte)
   - caja.Evento registra apertura, cobro, nota de crédito, cierre... : comprobante y listado por período.
   - Representación impresa del e-CF (factura de cobro y nota de crédito).
   - El cierre de caja (ppRptCierreCaja) y los ingresos (ppRptIngresos) están en el script 03.
   Fechas como DATETIME2 en hora local (el ReportViewer no formatea DATETIMEOFFSET).
   ============================================================================= */
USE CONADIS_CajaDB;
GO

CREATE OR ALTER PROCEDURE caja.ppRptTransacciones
    @Desde    DATE,
    @Hasta    DATE,
    @Usuario  NVARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Desde > @Hasta THROW 58018, N'La fecha inicial no puede ser mayor que la final.', 1;
    SELECT EventoId, CAST(Fecha AS DATETIME2(0)) AS Fecha, Usuario, Accion, ISNULL(Detalle, N'') AS Detalle
    FROM caja.Evento
    WHERE CAST(Fecha AS DATE) BETWEEN @Desde AND @Hasta
      AND (@Usuario IS NULL OR Usuario = @Usuario)
    ORDER BY EventoId;
END;
GO

CREATE OR ALTER PROCEDURE caja.ppRptComprobanteTransaccion
    @EventoId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM caja.Evento WHERE EventoId = @EventoId)
        THROW 58021, N'La transacción indicada no existe.', 1;
    SELECT EventoId, CAST(Fecha AS DATETIME2(0)) AS Fecha, Usuario, Accion, ISNULL(Detalle, N'') AS Detalle
    FROM caja.Evento WHERE EventoId = @EventoId;
END;
GO

-- Representación impresa del e-CF: 1) encabezado  2) líneas  3) pagos
CREATE OR ALTER PROCEDURE caja.ppRptFactura
    @FacturaId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM caja.Factura WHERE FacturaId = @FacturaId)
        THROW 58022, N'El comprobante indicado no existe.', 1;

    SELECT f.FacturaId, f.Tipo,
           CASE f.Tipo WHEN '31' THEN N'Factura de crédito fiscal electrónica'
                       WHEN '32' THEN N'Factura de consumo electrónica'
                       WHEN '34' THEN N'Nota de crédito electrónica'
                       WHEN '45' THEN N'Comprobante gubernamental electrónico' END AS TipoDescripcion,
           f.Encf, ISNULL(f.NumeroOrden, N'-') AS NumeroOrden, f.DocumentoCliente, f.NombreCliente, ISNULL(f.CorreoCliente, N'-') AS CorreoCliente,
           f.Subtotal, f.Itbis, f.Total, f.CodigoSeguridad, f.EstadoDgii, ISNULL(f.TrackId, N'-') AS TrackId,
           CAST(f.Fecha AS DATETIME2(0)) AS Fecha, f.Anulada, ISNULL(r.Encf, N'-') AS EncfReferencia,
           c.Codigo AS Caja, c.Sucursal, u.NombreCompleto AS Cajero,
           ISNULL((SELECT Valor FROM caja.Parametro WHERE Clave = 'RncEmisor'), '401000001') AS RncEmisor
    FROM caja.Factura f
    INNER JOIN caja.Sesion s ON s.SesionId = f.SesionId
    INNER JOIN caja.Caja c ON c.CajaId = s.CajaId
    INNER JOIN seg.Usuario u ON u.UsuarioId = s.UsuarioId
    LEFT JOIN caja.Factura r ON r.FacturaId = f.FacturaReferenciaId
    WHERE f.FacturaId = @FacturaId;

    SELECT Linea, TasaCodigo, Descripcion, Cantidad, PrecioUnitario, ItbisPorcentaje, Itbis, Total
    FROM caja.LineaFactura WHERE FacturaId = @FacturaId ORDER BY Linea;

    SELECT MedioPago, Monto, ISNULL(MontoRecibido, Monto) AS MontoRecibido, ISNULL(Cambio, 0) AS Cambio,
           ISNULL(Referencia, N'-') AS Referencia, ISNULL(Autorizacion, N'-') AS Autorizacion
    FROM caja.Pago WHERE FacturaId = @FacturaId;
END;
GO

PRINT N'06 - Procedimientos de reportes de la caja creados.';
GO
