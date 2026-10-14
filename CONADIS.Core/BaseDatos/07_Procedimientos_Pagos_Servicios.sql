/* =============================================================================
   CONADIS.Core - 07: Órdenes de cobro, pagos, solicitudes de servicio y notificaciones
   ============================================================================= */
USE CONADIS_CoreDB;
GO

/* ----------------------------- ÓRDENES DE COBRO ---------------------------- */
CREATE OR ALTER PROCEDURE core.ppOrden_Obtener
    @NumeroOrden VARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT o.NumeroOrden, t.Codigo AS TasaCodigo, t.Descripcion AS Concepto, o.Documento, o.NombrePagador, o.Referencia,
           o.Cantidad, o.MontoUnitario, o.Total, o.Estado, o.FechaCreacion, o.FechaPago, o.NumeroComprobante
    FROM core.OrdenCobro o
    INNER JOIN cat.Tasa t ON t.TasaId = o.TasaId
    WHERE o.NumeroOrden = @NumeroOrden;
END;
GO

-- El precio sale SIEMPRE del catálogo vigente: no se admiten precios manuales (RINV-07).
CREATE OR ALTER PROCEDURE core.ppOrden_Crear
    @TasaCodigo     VARCHAR(20),
    @Cantidad       INT,
    @Documento      VARCHAR(11),
    @NombrePagador  NVARCHAR(150),
    @Referencia     VARCHAR(20) = NULL,
    @Usuario        NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @TasaId INT, @Monto DECIMAL(18,2), @NumeroOrden VARCHAR(20);

    IF @Cantidad < 1 OR @Cantidad > 100 THROW 55001, N'La cantidad debe estar entre 1 y 100.', 1;
    IF LEN(@Documento) NOT IN (9, 11) THROW 55002, N'El documento debe ser una cédula (11 dígitos) o un RNC (9 dígitos).', 1;

    SELECT @TasaId = TasaId, @Monto = Monto FROM cat.Tasa WHERE Codigo = @TasaCodigo AND Activa = 1;
    IF @TasaId IS NULL THROW 55003, N'La tasa indicada no existe o no está activa.', 1;

    BEGIN TRANSACTION;
        EXEC core.ppSecuencia_Siguiente @Prefijo = 'OC', @Numero = @NumeroOrden OUTPUT;
        INSERT INTO core.OrdenCobro (NumeroOrden, TasaId, Cantidad, MontoUnitario, Total, Documento, NombrePagador, Referencia)
        VALUES (@NumeroOrden, @TasaId, @Cantidad, @Monto, @Monto * @Cantidad, @Documento, @NombrePagador, @Referencia);

        EXEC audit.ppEvento_Registrar @Usuario = @Usuario, @Accion = N'OrdenCreada',
             @Entidad = N'OrdenCobro', @EntidadId = @NumeroOrden, @Detalle = @TasaCodigo;
    COMMIT TRANSACTION;

    EXEC core.ppOrden_Obtener @NumeroOrden = @NumeroOrden;
END;
GO

/* ----------------------------- PAGOS --------------------------------------- */
-- Registra un pago llegado desde la Caja o el portal (vía Integración).
-- Idempotente: si el TransaccionId ya existe devuelve 'Duplicado' y no hace nada (RIC-05).
CREATE OR ALTER PROCEDURE core.ppPago_Registrar
    @TransaccionId      UNIQUEIDENTIFIER,
    @Origen             VARCHAR(10),
    @NumeroComprobante  VARCHAR(20),
    @TasaCodigo         VARCHAR(20),
    @Cantidad           INT,
    @Total              DECIMAL(18,2),
    @MedioPago          NVARCHAR(20),
    @Documento          VARCHAR(11),
    @NombrePagador      NVARCHAR(150),
    @NumeroOrden        VARCHAR(20) = NULL,
    @Referencia         VARCHAR(20) = NULL,
    @Caja               NVARCHAR(50) = NULL,
    @Cajero             NVARCHAR(50) = NULL,
    @FechaPago          DATETIMEOFFSET(0)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @OrdenId INT, @EstadoOrden NVARCHAR(10), @TotalOrden DECIMAL(18,2), @RefOrden VARCHAR(20);
    DECLARE @Detalle NVARCHAR(2000);

    BEGIN TRANSACTION;
        IF EXISTS (SELECT 1 FROM core.Pago WITH (UPDLOCK, HOLDLOCK) WHERE TransaccionId = @TransaccionId)
        BEGIN
            COMMIT TRANSACTION;
            SELECT N'Duplicado' AS Estado;
            RETURN;
        END

        IF @NumeroOrden IS NOT NULL
        BEGIN
            SELECT @OrdenId = OrdenId, @EstadoOrden = Estado, @TotalOrden = Total, @RefOrden = Referencia
            FROM core.OrdenCobro WITH (UPDLOCK) WHERE NumeroOrden = @NumeroOrden;

            IF @OrdenId IS NULL THROW 55010, N'La orden de cobro no existe.', 1;
            IF @EstadoOrden = N'Anulada' THROW 55011, N'La orden de cobro está anulada.', 1;
            IF @EstadoOrden = N'Pagada' THROW 55012, N'La orden de cobro ya fue pagada con otra transacción.', 1;
        END

        INSERT INTO core.Pago (TransaccionId, Origen, OrdenId, TasaCodigo, Cantidad, Total, MedioPago, Documento,
                               NombrePagador, NumeroComprobante, Caja, Cajero, FechaPago)
        VALUES (@TransaccionId, @Origen, @OrdenId, @TasaCodigo, @Cantidad, @Total, @MedioPago, @Documento,
                @NombrePagador, @NumeroComprobante, @Caja, @Cajero, @FechaPago);

        IF @OrdenId IS NOT NULL
        BEGIN
            UPDATE core.OrdenCobro
            SET Estado = N'Pagada', FechaPago = @FechaPago, NumeroComprobante = @NumeroComprobante
            WHERE OrdenId = @OrdenId;

            -- "Trámite desbloqueado": deja constancia en el expediente del duplicado pagado
            INSERT INTO core.HistorialEstado (ExpedienteId, EstadoAnterior, EstadoNuevo, Unidad, Usuario, Comentario)
            SELECT e.ExpedienteId, e.Estado, e.Estado, N'Financiera', CONCAT(@Origen, N':', ISNULL(@Cajero, N'portal')),
                   CONCAT(N'Pago recibido (', @NumeroComprobante, N'). Trámite listo para continuar.')
            FROM core.Solicitud s INNER JOIN core.Expediente e ON e.ExpedienteId = s.ExpedienteId
            WHERE s.NumeroTramite = @RefOrden;

            UPDATE core.SolicitudServicio
            SET Estado = N'EnRevision', FechaActualizacion = SYSDATETIMEOFFSET()
            WHERE OrdenId = @OrdenId AND Estado = N'Recibida';
        END

        SET @Detalle = CONCAT(@Origen, N' ', @MedioPago, N' RD$', @Total, N' comprobante ', @NumeroComprobante,
            CASE WHEN @TotalOrden IS NOT NULL AND @TotalOrden <> @Total THEN N' (monto distinto al de la orden)' ELSE N'' END);
        DECLARE @EntidadId NVARCHAR(50) = CAST(@TransaccionId AS NVARCHAR(50));
        EXEC audit.ppEvento_Registrar @Usuario = N'integracion', @Accion = N'PagoRegistrado',
             @Entidad = N'Pago', @EntidadId = @EntidadId, @Detalle = @Detalle;
    COMMIT TRANSACTION;

    SELECT N'Aplicado' AS Estado;
END;
GO

/* ----------------------------- SOLICITUDES DE SERVICIO (RF-WEB-03) --------- */
CREATE OR ALTER PROCEDURE core.ppServicio_Registrar
    @Tipo               VARCHAR(20),
    @Documento          VARCHAR(11),
    @NombreSolicitante  NVARCHAR(150),
    @Correo             NVARCHAR(150),
    @Resumen            NVARCHAR(200),
    @Detalle            NVARCHAR(MAX),
    @Usuario            NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Numero VARCHAR(20), @Unidad NVARCHAR(100), @OrdenId INT, @NumeroOrden VARCHAR(20);
    DECLARE @EstadoCarnet NVARCHAR(10);

    IF @Tipo = 'AYUDA_TECNICA'
    BEGIN
        SELECT TOP (1) @EstadoCarnet = c.Estado
        FROM core.Carnet c
        INNER JOIN core.Expediente e ON e.ExpedienteId = c.ExpedienteId
        INNER JOIN core.Persona p ON p.PersonaId = e.PersonaId
        WHERE p.Cedula = @Documento
        ORDER BY c.CarnetId DESC;

        IF @EstadoCarnet = N'Vencido'
            THROW 55020, N'Tu carnet está vencido. Solicita la renovación para poder pedir ayudas técnicas.', 1;
        IF ISNULL(@EstadoCarnet, N'') <> N'Vigente'
            THROW 55021, N'Necesitas un carnet vigente para solicitar ayudas técnicas.', 1;
        SET @Unidad = N'Dirección de Servicios - Ayudas Técnicas';
    END
    ELSE IF @Tipo = 'CERT_ACCESIBILIDAD'
        SET @Unidad = N'Dirección de Accesibilidad';
    ELSE
        THROW 55022, N'Tipo de solicitud de servicio no válido.', 1;

    BEGIN TRANSACTION;
        EXEC core.ppSecuencia_Siguiente @Prefijo = 'TRA', @Numero = @Numero OUTPUT;

        -- La certificación de accesibilidad genera la orden de cobro de su tasa
        IF @Tipo = 'CERT_ACCESIBILIDAD'
        BEGIN
            DECLARE @TasaId INT, @Monto DECIMAL(18,2);
            SELECT @TasaId = TasaId, @Monto = Monto FROM cat.Tasa WHERE Codigo = 'CERT-ACCES' AND Activa = 1;
            IF @TasaId IS NULL THROW 55023, N'La tasa de certificación de accesibilidad no está configurada.', 1;

            EXEC core.ppSecuencia_Siguiente @Prefijo = 'OC', @Numero = @NumeroOrden OUTPUT;
            INSERT INTO core.OrdenCobro (NumeroOrden, TasaId, Cantidad, MontoUnitario, Total, Documento, NombrePagador, Referencia)
            VALUES (@NumeroOrden, @TasaId, 1, @Monto, @Monto, @Documento, @NombreSolicitante, @Numero);
            SET @OrdenId = SCOPE_IDENTITY();
        END

        INSERT INTO core.SolicitudServicio (NumeroTramite, Tipo, Documento, NombreSolicitante, Correo, Resumen, Detalle, Unidad, OrdenId)
        VALUES (@Numero, @Tipo, @Documento, @NombreSolicitante, @Correo, @Resumen, @Detalle, @Unidad, @OrdenId);

        INSERT INTO core.Notificacion (Cedula, Canal, Destinatario, Asunto, Mensaje)
        VALUES (CASE WHEN LEN(@Documento) = 11 THEN @Documento END, N'Correo', @Correo,
                CONCAT(N'Recibimos tu solicitud ', @Numero),
                CONCAT(N'Tu solicitud ', @Numero, N' fue recibida por la ', @Unidad, N'.',
                       CASE WHEN @NumeroOrden IS NOT NULL THEN CONCAT(N' Paga la orden ', @NumeroOrden, N' para continuar.') ELSE N'' END));

        EXEC audit.ppEvento_Registrar @Usuario = @Usuario, @Accion = N'ServicioRegistrado',
             @Entidad = N'SolicitudServicio', @EntidadId = @Numero, @Detalle = @Tipo;
    COMMIT TRANSACTION;

    SELECT @Numero AS NumeroTramite, SYSDATETIMEOFFSET() AS FechaRegistro, @NumeroOrden AS NumeroOrden;
END;
GO

CREATE OR ALTER PROCEDURE core.ppServicio_Listar
    @Tipo    VARCHAR(20)  = NULL,
    @Estado  NVARCHAR(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT sv.ServicioId, sv.NumeroTramite, sv.Tipo, sv.Documento, sv.NombreSolicitante, sv.Correo, sv.Resumen,
           sv.Detalle, sv.Unidad, sv.Estado, o.NumeroOrden, o.Estado AS EstadoOrden, sv.FechaRegistro, sv.FechaActualizacion
    FROM core.SolicitudServicio sv
    LEFT JOIN core.OrdenCobro o ON o.OrdenId = sv.OrdenId
    WHERE (@Tipo IS NULL OR sv.Tipo = @Tipo) AND (@Estado IS NULL OR sv.Estado = @Estado)
    ORDER BY sv.FechaActualizacion DESC;
END;
GO

CREATE OR ALTER PROCEDURE core.ppServicio_CambiarEstado
    @NumeroTramite  VARCHAR(20),
    @Estado         NVARCHAR(20),
    @Comentario     NVARCHAR(500) = NULL,
    @Usuario        NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Correo NVARCHAR(150), @Documento VARCHAR(11), @Actual NVARCHAR(20), @EstadoOrden NVARCHAR(10);

    SELECT @Correo = sv.Correo, @Documento = sv.Documento, @Actual = sv.Estado, @EstadoOrden = o.Estado
    FROM core.SolicitudServicio sv LEFT JOIN core.OrdenCobro o ON o.OrdenId = sv.OrdenId
    WHERE sv.NumeroTramite = @NumeroTramite;

    IF @Actual IS NULL THROW 55030, N'La solicitud no existe.', 1;
    IF @Actual IN (N'Rechazada', N'Entregada') THROW 55031, N'La solicitud ya está cerrada.', 1;
    IF @Estado = N'Aprobada' AND @EstadoOrden = N'Pendiente'
        THROW 55032, N'No se puede aprobar: la tasa de la solicitud no ha sido pagada.', 1;

    UPDATE core.SolicitudServicio SET Estado = @Estado, FechaActualizacion = SYSDATETIMEOFFSET()
    WHERE NumeroTramite = @NumeroTramite;

    INSERT INTO core.Notificacion (Cedula, Canal, Destinatario, Asunto, Mensaje)
    VALUES (CASE WHEN LEN(@Documento) = 11 THEN @Documento END, N'Correo', @Correo,
            CONCAT(N'Tu solicitud ', @NumeroTramite, N' cambió de estado'),
            CONCAT(N'Nuevo estado: ', @Estado, N'. ', @Comentario));

    DECLARE @Detalle NVARCHAR(2000) = CONCAT(@Actual, N' -> ', @Estado, N'. ', @Comentario);
    EXEC audit.ppEvento_Registrar @Usuario = @Usuario, @Accion = N'ServicioCambioEstado',
         @Entidad = N'SolicitudServicio', @EntidadId = @NumeroTramite, @Detalle = @Detalle;
END;
GO

/* ----------------------------- NOTIFICACIONES ------------------------------ */
-- El módulo de Integración toma las pendientes, las "envía" y reporta el resultado.
CREATE OR ALTER PROCEDURE core.ppNotificacion_Pendientes
    @Maximo INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Maximo) NotificacionId, Canal, Destinatario, Asunto, Mensaje, Intentos
    FROM core.Notificacion
    WHERE Estado = N'Pendiente' AND Canal IN (N'Correo', N'SMS') AND Intentos < 5
    ORDER BY NotificacionId;
END;
GO

CREATE OR ALTER PROCEDURE core.ppNotificacion_MarcarResultado
    @NotificacionId  BIGINT,
    @Exito           BIT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE core.Notificacion
    SET Estado = CASE WHEN @Exito = 1 THEN N'Enviada' WHEN Intentos + 1 >= 5 THEN N'Fallida' ELSE N'Pendiente' END,
        Intentos = Intentos + 1,
        FechaEnvio = CASE WHEN @Exito = 1 THEN SYSDATETIMEOFFSET() ELSE FechaEnvio END
    WHERE NotificacionId = @NotificacionId;
END;
GO

-- Avisos dentro del portal para una cédula
CREATE OR ALTER PROCEDURE core.ppNotificacion_ListarPortal
    @Cedula  CHAR(11),
    @Maximo  INT = 10
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Maximo) Asunto, Mensaje, FechaCreacion
    FROM core.Notificacion
    WHERE Cedula = @Cedula AND Canal = N'Portal'
    ORDER BY NotificacionId DESC;
END;
GO

PRINT N'07 - Procedimientos de pagos, servicios y notificaciones creados.';
GO