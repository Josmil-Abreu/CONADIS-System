/* =============================================================================
   CONADIS.Web - 06: Reportes del Portal Ciudadano
   Cada operación del ciudadano en el portal (crear cuenta, entrar, enviar solicitud, pagar, apelar,
   reprogramar cita, pedir ayuda técnica...) queda en web.EventoSeguridad. Con estos SP el ciudadano
   descarga el comprobante de cualquiera de ellas y el listado de su actividad.
   Los documentos del trámite (acuse, comprobante de pago, carnet) se arman con los datos que
   devuelve Integración, no necesitan SP.
   ============================================================================= */
USE CONADIS_WebDB;
GO

CREATE OR ALTER PROCEDURE web.ppRptTransacciones
    @CuentaId INT,
    @Desde    DATE,
    @Hasta    DATE
AS
BEGIN
    SET NOCOUNT ON;
    IF @Desde > @Hasta THROW 57090, N'La fecha inicial no puede ser mayor que la final.', 1;
    SELECT EventoId, CAST(Fecha AS DATETIME2(0)) AS Fecha, Evento, ISNULL(Ip, '-') AS Ip, ISNULL(Detalle, N'') AS Detalle
    FROM web.EventoSeguridad
    WHERE CuentaId = @CuentaId AND CAST(Fecha AS DATE) BETWEEN @Desde AND @Hasta
    ORDER BY EventoId DESC;
END;
GO

-- Solo devuelve el evento si pertenece a la cuenta (un ciudadano no puede ver comprobantes ajenos)
CREATE OR ALTER PROCEDURE web.ppRptComprobanteTransaccion
    @EventoId BIGINT,
    @CuentaId INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM web.EventoSeguridad WHERE EventoId = @EventoId AND CuentaId = @CuentaId)
        THROW 57091, N'La transacción indicada no existe en su cuenta.', 1;
    SELECT e.EventoId, CAST(e.Fecha AS DATETIME2(0)) AS Fecha, e.Evento, ISNULL(e.Ip, '-') AS Ip, ISNULL(e.Detalle, N'') AS Detalle,
           c.Cedula, c.NombreCompleto, c.Correo
    FROM web.EventoSeguridad e
    INNER JOIN web.Cuenta c ON c.CuentaId = e.CuentaId
    WHERE e.EventoId = @EventoId;
END;
GO

PRINT N'06 - Procedimientos de reportes del portal creados.';
GO
