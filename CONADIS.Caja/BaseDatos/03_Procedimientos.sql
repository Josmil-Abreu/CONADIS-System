/* =============================================================================
   CONADIS.Caja - 03: Procedimientos de la Caja
   Errores de negocio de la Caja: THROW 58001..58999.
   ============================================================================= */
USE CONADIS_CajaDB;
GO

/* ----------------------------- LOG4NET ------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.ppInsertarLog
    @log_date  DATETIME2(3),
    @thread    NVARCHAR(255),
    @level     NVARCHAR(50),
    @logger    NVARCHAR(255),
    @message   NVARCHAR(MAX),
    @exception NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Log4NetLog ([Date], [Thread], [Level], [Logger], [Message], [Exception])
    VALUES (@log_date, LEFT(@thread, 255), LEFT(@level, 50), LEFT(@logger, 255), @message, @exception);
END;
GO

CREATE OR ALTER PROCEDURE dbo.ppLog_Listar
    @Desde  DATETIME2(3),
    @Hasta  DATETIME2(3),
    @Nivel  NVARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (1000) Id, [Date] AS Fecha, [Level] AS Nivel, [Logger] AS Origen, [Message] AS Mensaje, [Exception] AS Excepcion
    FROM dbo.Log4NetLog
    WHERE [Date] >= @Desde AND [Date] < @Hasta
      AND (@Nivel IS NULL OR [Level] = @Nivel)
    ORDER BY Id DESC;
END;
GO

/* ----------------------------- AUDITORÍA ----------------------------------- */
CREATE OR ALTER PROCEDURE caja.ppEvento_Registrar
    @Usuario  NVARCHAR(50),
    @Accion   NVARCHAR(50),
    @Detalle  NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO caja.Evento (Usuario, Accion, Detalle) VALUES (@Usuario, @Accion, LEFT(@Detalle, 500));
END;
GO

/* ----------------------------- USUARIOS ------------------------------------ */
CREATE OR ALTER PROCEDURE seg.ppUsuario_ObtenerParaLogin
    @Username NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT UsuarioId, Username, PasswordHash, NombreCompleto, Rol, Activo, IntentosFallidos, BloqueadoHasta
    FROM seg.Usuario WHERE Username = @Username;
END;
GO

CREATE OR ALTER PROCEDURE seg.ppUsuario_RegistrarIntentoFallido
    @UsuarioId INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Max INT = ISNULL(TRY_CAST((SELECT Valor FROM caja.Parametro WHERE Clave = 'LoginIntentosMaximos') AS INT), 5);
    DECLARE @Minutos INT = ISNULL(TRY_CAST((SELECT Valor FROM caja.Parametro WHERE Clave = 'LoginMinutosBloqueo') AS INT), 15);
    UPDATE seg.Usuario
    SET IntentosFallidos = CASE WHEN IntentosFallidos + 1 >= @Max THEN 0 ELSE IntentosFallidos + 1 END,
        BloqueadoHasta   = CASE WHEN IntentosFallidos + 1 >= @Max THEN DATEADD(MINUTE, @Minutos, SYSDATETIMEOFFSET()) ELSE BloqueadoHasta END
    WHERE UsuarioId = @UsuarioId;
    SELECT BloqueadoHasta FROM seg.Usuario WHERE UsuarioId = @UsuarioId;
END;
GO

CREATE OR ALTER PROCEDURE seg.ppUsuario_RegistrarAccesoExitoso
    @UsuarioId INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE seg.Usuario SET IntentosFallidos = 0, BloqueadoHasta = NULL, UltimoAcceso = SYSDATETIMEOFFSET() WHERE UsuarioId = @UsuarioId;
END;
GO

/* ----------------------------- CATÁLOGOS ----------------------------------- */
CREATE OR ALTER PROCEDURE caja.ppParametro_Listar
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Clave, Valor, Descripcion FROM caja.Parametro ORDER BY Clave;
END;
GO

CREATE OR ALTER PROCEDURE caja.ppCaja_Listar
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CajaId, Codigo, Sucursal, Activa FROM caja.Caja WHERE Activa = 1 ORDER BY Codigo;
END;
GO

CREATE OR ALTER PROCEDURE caja.ppTasa_Listar
    @SoloActivas BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Codigo, Descripcion, Monto, ItbisPorcentaje, Activa, FechaSincronizacion
    FROM caja.Tasa WHERE @SoloActivas = 0 OR Activa = 1 ORDER BY Descripcion;
END;
GO

-- Actualiza la copia local con el catálogo del Core (JSON: [{"codigo","descripcion","monto","itbisPorcentaje","activa"}]).
-- Las tasas que ya no vienen quedan inactivas (no se borran: hay facturas que las usan).
CREATE OR ALTER PROCEDURE caja.ppTasa_Sincronizar
    @Tasas NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF ISJSON(@Tasas) = 0 THROW 58020, N'El catálogo de tasas recibido no es un JSON válido.', 1;

    DECLARE @Origen TABLE (Codigo VARCHAR(20) PRIMARY KEY, Descripcion NVARCHAR(200), Monto DECIMAL(18,2), ItbisPorcentaje DECIMAL(5,2), Activa BIT);
    INSERT INTO @Origen
    SELECT Codigo, Descripcion, Monto, ISNULL(ItbisPorcentaje, 0), ISNULL(Activa, 1)
    FROM OPENJSON(@Tasas) WITH (Codigo VARCHAR(20) '$.codigo', Descripcion NVARCHAR(200) '$.descripcion',
                                Monto DECIMAL(18,2) '$.monto', ItbisPorcentaje DECIMAL(5,2) '$.itbisPorcentaje', Activa BIT '$.activa')
    WHERE Codigo IS NOT NULL AND Monto IS NOT NULL;

    BEGIN TRANSACTION;
        MERGE caja.Tasa AS d
        USING @Origen AS o ON d.Codigo = o.Codigo
        WHEN MATCHED THEN UPDATE SET Descripcion = o.Descripcion, Monto = o.Monto, ItbisPorcentaje = o.ItbisPorcentaje,
                                     Activa = o.Activa, FechaSincronizacion = SYSDATETIMEOFFSET()
        WHEN NOT MATCHED BY TARGET THEN INSERT (Codigo, Descripcion, Monto, ItbisPorcentaje, Activa)
                                        VALUES (o.Codigo, o.Descripcion, o.Monto, o.ItbisPorcentaje, o.Activa)
        WHEN NOT MATCHED BY SOURCE THEN UPDATE SET Activa = 0, FechaSincronizacion = SYSDATETIMEOFFSET();
    COMMIT TRANSACTION;

    SELECT COUNT(*) AS TasasRecibidas FROM @Origen;
END;
GO

-- Disponibilidad de cada secuencia e-NCF. Alerta al quedar el 10 % o 30 días de vigencia (RF-CAJ-02).
CREATE OR ALTER PROCEDURE caja.ppSecuencia_Estado
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Tipo, Siguiente, Hasta, Hasta - Siguiente + 1 AS Disponibles, VenceEn,
           CAST(CASE WHEN Hasta - Siguiente + 1 <= Hasta / 10 OR VenceEn <= DATEADD(DAY, 30, CAST(SYSDATETIMEOFFSET() AS DATE))
                     THEN 1 ELSE 0 END AS BIT) AS RequiereAtencion
    FROM caja.SecuenciaEcf ORDER BY Tipo;
END;
GO

/* ----------------------------- SESIONES ------------------------------------ */
CREATE OR ALTER PROCEDURE caja.ppSesion_Abrir
    @CajaId        INT,
    @UsuarioId     INT,
    @FondoInicial  DECIMAL(18,2)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @FondoInicial < 0 THROW 58001, N'El fondo inicial no puede ser negativo.', 1;

    BEGIN TRANSACTION;
        IF EXISTS (SELECT 1 FROM caja.Sesion WITH (UPDLOCK, HOLDLOCK) WHERE CajaId = @CajaId AND Estado <> N'Cerrada')
            THROW 58002, N'Esta caja ya tiene una sesión abierta. Ciérrela o use otra caja.', 1;
        IF EXISTS (SELECT 1 FROM caja.Sesion WITH (UPDLOCK, HOLDLOCK) WHERE UsuarioId = @UsuarioId AND Estado <> N'Cerrada')
            THROW 58002, N'Ya tiene una sesión de caja abierta.', 1;

        INSERT INTO caja.Sesion (CajaId, UsuarioId, FondoInicial) VALUES (@CajaId, @UsuarioId, @FondoInicial);
        DECLARE @SesionId INT = SCOPE_IDENTITY();

        DECLARE @Usuario NVARCHAR(50) = (SELECT Username FROM seg.Usuario WHERE UsuarioId = @UsuarioId);
        DECLARE @Detalle NVARCHAR(500) = CONCAT(N'Sesión ', @SesionId, N', fondo RD$', @FondoInicial);
        EXEC caja.ppEvento_Registrar @Usuario = @Usuario, @Accion = N'AperturaCaja', @Detalle = @Detalle;
    COMMIT TRANSACTION;

    SELECT @SesionId AS SesionId;
END;
GO

CREATE OR ALTER PROCEDURE caja.ppSesion_ObtenerAbierta
    @UsuarioId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.SesionId, s.CajaId, c.Codigo AS CajaCodigo, c.Sucursal, s.FondoInicial, s.Estado, s.FechaApertura
    FROM caja.Sesion s INNER JOIN caja.Caja c ON c.CajaId = s.CajaId
    WHERE s.UsuarioId = @UsuarioId AND s.Estado <> N'Cerrada';
END;
GO

/* ----------------------------- FACTURACIÓN (e-CF) -------------------------- */
-- Emite un comprobante en UNA transacción serializable (RF-CAJ-01 y RF-CAJ-02):
--   valida la sesión, toma los precios del catálogo local (nunca manuales), calcula ITBIS y total,
--   valida el pago, asigna el siguiente e-NCF sin saltos ni duplicados y guarda factura, líneas y pago.
-- @Lineas = JSON: [{"tasaCodigo":"COPIA-CERT","cantidad":2}]
CREATE OR ALTER PROCEDURE caja.ppFactura_Emitir
    @SesionId          INT,
    @UsuarioId         INT,
    @Tipo              CHAR(2),
    @NumeroOrden       VARCHAR(20)   = NULL,
    @DocumentoCliente  VARCHAR(11),
    @NombreCliente     NVARCHAR(150),
    @CorreoCliente     NVARCHAR(150) = NULL,
    @Lineas            NVARCHAR(MAX),
    @MedioPago         NVARCHAR(20),
    @MontoRecibido     DECIMAL(18,2) = NULL,
    @Referencia        NVARCHAR(50)  = NULL,
    @Autorizacion      NVARCHAR(20)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

    IF @Tipo NOT IN ('31', '32', '45') THROW 58010, N'Tipo de comprobante no válido para un cobro (31, 32 o 45).', 1;
    IF ISJSON(@Lineas) = 0 THROW 58004, N'El detalle del cobro no es válido.', 1;
    IF @MedioPago = N'Tarjeta' AND ISNULL(LTRIM(@Autorizacion), N'') = N''
        THROW 58007, N'Falta el código de autorización del POS.', 1;
    IF @MedioPago = N'Transferencia' AND ISNULL(LTRIM(@Referencia), N'') = N''
        THROW 58008, N'Falta el número de referencia de la transferencia.', 1;

    DECLARE @Detalle TABLE (Linea INT IDENTITY(1,1), TasaCodigo VARCHAR(20), Descripcion NVARCHAR(200), Cantidad INT,
                            PrecioUnitario DECIMAL(18,2), ItbisPorcentaje DECIMAL(5,2), Itbis DECIMAL(18,2), Total DECIMAL(18,2));
    DECLARE @Pedido TABLE (TasaCodigo VARCHAR(20), Cantidad INT);

    INSERT INTO @Pedido
    SELECT TasaCodigo, Cantidad FROM OPENJSON(@Lineas) WITH (TasaCodigo VARCHAR(20) '$.tasaCodigo', Cantidad INT '$.cantidad');

    IF NOT EXISTS (SELECT 1 FROM @Pedido) THROW 58005, N'Agregue al menos una tasa al cobro.', 1;
    IF EXISTS (SELECT 1 FROM @Pedido WHERE Cantidad IS NULL OR Cantidad NOT BETWEEN 1 AND 100)
        THROW 58005, N'La cantidad de cada tasa debe estar entre 1 y 100.', 1;
    IF EXISTS (SELECT 1 FROM @Pedido p LEFT JOIN caja.Tasa t ON t.Codigo = p.TasaCodigo AND t.Activa = 1 WHERE t.Codigo IS NULL)
        THROW 58004, N'Una de las tasas no existe o no está vigente.', 1;

    -- Precios SIEMPRE del catálogo
    INSERT INTO @Detalle (TasaCodigo, Descripcion, Cantidad, PrecioUnitario, ItbisPorcentaje, Itbis, Total)
    SELECT t.Codigo, t.Descripcion, p.Cantidad, t.Monto, t.ItbisPorcentaje,
           ROUND(t.Monto * p.Cantidad * t.ItbisPorcentaje / 100, 2),
           t.Monto * p.Cantidad + ROUND(t.Monto * p.Cantidad * t.ItbisPorcentaje / 100, 2)
    FROM @Pedido p INNER JOIN caja.Tasa t ON t.Codigo = p.TasaCodigo;

    DECLARE @Itbis DECIMAL(18,2) = (SELECT SUM(Itbis) FROM @Detalle);
    DECLARE @Total DECIMAL(18,2) = (SELECT SUM(Total) FROM @Detalle);
    DECLARE @Subtotal DECIMAL(18,2) = @Total - @Itbis;
    DECLARE @Cambio DECIMAL(18,2) = NULL;

    IF @MedioPago = N'Efectivo'
    BEGIN
        IF @MontoRecibido IS NULL OR @MontoRecibido < @Total
            THROW 58006, N'El efectivo recibido no cubre el total.', 1;
        SET @Cambio = @MontoRecibido - @Total;
    END

    DECLARE @RncEmisor VARCHAR(11) = ISNULL((SELECT Valor FROM caja.Parametro WHERE Clave = 'RncEmisor'), '401000001');
    DECLARE @Numero BIGINT, @Encf CHAR(13), @FacturaId BIGINT, @Codigo CHAR(6), @Usuario NVARCHAR(50);

    BEGIN TRANSACTION;
        -- La sesión debe estar abierta y ser del cajero (RF-CAJ-01 paso 1)
        IF NOT EXISTS (SELECT 1 FROM caja.Sesion WITH (UPDLOCK) WHERE SesionId = @SesionId AND UsuarioId = @UsuarioId AND Estado = N'Abierta')
            THROW 58003, N'No tiene una caja abierta. Abra la caja (Apertura) antes de cobrar.', 1;

        -- Siguiente e-NCF: el bloqueo impide que dos cajas obtengan el mismo número
        UPDATE caja.SecuenciaEcf WITH (UPDLOCK, HOLDLOCK)
        SET @Numero = Siguiente, Siguiente = Siguiente + 1
        WHERE Tipo = @Tipo AND Siguiente <= Hasta AND VenceEn >= CAST(SYSDATETIMEOFFSET() AS DATE);
        IF @@ROWCOUNT = 0
            THROW 58009, N'La secuencia de comprobantes de este tipo está agotada o vencida. Avise al supervisor.', 1;

        SET @Encf = CONCAT('E', @Tipo, RIGHT(CONCAT('0000000000', @Numero), 10));
        SET @Codigo = LEFT(CONVERT(VARCHAR(64), HASHBYTES('SHA2_256',
                        CONCAT(@Encf, '|', @RncEmisor, '|', @Total, '|', CONVERT(VARCHAR(40), SYSDATETIMEOFFSET(), 126))), 2), 6);

        INSERT INTO caja.Factura (SesionId, Tipo, Encf, NumeroOrden, DocumentoCliente, NombreCliente, CorreoCliente,
                                  Subtotal, Itbis, Total, CodigoSeguridad)
        VALUES (@SesionId, @Tipo, @Encf, NULLIF(LTRIM(@NumeroOrden), ''), @DocumentoCliente, @NombreCliente, @CorreoCliente,
                @Subtotal, @Itbis, @Total, @Codigo);
        SET @FacturaId = SCOPE_IDENTITY();

        INSERT INTO caja.LineaFactura (FacturaId, Linea, TasaCodigo, Descripcion, Cantidad, PrecioUnitario, ItbisPorcentaje, Itbis, Total)
        SELECT @FacturaId, Linea, TasaCodigo, Descripcion, Cantidad, PrecioUnitario, ItbisPorcentaje, Itbis, Total FROM @Detalle;

        INSERT INTO caja.Pago (FacturaId, MedioPago, Monto, MontoRecibido, Cambio, Referencia, Autorizacion)
        VALUES (@FacturaId, @MedioPago, @Total, @MontoRecibido, @Cambio, @Referencia, @Autorizacion);

        SET @Usuario = (SELECT Username FROM seg.Usuario WHERE UsuarioId = @UsuarioId);
        DECLARE @Evento NVARCHAR(500) = CONCAT(@Encf, N' RD$', @Total, N' ', @MedioPago);
        EXEC caja.ppEvento_Registrar @Usuario = @Usuario, @Accion = N'Cobro', @Detalle = @Evento;
    COMMIT TRANSACTION;

    SELECT FacturaId, Tipo, Encf, CodigoSeguridad, Subtotal, Itbis, Total, @Cambio AS Cambio, Fecha, EstadoDgii
    FROM caja.Factura WHERE FacturaId = @FacturaId;
END;
GO

-- Nota de crédito E34 (anulación o devolución). Solo un supervisor de caja (clase FacturaCaja).
CREATE OR ALTER PROCEDURE caja.ppFactura_EmitirNotaCredito
    @FacturaId     BIGINT,
    @SesionId      INT,
    @SupervisorId  INT,
    @Motivo        NVARCHAR(300)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

    IF NOT EXISTS (SELECT 1 FROM seg.Usuario WHERE UsuarioId = @SupervisorId AND Rol IN (N'SupervisorCaja', N'Administrador') AND Activo = 1)
        THROW 58011, N'Solo un supervisor de caja puede emitir notas de crédito.', 1;
    IF LEN(ISNULL(@Motivo, N'')) < 10 THROW 58012, N'Indique el motivo de la nota de crédito (mínimo 10 caracteres).', 1;

    DECLARE @Numero BIGINT, @Encf CHAR(13), @NuevaId BIGINT, @Codigo CHAR(6), @Total DECIMAL(18,2), @Medio NVARCHAR(20);
    DECLARE @RncEmisor VARCHAR(11) = ISNULL((SELECT Valor FROM caja.Parametro WHERE Clave = 'RncEmisor'), '401000001');

    BEGIN TRANSACTION;
        IF NOT EXISTS (SELECT 1 FROM caja.Sesion WHERE SesionId = @SesionId AND Estado = N'Abierta')
            THROW 58003, N'La nota de crédito debe emitirse en una caja abierta.', 1;

        SELECT @Total = Total FROM caja.Factura WITH (UPDLOCK) WHERE FacturaId = @FacturaId AND Tipo <> '34' AND Anulada = 0;
        IF @Total IS NULL THROW 58013, N'El comprobante no existe, ya fue anulado o es una nota de crédito.', 1;
        SET @Medio = (SELECT TOP (1) MedioPago FROM caja.Pago WHERE FacturaId = @FacturaId);

        UPDATE caja.SecuenciaEcf WITH (UPDLOCK, HOLDLOCK)
        SET @Numero = Siguiente, Siguiente = Siguiente + 1
        WHERE Tipo = '34' AND Siguiente <= Hasta AND VenceEn >= CAST(SYSDATETIMEOFFSET() AS DATE);
        IF @@ROWCOUNT = 0 THROW 58009, N'La secuencia de notas de crédito está agotada o vencida.', 1;

        SET @Encf = CONCAT('E34', RIGHT(CONCAT('0000000000', @Numero), 10));
        SET @Codigo = LEFT(CONVERT(VARCHAR(64), HASHBYTES('SHA2_256',
                        CONCAT(@Encf, '|', @RncEmisor, '|', @Total, '|', CONVERT(VARCHAR(40), SYSDATETIMEOFFSET(), 126))), 2), 6);

        INSERT INTO caja.Factura (SesionId, Tipo, Encf, NumeroOrden, DocumentoCliente, NombreCliente, CorreoCliente,
                                  Subtotal, Itbis, Total, CodigoSeguridad, FacturaReferenciaId)
        SELECT @SesionId, '34', @Encf, NumeroOrden, DocumentoCliente, NombreCliente, CorreoCliente, Subtotal, Itbis, Total, @Codigo, FacturaId
        FROM caja.Factura WHERE FacturaId = @FacturaId;
        SET @NuevaId = SCOPE_IDENTITY();

        -- Las líneas de la nota de crédito no se envían al Core como pagos (NoAplica)
        INSERT INTO caja.LineaFactura (FacturaId, Linea, TasaCodigo, Descripcion, Cantidad, PrecioUnitario, ItbisPorcentaje, Itbis, Total, EstadoSincronizacion)
        SELECT @NuevaId, Linea, TasaCodigo, Descripcion, Cantidad, PrecioUnitario, ItbisPorcentaje, Itbis, Total, N'NoAplica'
        FROM caja.LineaFactura WHERE FacturaId = @FacturaId;

        -- La devolución sale de la caja por el mismo medio del cobro original
        INSERT INTO caja.Pago (FacturaId, MedioPago, Monto, Referencia) VALUES (@NuevaId, ISNULL(@Medio, N'Efectivo'), @Total, N'Devolución');
        UPDATE caja.Factura SET Anulada = 1 WHERE FacturaId = @FacturaId;

        DECLARE @Usuario NVARCHAR(50) = (SELECT Username FROM seg.Usuario WHERE UsuarioId = @SupervisorId);
        DECLARE @Detalle NVARCHAR(500) = CONCAT(@Encf, N' anula ', (SELECT Encf FROM caja.Factura WHERE FacturaId = @FacturaId), N': ', @Motivo);
        EXEC caja.ppEvento_Registrar @Usuario = @Usuario, @Accion = N'NotaCredito', @Detalle = @Detalle;
    COMMIT TRANSACTION;

    SELECT FacturaId, Tipo, Encf, CodigoSeguridad, Subtotal, Itbis, Total, CAST(NULL AS DECIMAL(18,2)) AS Cambio, Fecha, EstadoDgii
    FROM caja.Factura WHERE FacturaId = @NuevaId;
END;
GO

-- Comprobante completo: cabecera, líneas y pagos (para reimprimir).
CREATE OR ALTER PROCEDURE caja.ppFactura_Obtener
    @FacturaId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT f.FacturaId, f.SesionId, f.Tipo, f.Encf, f.NumeroOrden, f.DocumentoCliente, f.NombreCliente, f.CorreoCliente,
           f.Subtotal, f.Itbis, f.Total, f.CodigoSeguridad, f.EstadoDgii, f.TrackId, f.MensajeDgii, f.Anulada, f.Fecha,
           r.Encf AS EncfReferencia, c.Codigo AS CajaCodigo, u.Username AS Cajero
    FROM caja.Factura f
    INNER JOIN caja.Sesion s ON s.SesionId = f.SesionId
    INNER JOIN caja.Caja c ON c.CajaId = s.CajaId
    INNER JOIN seg.Usuario u ON u.UsuarioId = s.UsuarioId
    LEFT JOIN caja.Factura r ON r.FacturaId = f.FacturaReferenciaId
    WHERE f.FacturaId = @FacturaId;

    SELECT Linea, TasaCodigo, Descripcion, Cantidad, PrecioUnitario, ItbisPorcentaje, Itbis, Total, TransaccionId, EstadoSincronizacion
    FROM caja.LineaFactura WHERE FacturaId = @FacturaId ORDER BY Linea;

    SELECT MedioPago, Monto, MontoRecibido, Cambio, Referencia, Autorizacion FROM caja.Pago WHERE FacturaId = @FacturaId;
END;
GO

CREATE OR ALTER PROCEDURE caja.ppFactura_ListarSesion
    @SesionId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT f.FacturaId, f.Tipo, f.Encf, f.NombreCliente, f.Total, f.EstadoDgii, f.Anulada, f.Fecha,
           (SELECT TOP (1) MedioPago FROM caja.Pago p WHERE p.FacturaId = f.FacturaId) AS MedioPago
    FROM caja.Factura f WHERE f.SesionId = @SesionId ORDER BY f.FacturaId DESC;
END;
GO

/* ----------------------------- DGII (envío y contingencia) ----------------- */
CREATE OR ALTER PROCEDURE caja.ppFactura_PendientesDgii
    @Maximo INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Maximo) FacturaId, Encf, Tipo, DocumentoCliente, Total, Itbis, Fecha,
           ISNULL((SELECT Valor FROM caja.Parametro WHERE Clave = 'RncEmisor'), '401000001') AS RncEmisor
    FROM caja.Factura WHERE EstadoDgii IN (N'Pendiente', N'Contingencia') ORDER BY FacturaId;
END;
GO

CREATE OR ALTER PROCEDURE caja.ppFactura_RegistrarRespuestaDgii
    @FacturaId  BIGINT,
    @Estado     NVARCHAR(20),
    @TrackId    VARCHAR(64)   = NULL,
    @Mensaje    NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE caja.Factura SET EstadoDgii = @Estado, TrackId = COALESCE(@TrackId, TrackId), MensajeDgii = @Mensaje WHERE FacturaId = @FacturaId;
    IF @Estado = N'Rechazado'
    BEGIN
        DECLARE @Detalle NVARCHAR(500) = CONCAT((SELECT Encf FROM caja.Factura WHERE FacturaId = @FacturaId), N': ', @Mensaje);
        EXEC caja.ppEvento_Registrar @Usuario = N'sistema', @Accion = N'EcfRechazado', @Detalle = @Detalle;
    END
END;
GO

/* ----------------------------- SINCRONIZACIÓN CON INTEGRACIÓN -------------- */
-- Líneas cobradas que aún no llegan a Integración (fuera de línea o con error). Cada una es un pago para el Core.
CREATE OR ALTER PROCEDURE caja.ppLinea_PendientesSincronizar
    @Maximo INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Maximo) l.TransaccionId, f.Encf AS NumeroComprobante, l.TasaCodigo, l.Cantidad, l.Total,
           (SELECT TOP (1) p.MedioPago FROM caja.Pago p WHERE p.FacturaId = f.FacturaId) AS MedioPago,
           f.DocumentoCliente, f.NombreCliente, f.NumeroOrden, c.Codigo AS CajaCodigo, u.Username AS Cajero, f.Fecha,
           l.IntentosSincronizacion
    FROM caja.LineaFactura l
    INNER JOIN caja.Factura f ON f.FacturaId = l.FacturaId
    INNER JOIN caja.Sesion s ON s.SesionId = f.SesionId
    INNER JOIN caja.Caja c ON c.CajaId = s.CajaId
    INNER JOIN seg.Usuario u ON u.UsuarioId = s.UsuarioId
    WHERE l.EstadoSincronizacion = N'Pendiente' AND f.Tipo <> '34' AND l.IntentosSincronizacion < 100
    ORDER BY f.FacturaId, l.Linea;
END;
GO

-- Líneas ya recibidas por Integración cuyo resultado final en el Core aún no se conoce.
CREATE OR ALTER PROCEDURE caja.ppLinea_PendientesConfirmacion
    @Maximo INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Maximo) TransaccionId FROM caja.LineaFactura WHERE EstadoSincronizacion = N'Recibido' ORDER BY FacturaId, Linea;
END;
GO

-- @Estado: Recibido | Aplicado | Duplicado | Rechazado | Error (Error = sigue Pendiente y suma un intento)
CREATE OR ALTER PROCEDURE caja.ppLinea_RegistrarSincronizacion
    @TransaccionId  UNIQUEIDENTIFIER,
    @Estado         NVARCHAR(12),
    @Error          NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Estado = N'Error'
        UPDATE caja.LineaFactura SET IntentosSincronizacion = IntentosSincronizacion + 1, UltimoError = @Error
        WHERE TransaccionId = @TransaccionId;
    ELSE
        UPDATE caja.LineaFactura SET EstadoSincronizacion = @Estado, UltimoError = @Error, FechaSincronizacion = SYSDATETIMEOFFSET()
        WHERE TransaccionId = @TransaccionId;
END;
GO

-- Resumen para la barra de estado de la Caja: cuánto falta por sincronizar.
CREATE OR ALTER PROCEDURE caja.ppSincronizacion_Resumen
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        (SELECT COUNT(*) FROM caja.LineaFactura l INNER JOIN caja.Factura f ON f.FacturaId = l.FacturaId
         WHERE l.EstadoSincronizacion = N'Pendiente' AND f.Tipo <> '34') AS PagosPendientes,
        (SELECT COUNT(*) FROM caja.LineaFactura WHERE EstadoSincronizacion = N'Recibido') AS PagosPorConfirmar,
        (SELECT COUNT(*) FROM caja.LineaFactura WHERE EstadoSincronizacion = N'Rechazado') AS PagosRechazados,
        (SELECT COUNT(*) FROM caja.Factura WHERE EstadoDgii IN (N'Pendiente', N'Contingencia')) AS EcfEnContingencia,
        (SELECT MAX(FechaSincronizacion) FROM caja.Tasa) AS UltimaActualizacionTasas;
END;
GO

/* ----------------------------- ARQUEO Y CIERRE (RF-CAJ-03) ----------------- */
-- Arqueo ciego: guarda lo declarado y lo esperado; la sesión pasa a EnArqueo (ya no se puede cobrar).
CREATE OR ALTER PROCEDURE caja.ppSesion_Arquear
    @SesionId               INT,
    @UsuarioId              INT,
    @EfectivoDeclarado      DECIMAL(18,2),
    @TarjetaDeclarado       DECIMAL(18,2),
    @TransferenciaDeclarado DECIMAL(18,2)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
        DECLARE @Fondo DECIMAL(18,2);
        SELECT @Fondo = FondoInicial FROM caja.Sesion WITH (UPDLOCK)
        WHERE SesionId = @SesionId AND UsuarioId = @UsuarioId AND Estado = N'Abierta';
        IF @Fondo IS NULL THROW 58014, N'La sesión no existe, no es suya o ya está en arqueo.', 1;

        -- Cobros suman y notas de crédito (E34) restan, por medio de pago
        ;WITH Movimientos AS
        (
            SELECT p.MedioPago, CASE WHEN f.Tipo = '34' THEN -p.Monto ELSE p.Monto END AS Monto
            FROM caja.Pago p INNER JOIN caja.Factura f ON f.FacturaId = p.FacturaId
            WHERE f.SesionId = @SesionId
        )
        INSERT INTO caja.Arqueo (SesionId, EfectivoDeclarado, TarjetaDeclarado, TransferenciaDeclarado,
                                 EfectivoEsperado, TarjetaEsperado, TransferenciaEsperado)
        SELECT @SesionId, @EfectivoDeclarado, @TarjetaDeclarado, @TransferenciaDeclarado,
               @Fondo + ISNULL(SUM(CASE WHEN MedioPago = N'Efectivo' THEN Monto END), 0),
               ISNULL(SUM(CASE WHEN MedioPago = N'Tarjeta' THEN Monto END), 0),
               ISNULL(SUM(CASE WHEN MedioPago = N'Transferencia' THEN Monto END), 0)
        FROM Movimientos;

        UPDATE caja.Sesion SET Estado = N'EnArqueo' WHERE SesionId = @SesionId;
    COMMIT TRANSACTION;

    SELECT EfectivoDeclarado, TarjetaDeclarado, TransferenciaDeclarado, EfectivoEsperado, TarjetaEsperado, TransferenciaEsperado,
           EfectivoDeclarado - EfectivoEsperado AS DiferenciaEfectivo,
           TarjetaDeclarado - TarjetaEsperado AS DiferenciaTarjeta,
           TransferenciaDeclarado - TransferenciaEsperado AS DiferenciaTransferencia
    FROM caja.Arqueo WHERE SesionId = @SesionId;
END;
GO

-- Arqueo ya registrado de una sesión (para retomar el cierre si se cerró la ventana después de contar).
CREATE OR ALTER PROCEDURE caja.ppArqueo_Obtener
    @SesionId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT EfectivoDeclarado, TarjetaDeclarado, TransferenciaDeclarado, EfectivoEsperado, TarjetaEsperado, TransferenciaEsperado,
           EfectivoDeclarado - EfectivoEsperado AS DiferenciaEfectivo,
           TarjetaDeclarado - TarjetaEsperado AS DiferenciaTarjeta,
           TransferenciaDeclarado - TransferenciaEsperado AS DiferenciaTransferencia
    FROM caja.Arqueo WHERE SesionId = @SesionId;
END;
GO

-- Cierre aprobado por un supervisor. Diferencias por encima de la tolerancia exigen justificación.
CREATE OR ALTER PROCEDURE caja.ppSesion_Cerrar
    @SesionId       INT,
    @SupervisorId   INT,
    @Justificacion  NVARCHAR(500) = NULL,
    @Observaciones  NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Tolerancia DECIMAL(18,2) = ISNULL(TRY_CAST((SELECT Valor FROM caja.Parametro WHERE Clave = 'ToleranciaArqueo') AS DECIMAL(18,2)), 0);

    IF NOT EXISTS (SELECT 1 FROM seg.Usuario WHERE UsuarioId = @SupervisorId AND Rol IN (N'SupervisorCaja', N'Administrador') AND Activo = 1)
        THROW 58011, N'El cierre de caja debe aprobarlo un supervisor de caja.', 1;

    BEGIN TRANSACTION;
        IF NOT EXISTS (SELECT 1 FROM caja.Sesion WITH (UPDLOCK) WHERE SesionId = @SesionId AND Estado = N'EnArqueo')
            THROW 58015, N'Primero debe hacerse el arqueo de la caja.', 1;

        DECLARE @Diferencia DECIMAL(18,2) = (SELECT ABS(EfectivoDeclarado - EfectivoEsperado) + ABS(TarjetaDeclarado - TarjetaEsperado)
                                                    + ABS(TransferenciaDeclarado - TransferenciaEsperado)
                                             FROM caja.Arqueo WHERE SesionId = @SesionId);
        IF @Diferencia > @Tolerancia AND LEN(ISNULL(@Justificacion, N'')) < 10
            THROW 58016, N'Hay diferencias en el arqueo: escriba una justificación (mínimo 10 caracteres).', 1;

        UPDATE caja.Arqueo SET Justificacion = @Justificacion WHERE SesionId = @SesionId;
        UPDATE caja.Sesion SET Estado = N'Cerrada', FechaCierre = SYSDATETIMEOFFSET(), SupervisorId = @SupervisorId,
                               Observaciones = @Observaciones
        WHERE SesionId = @SesionId;

        DECLARE @Supervisor NVARCHAR(50) = (SELECT Username FROM seg.Usuario WHERE UsuarioId = @SupervisorId);
        DECLARE @Detalle NVARCHAR(500) = CONCAT(N'Sesión ', @SesionId, N' cerrada. Diferencia total RD$', @Diferencia);
        EXEC caja.ppEvento_Registrar @Usuario = @Supervisor, @Accion = N'CierreCaja', @Detalle = @Detalle;
    COMMIT TRANSACTION;

    -- Advertencia: comprobantes en contingencia aún sin confirmar por la DGII (se permiten, pero se listan)
    SELECT @Diferencia AS DiferenciaTotal,
           (SELECT COUNT(*) FROM caja.Factura WHERE SesionId = @SesionId AND EstadoDgii IN (N'Pendiente', N'Contingencia')) AS EcfEnContingencia;
END;
GO

-- Sesión no cerrada al final del día: cierre administrativo con registro de auditoría.
CREATE OR ALTER PROCEDURE caja.ppSesion_CerrarForzado
    @SesionId      INT,
    @SupervisorId  INT,
    @Motivo        NVARCHAR(500)
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM seg.Usuario WHERE UsuarioId = @SupervisorId AND Rol IN (N'SupervisorCaja', N'Administrador') AND Activo = 1)
        THROW 58011, N'Solo un supervisor de caja puede forzar un cierre.', 1;
    IF LEN(ISNULL(@Motivo, N'')) < 10 THROW 58012, N'Indique el motivo del cierre forzado (mínimo 10 caracteres).', 1;

    UPDATE caja.Sesion SET Estado = N'Cerrada', FechaCierre = SYSDATETIMEOFFSET(), SupervisorId = @SupervisorId,
                           CierreForzado = 1, Observaciones = @Motivo
    WHERE SesionId = @SesionId AND Estado <> N'Cerrada';
    IF @@ROWCOUNT = 0 THROW 58017, N'La sesión no existe o ya estaba cerrada.', 1;

    DECLARE @Supervisor NVARCHAR(50) = (SELECT Username FROM seg.Usuario WHERE UsuarioId = @SupervisorId);
    DECLARE @Detalle NVARCHAR(500) = CONCAT(N'Sesión ', @SesionId, N': ', @Motivo);
    EXEC caja.ppEvento_Registrar @Usuario = @Supervisor, @Accion = N'CierreForzado', @Detalle = @Detalle;
END;
GO

-- Sesiones abiertas desde un día anterior (para alertar al supervisor).
CREATE OR ALTER PROCEDURE caja.ppSesion_ListarSinCerrar
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.SesionId, c.Codigo AS CajaCodigo, u.Username AS Cajero, s.Estado, s.FechaApertura
    FROM caja.Sesion s INNER JOIN caja.Caja c ON c.CajaId = s.CajaId INNER JOIN seg.Usuario u ON u.UsuarioId = s.UsuarioId
    WHERE s.Estado <> N'Cerrada' AND CAST(s.FechaApertura AS DATE) < CAST(SYSDATETIMEOFFSET() AS DATE);
END;
GO

/* ----------------------------- REPORTES (datos para RDLC) ------------------ */
-- Acta de cierre: cabecera, totales por medio de pago y por tasa.
CREATE OR ALTER PROCEDURE caja.ppRptCierreCaja
    @SesionId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.SesionId, c.Codigo AS Caja, c.Sucursal, u.NombreCompleto AS Cajero, sup.NombreCompleto AS Supervisor,
           s.FondoInicial, s.FechaApertura, s.FechaCierre, s.Estado, s.CierreForzado, s.Observaciones,
           a.EfectivoDeclarado, a.EfectivoEsperado, a.TarjetaDeclarado, a.TarjetaEsperado,
           a.TransferenciaDeclarado, a.TransferenciaEsperado, a.Justificacion
    FROM caja.Sesion s
    INNER JOIN caja.Caja c ON c.CajaId = s.CajaId
    INNER JOIN seg.Usuario u ON u.UsuarioId = s.UsuarioId
    LEFT JOIN seg.Usuario sup ON sup.UsuarioId = s.SupervisorId
    LEFT JOIN caja.Arqueo a ON a.SesionId = s.SesionId
    WHERE s.SesionId = @SesionId;

    SELECT p.MedioPago, COUNT(*) AS Comprobantes, SUM(CASE WHEN f.Tipo = '34' THEN -p.Monto ELSE p.Monto END) AS Monto
    FROM caja.Pago p INNER JOIN caja.Factura f ON f.FacturaId = p.FacturaId
    WHERE f.SesionId = @SesionId GROUP BY p.MedioPago ORDER BY p.MedioPago;

    SELECT l.TasaCodigo, l.Descripcion, SUM(CASE WHEN f.Tipo = '34' THEN -l.Cantidad ELSE l.Cantidad END) AS Cantidad,
           SUM(CASE WHEN f.Tipo = '34' THEN -l.Total ELSE l.Total END) AS Total
    FROM caja.LineaFactura l INNER JOIN caja.Factura f ON f.FacturaId = l.FacturaId
    WHERE f.SesionId = @SesionId GROUP BY l.TasaCodigo, l.Descripcion ORDER BY l.TasaCodigo;
END;
GO

-- Ingresos por día y concepto (exportación contable).
CREATE OR ALTER PROCEDURE caja.ppRptIngresos
    @Desde DATE,
    @Hasta DATE
AS
BEGIN
    SET NOCOUNT ON;
    IF @Desde > @Hasta THROW 58018, N'La fecha inicial no puede ser mayor que la final.', 1;
    SELECT CAST(f.Fecha AS DATE) AS Dia, l.TasaCodigo, l.Descripcion,
           SUM(CASE WHEN f.Tipo = '34' THEN -l.Cantidad ELSE l.Cantidad END) AS Cantidad,
           SUM(CASE WHEN f.Tipo = '34' THEN -(l.Total - l.Itbis) ELSE l.Total - l.Itbis END) AS Subtotal,
           SUM(CASE WHEN f.Tipo = '34' THEN -l.Itbis ELSE l.Itbis END) AS Itbis,
           SUM(CASE WHEN f.Tipo = '34' THEN -l.Total ELSE l.Total END) AS Total
    FROM caja.LineaFactura l INNER JOIN caja.Factura f ON f.FacturaId = l.FacturaId
    WHERE CAST(f.Fecha AS DATE) BETWEEN @Desde AND @Hasta
    GROUP BY CAST(f.Fecha AS DATE), l.TasaCodigo, l.Descripcion
    ORDER BY Dia, l.TasaCodigo;
END;
GO

PRINT N'03 - Procedimientos de Caja creados.';
GO
