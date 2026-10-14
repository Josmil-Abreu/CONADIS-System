/* =============================================================================
   CONADIS.Integracion - 02: Tablas
   Reglas aplicadas:
     - Claves foráneas declaradas, sin borrado en cascada.
     - CHECK en todos los dominios cerrados (estados, orígenes, medios de pago).
     - JSON validado con ISJSON en las columnas que guardan cuerpos de mensajes.
     - Fechas en UTC (DATETIME2 + SYSUTCDATETIME), montos en DECIMAL(18,2).
     - La bitácora es inmutable: un trigger rechaza UPDATE y DELETE.
   Se puede ejecutar varias veces: solo crea lo que falta.
   ============================================================================= */
USE CONADIS_IntegracionDB;
GO

-- Necesarios para índices filtrados y JSON (Visual Studio ya los trae activos; sqlcmd no)
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* ------------------------------- LOG4NET ----------------------------------- */
IF OBJECT_ID(N'dbo.Log4NetLog', N'U') IS NULL
CREATE TABLE dbo.Log4NetLog
(
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Log4NetLog PRIMARY KEY,
    [Date]      DATETIME2(3)  NOT NULL,
    [Thread]    NVARCHAR(255) NOT NULL,
    [Level]     NVARCHAR(50)  NOT NULL,
    [Logger]    NVARCHAR(255) NOT NULL,
    [Message]   NVARCHAR(MAX) NOT NULL,   -- MAX: un stack trace pasa de 4000 caracteres
    [Exception] NVARCHAR(MAX) NULL
);
GO

/* ------------------------------- PARÁMETROS -------------------------------- */
IF OBJECT_ID(N'cat.Parametro', N'U') IS NULL
CREATE TABLE cat.Parametro
(
    Clave       NVARCHAR(50)  NOT NULL CONSTRAINT PK_Parametro PRIMARY KEY,
    Valor       NVARCHAR(200) NOT NULL,
    Descripcion NVARCHAR(300) NOT NULL
);
GO

/* ------------------------------- CLIENTES ---------------------------------- */
-- Cada módulo que llama a Integración (WEB, CAJA) se identifica con una API key.
-- Solo se guarda su SHA-256 en hexadecimal; la clave en claro nunca toca la base.
IF OBJECT_ID(N'seg.ClienteApi', N'U') IS NULL
CREATE TABLE seg.ClienteApi
(
    ClienteId     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ClienteApi PRIMARY KEY,
    Codigo        NVARCHAR(20)  NOT NULL CONSTRAINT UQ_ClienteApi_Codigo UNIQUE,
    Nombre        NVARCHAR(100) NOT NULL,
    ApiKeyHash    CHAR(64)      NOT NULL CONSTRAINT UQ_ClienteApi_Hash UNIQUE,
    Activo        BIT           NOT NULL CONSTRAINT DF_ClienteApi_Activo DEFAULT (1),
    FechaCreacion DATETIME2(3)  NOT NULL CONSTRAINT DF_ClienteApi_Fecha DEFAULT (SYSUTCDATETIME()),
    UltimoUso     DATETIME2(3)  NULL,
    CONSTRAINT CK_ClienteApi_Hash CHECK (ApiKeyHash NOT LIKE '%[^0-9A-F]%')
);
GO

/* ------------------------- SERVICIOS Y CORTACIRCUITO ----------------------- */
-- Destinos a los que Integración llama: el Core y los servicios externos.
-- El cortacircuito vive aquí (y no en memoria) para que la API y el Servicio de
-- Windows compartan el mismo estado.
IF OBJECT_ID(N'snd.Servicio', N'U') IS NULL
CREATE TABLE snd.Servicio
(
    ServicioId         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Servicio PRIMARY KEY,
    Codigo             NVARCHAR(20)  NOT NULL CONSTRAINT UQ_Servicio_Codigo UNIQUE,
    Nombre             NVARCHAR(100) NOT NULL,
    UrlBase            NVARCHAR(300) NULL,
    EsSimulado         BIT NOT NULL CONSTRAINT DF_Servicio_Simulado DEFAULT (1),
    EstadoCircuito     NVARCHAR(15)  NOT NULL CONSTRAINT DF_Servicio_Estado DEFAULT (N'Cerrado'),
    FallosConsecutivos INT NOT NULL CONSTRAINT DF_Servicio_Fallos DEFAULT (0),
    UmbralFallos       INT NOT NULL CONSTRAINT DF_Servicio_Umbral DEFAULT (5),
    SegundosApertura   INT NOT NULL CONSTRAINT DF_Servicio_Apertura DEFAULT (60),
    AbiertoHasta       DATETIME2(3) NULL,
    UltimoExito        DATETIME2(3) NULL,
    UltimoFallo        DATETIME2(3) NULL,
    Activo             BIT NOT NULL CONSTRAINT DF_Servicio_Activo DEFAULT (1),
    CONSTRAINT CK_Servicio_Estado   CHECK (EstadoCircuito IN (N'Cerrado', N'Abierto', N'SemiAbierto')),
    CONSTRAINT CK_Servicio_Umbral   CHECK (UmbralFallos BETWEEN 1 AND 100),
    CONSTRAINT CK_Servicio_Apertura CHECK (SegundosApertura BETWEEN 5 AND 3600),
    CONSTRAINT CK_Servicio_Fallos   CHECK (FallosConsecutivos >= 0)
);
GO

/* --------------------------- COLA DE MENSAJES (OUTBOX) --------------------- */
-- Todo lo que debe llegar a otro servicio pasa por aquí. Si el destino está caído,
-- el mensaje espera y el Servicio de Windows lo reintenta con espera exponencial.
IF OBJECT_ID(N'snd.Mensaje', N'U') IS NULL
CREATE TABLE snd.Mensaje
(
    MensajeId          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Mensaje PRIMARY KEY,
    MensajeGuid        UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Mensaje_Guid DEFAULT (NEWID())
                                             CONSTRAINT UQ_Mensaje_Guid UNIQUE,
    ClienteId          INT NULL CONSTRAINT FK_Mensaje_Cliente REFERENCES seg.ClienteApi (ClienteId),
    ServicioDestinoId  INT NOT NULL CONSTRAINT FK_Mensaje_Servicio REFERENCES snd.Servicio (ServicioId),
    Tipo               NVARCHAR(60)  NOT NULL,          -- Pago.Registrar, Solicitud.Crear, Evento.Caja...
    ClaveIdempotencia  NVARCHAR(100) NULL,              -- evita encolar dos veces lo mismo
    Payload            NVARCHAR(MAX) NOT NULL,
    Estado             NVARCHAR(15)  NOT NULL CONSTRAINT DF_Mensaje_Estado DEFAULT (N'Pendiente'),
    Intentos           INT NOT NULL CONSTRAINT DF_Mensaje_Intentos DEFAULT (0),
    MaxIntentos        INT NOT NULL,
    ProximoIntento     DATETIME2(3) NOT NULL CONSTRAINT DF_Mensaje_Proximo DEFAULT (SYSUTCDATETIME()),
    BloqueadoHasta     DATETIME2(3) NULL,               -- el trabajador que lo tomó lo "alquila" hasta esta hora
    ProcesadoPor       NVARCHAR(100) NULL,
    UltimoError        NVARCHAR(MAX) NULL,
    RespuestaJson      NVARCHAR(MAX) NULL,
    CorrelacionId      UNIQUEIDENTIFIER NULL,
    FechaCreacion      DATETIME2(3) NOT NULL CONSTRAINT DF_Mensaje_Fecha DEFAULT (SYSUTCDATETIME()),
    FechaEntrega       DATETIME2(3) NULL,
    Version            ROWVERSION NOT NULL,
    CONSTRAINT CK_Mensaje_Estado      CHECK (Estado IN (N'Pendiente', N'EnProceso', N'Entregado', N'Muerto')),
    CONSTRAINT CK_Mensaje_Payload     CHECK (ISJSON(Payload) = 1),
    CONSTRAINT CK_Mensaje_Respuesta   CHECK (RespuestaJson IS NULL OR ISJSON(RespuestaJson) = 1),
    CONSTRAINT CK_Mensaje_Intentos    CHECK (Intentos >= 0),
    CONSTRAINT CK_Mensaje_MaxIntentos CHECK (MaxIntentos BETWEEN 1 AND 100)
);
GO

-- Lo que busca el trabajador en cada vuelta: pendientes cuya hora ya llegó
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Mensaje_Cola' AND object_id = OBJECT_ID(N'snd.Mensaje'))
CREATE INDEX IX_Mensaje_Cola ON snd.Mensaje (Estado, ProximoIntento) INCLUDE (ServicioDestinoId, BloqueadoHasta);
GO

-- Un mismo cliente no puede encolar dos veces el mismo tipo con la misma clave
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Mensaje_Idempotencia' AND object_id = OBJECT_ID(N'snd.Mensaje'))
CREATE UNIQUE INDEX UX_Mensaje_Idempotencia ON snd.Mensaje (ServicioDestinoId, Tipo, ClaveIdempotencia)
    WHERE ClaveIdempotencia IS NOT NULL;
GO

/* ------------------------------ IDEMPOTENCIA ------------------------------- */
-- Guarda la respuesta de cada petición con encabezado Idempotency-Key.
-- Si el cliente repite la petición (por ejemplo, tras perder la conexión),
-- recibe la misma respuesta y la operación no se ejecuta dos veces.
IF OBJECT_ID(N'snd.Idempotencia', N'U') IS NULL
CREATE TABLE snd.Idempotencia
(
    ClienteId      INT NOT NULL CONSTRAINT FK_Idempotencia_Cliente REFERENCES seg.ClienteApi (ClienteId),
    Clave          NVARCHAR(100) NOT NULL,
    HashSolicitud  CHAR(64)      NOT NULL,              -- SHA-256 del cuerpo: misma clave + otro cuerpo = conflicto
    Estado         NVARCHAR(15)  NOT NULL,
    CodigoHttp     INT NULL,
    RespuestaJson  NVARCHAR(MAX) NULL,
    FechaCreacion  DATETIME2(3) NOT NULL CONSTRAINT DF_Idempotencia_Fecha DEFAULT (SYSUTCDATETIME()),
    FechaExpira    DATETIME2(3) NOT NULL,
    CONSTRAINT PK_Idempotencia PRIMARY KEY (ClienteId, Clave),
    CONSTRAINT CK_Idempotencia_Estado    CHECK (Estado IN (N'EnProceso', N'Completado')),
    CONSTRAINT CK_Idempotencia_Respuesta CHECK (RespuestaJson IS NULL OR ISJSON(RespuestaJson) = 1),
    CONSTRAINT CK_Idempotencia_Codigo    CHECK (CodigoHttp IS NULL OR CodigoHttp BETWEEN 100 AND 599)
);
GO

/* ----------------------------- PAGOS RECIBIDOS ----------------------------- */
-- Cobros de la Caja y pagos en línea del portal. TransaccionId lo genera el origen:
-- si el mismo cobro llega dos veces (reenvío tras una caída), no se duplica.
IF OBJECT_ID(N'snd.PagoRecibido', N'U') IS NULL
CREATE TABLE snd.PagoRecibido
(
    PagoId             BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PagoRecibido PRIMARY KEY,
    TransaccionId      UNIQUEIDENTIFIER NOT NULL CONSTRAINT UQ_Pago_Transaccion UNIQUE,
    ClienteId          INT NOT NULL CONSTRAINT FK_Pago_Cliente REFERENCES seg.ClienteApi (ClienteId),
    Origen             NVARCHAR(10)  NOT NULL,
    Caja               NVARCHAR(60)  NULL,
    Cajero             NVARCHAR(100) NULL,
    NumeroComprobante  NVARCHAR(30)  NOT NULL,
    TasaCodigo         NVARCHAR(40)  NOT NULL,
    Concepto           NVARCHAR(200) NOT NULL,
    Cantidad           INT NOT NULL,
    MontoUnitario      DECIMAL(18,2) NOT NULL,
    Total              DECIMAL(18,2) NOT NULL,
    MedioPago          NVARCHAR(20)  NOT NULL,
    Documento          NVARCHAR(11)  NOT NULL,
    NombrePagador      NVARCHAR(200) NOT NULL,
    NumeroOrden        NVARCHAR(30)  NULL,
    Referencia         NVARCHAR(100) NULL,
    FechaCobro         DATETIME2(3)  NOT NULL,
    FechaRecepcion     DATETIME2(3)  NOT NULL CONSTRAINT DF_Pago_Recepcion DEFAULT (SYSUTCDATETIME()),
    Estado             NVARCHAR(15)  NOT NULL CONSTRAINT DF_Pago_Estado DEFAULT (N'Recibido'),
    MensajeId          BIGINT NULL CONSTRAINT FK_Pago_Mensaje REFERENCES snd.Mensaje (MensajeId),
    FechaAplicacion    DATETIME2(3)  NULL,
    Observacion        NVARCHAR(500) NULL,
    CONSTRAINT CK_Pago_Origen    CHECK (Origen IN (N'CAJA', N'WEB')),
    CONSTRAINT CK_Pago_Medio     CHECK (MedioPago IN (N'Efectivo', N'Tarjeta', N'Transferencia', N'EnLinea')),
    CONSTRAINT CK_Pago_Estado    CHECK (Estado IN (N'Recibido', N'Aplicado', N'Rechazado')),
    CONSTRAINT CK_Pago_Cantidad  CHECK (Cantidad BETWEEN 1 AND 1000),
    CONSTRAINT CK_Pago_Montos    CHECK (MontoUnitario >= 0 AND Total >= 0),
    CONSTRAINT CK_Pago_Documento CHECK (Documento NOT LIKE '%[^0-9]%' AND LEN(Documento) IN (9, 11)),
    CONSTRAINT CK_Pago_CajaDatos CHECK (Origen <> N'CAJA' OR (Caja IS NOT NULL AND Cajero IS NOT NULL))
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Pago_Fecha' AND object_id = OBJECT_ID(N'snd.PagoRecibido'))
CREATE INDEX IX_Pago_Fecha ON snd.PagoRecibido (FechaCobro) INCLUDE (Origen, Total, Estado);
GO

/* --------------------------------- CACHÉ ----------------------------------- */
-- Última respuesta conocida de cada consulta. Si el servicio está caído,
-- Integración responde con este valor (aunque esté vencido) y lo indica.
IF OBJECT_ID(N'snd.Cache', N'U') IS NULL
CREATE TABLE snd.Cache
(
    Clave              NVARCHAR(200) NOT NULL CONSTRAINT PK_Cache PRIMARY KEY,
    ServicioId         INT NOT NULL CONSTRAINT FK_Cache_Servicio REFERENCES snd.Servicio (ServicioId),
    ValorJson          NVARCHAR(MAX) NOT NULL,
    FechaActualizacion DATETIME2(3) NOT NULL,
    FechaExpira        DATETIME2(3) NOT NULL,
    CONSTRAINT CK_Cache_Valor CHECK (ISJSON(ValorJson) = 1),
    CONSTRAINT CK_Cache_Fechas CHECK (FechaExpira >= FechaActualizacion)
);
GO

/* -------------------------------- BITÁCORA --------------------------------- */
-- Registro de cada llamada que entra o sale de Integración. Es la base de los
-- reportes de disponibilidad y errores. No se puede modificar ni borrar.
IF OBJECT_ID(N'snd.Bitacora', N'U') IS NULL
CREATE TABLE snd.Bitacora
(
    BitacoraId      BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Bitacora PRIMARY KEY,
    Fecha           DATETIME2(3) NOT NULL CONSTRAINT DF_Bitacora_Fecha DEFAULT (SYSUTCDATETIME()),
    CorrelacionId   UNIQUEIDENTIFIER NULL,
    ClienteId       INT NULL CONSTRAINT FK_Bitacora_Cliente REFERENCES seg.ClienteApi (ClienteId),
    ServicioId      INT NULL CONSTRAINT FK_Bitacora_Servicio REFERENCES snd.Servicio (ServicioId),
    Direccion       NVARCHAR(10)  NOT NULL,
    Operacion       NVARCHAR(100) NOT NULL,
    CodigoResultado INT NULL,
    Exitoso         BIT NOT NULL,
    DuracionMs      INT NOT NULL,
    Detalle         NVARCHAR(MAX) NULL,
    CONSTRAINT CK_Bitacora_Direccion CHECK (Direccion IN (N'Entrante', N'Saliente')),
    CONSTRAINT CK_Bitacora_Duracion  CHECK (DuracionMs >= 0)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Bitacora_Fecha' AND object_id = OBJECT_ID(N'snd.Bitacora'))
CREATE INDEX IX_Bitacora_Fecha ON snd.Bitacora (Fecha) INCLUDE (ServicioId, Exitoso, DuracionMs, Direccion);
GO

CREATE OR ALTER TRIGGER snd.trBitacora_Inmutable
ON snd.Bitacora
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 50090, N'La bitácora de Integración es inmutable: no se puede modificar ni borrar.', 1;
END;
GO

PRINT N'02 - Tablas de CONADIS_IntegracionDB listas.';
GO
