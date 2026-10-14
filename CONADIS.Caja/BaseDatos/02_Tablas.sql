/* =============================================================================
   CONADIS.Caja - 02: Tablas (RF-CAJ-01, RF-CAJ-02, RF-CAJ-03, clase FacturaCaja)
   ============================================================================= */
USE CONADIS_CajaDB;
GO

/* ----------------------------- LOG4NET ------------------------------------- */
IF OBJECT_ID(N'dbo.Log4NetLog', N'U') IS NULL
CREATE TABLE dbo.Log4NetLog
(
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Log4NetLog PRIMARY KEY,
    [Date]      DATETIME2(3)   NOT NULL,
    [Thread]    NVARCHAR(255)  NOT NULL,
    [Level]     NVARCHAR(50)   NOT NULL,
    [Logger]    NVARCHAR(255)  NOT NULL,
    [Message]   NVARCHAR(MAX)  NOT NULL,
    [Exception] NVARCHAR(MAX)  NULL
);
GO

/* ----------------------------- PARÁMETROS ---------------------------------- */
IF OBJECT_ID(N'caja.Parametro', N'U') IS NULL
CREATE TABLE caja.Parametro
(
    Clave        VARCHAR(60)   NOT NULL CONSTRAINT PK_Parametro PRIMARY KEY,
    Valor        NVARCHAR(200) NOT NULL,
    Descripcion  NVARCHAR(300) NOT NULL
);
GO

/* ----------------------------- USUARIOS DE CAJA ---------------------------- */
IF OBJECT_ID(N'seg.Usuario', N'U') IS NULL
CREATE TABLE seg.Usuario
(
    UsuarioId         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Usuario PRIMARY KEY,
    Username          NVARCHAR(50)  NOT NULL CONSTRAINT UQ_Usuario_Username UNIQUE,
    PasswordHash      VARCHAR(200)  NOT NULL,
    NombreCompleto    NVARCHAR(150) NOT NULL,
    Rol               NVARCHAR(20)  NOT NULL CONSTRAINT CK_Usuario_Rol CHECK (Rol IN (N'Cajero', N'SupervisorCaja', N'Administrador')),
    Activo            BIT NOT NULL CONSTRAINT DF_Usuario_Activo DEFAULT (1),
    IntentosFallidos  INT NOT NULL CONSTRAINT DF_Usuario_Intentos DEFAULT (0),
    BloqueadoHasta    DATETIMEOFFSET(0) NULL,
    UltimoAcceso      DATETIMEOFFSET(0) NULL
);
GO

/* ----------------------------- CAJAS --------------------------------------- */
IF OBJECT_ID(N'caja.Caja', N'U') IS NULL
CREATE TABLE caja.Caja
(
    CajaId    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Caja PRIMARY KEY,
    Codigo    VARCHAR(20)   NOT NULL CONSTRAINT UQ_Caja_Codigo UNIQUE,
    Sucursal  NVARCHAR(100) NOT NULL,
    Activa    BIT NOT NULL CONSTRAINT DF_Caja_Activa DEFAULT (1)
);
GO

/* ----------------------------- TASAS (copia local del catálogo del Core) --- */
-- Se actualiza desde Integración cuando hay conexión; sin conexión se cobra con la última copia.
-- El precio NUNCA se escribe a mano en el cobro: sale de aquí (RF-CAJ-01 paso 2, RINV-07).
IF OBJECT_ID(N'caja.Tasa', N'U') IS NULL
CREATE TABLE caja.Tasa
(
    Codigo              VARCHAR(20)   NOT NULL CONSTRAINT PK_Tasa PRIMARY KEY,
    Descripcion         NVARCHAR(200) NOT NULL,
    Monto               DECIMAL(18,2) NOT NULL CONSTRAINT CK_Tasa_Monto CHECK (Monto >= 0),
    ItbisPorcentaje     DECIMAL(5,2)  NOT NULL CONSTRAINT CK_Tasa_Itbis CHECK (ItbisPorcentaje BETWEEN 0 AND 100),
    Activa              BIT NOT NULL CONSTRAINT DF_Tasa_Activa DEFAULT (1),
    FechaSincronizacion DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Tasa_Fecha DEFAULT (SYSDATETIMEOFFSET())
);
GO

/* ----------------------------- SECUENCIAS e-NCF ---------------------------- */
-- Tipos: 31 crédito fiscal, 32 consumo, 34 nota de crédito, 45 gubernamental (RF-CAJ-02).
IF OBJECT_ID(N'caja.SecuenciaEcf', N'U') IS NULL
CREATE TABLE caja.SecuenciaEcf
(
    Tipo       CHAR(2)  NOT NULL CONSTRAINT PK_SecuenciaEcf PRIMARY KEY
               CONSTRAINT CK_Secuencia_Tipo CHECK (Tipo IN ('31', '32', '34', '45')),
    Siguiente  BIGINT   NOT NULL CONSTRAINT CK_Secuencia_Siguiente CHECK (Siguiente >= 1),
    Hasta      BIGINT   NOT NULL,
    VenceEn    DATE     NOT NULL,
    CONSTRAINT CK_Secuencia_Rango CHECK (Hasta <= 9999999999)
);
GO

/* ----------------------------- SESIONES DE CAJA ---------------------------- */
IF OBJECT_ID(N'caja.Sesion', N'U') IS NULL
CREATE TABLE caja.Sesion
(
    SesionId       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Sesion PRIMARY KEY,
    CajaId         INT NOT NULL CONSTRAINT FK_Sesion_Caja REFERENCES caja.Caja(CajaId),
    UsuarioId      INT NOT NULL CONSTRAINT FK_Sesion_Usuario REFERENCES seg.Usuario(UsuarioId),
    FondoInicial   DECIMAL(18,2) NOT NULL CONSTRAINT CK_Sesion_Fondo CHECK (FondoInicial >= 0),
    Estado         NVARCHAR(10)  NOT NULL CONSTRAINT DF_Sesion_Estado DEFAULT (N'Abierta')
                   CONSTRAINT CK_Sesion_Estado CHECK (Estado IN (N'Abierta', N'EnArqueo', N'Cerrada')),
    FechaApertura  DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Sesion_Apertura DEFAULT (SYSDATETIMEOFFSET()),
    FechaCierre    DATETIMEOFFSET(0) NULL,
    SupervisorId   INT NULL CONSTRAINT FK_Sesion_Supervisor REFERENCES seg.Usuario(UsuarioId),
    Observaciones  NVARCHAR(500) NULL,
    CierreForzado  BIT NOT NULL CONSTRAINT DF_Sesion_Forzado DEFAULT (0)
);
GO

-- Una sola sesión no cerrada por caja y por cajero (RF-CAJ-03 paso 1)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Sesion_CajaAbierta')
    CREATE UNIQUE INDEX UX_Sesion_CajaAbierta ON caja.Sesion (CajaId) WHERE Estado <> N'Cerrada';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Sesion_CajeroAbierta')
    CREATE UNIQUE INDEX UX_Sesion_CajeroAbierta ON caja.Sesion (UsuarioId) WHERE Estado <> N'Cerrada';
GO

/* ----------------------------- FACTURAS (e-CF) ----------------------------- */
IF OBJECT_ID(N'caja.Factura', N'U') IS NULL
CREATE TABLE caja.Factura
(
    FacturaId            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Factura PRIMARY KEY,
    SesionId             INT NOT NULL CONSTRAINT FK_Factura_Sesion REFERENCES caja.Sesion(SesionId),
    Tipo                 CHAR(2)  NOT NULL CONSTRAINT CK_Factura_Tipo CHECK (Tipo IN ('31', '32', '34', '45')),
    Encf                 CHAR(13) NOT NULL CONSTRAINT UQ_Factura_Encf UNIQUE,
    NumeroOrden          VARCHAR(20)   NULL,        -- orden de cobro del Core (si el cobro vino de un trámite)
    DocumentoCliente     VARCHAR(11)   NOT NULL,    -- cédula (11) o RNC (9)
    NombreCliente        NVARCHAR(150) NOT NULL,
    CorreoCliente        NVARCHAR(150) NULL,
    Subtotal             DECIMAL(18,2) NOT NULL,
    Itbis                DECIMAL(18,2) NOT NULL,
    Total                DECIMAL(18,2) NOT NULL CONSTRAINT CK_Factura_Total CHECK (Total >= 0),
    CodigoSeguridad      CHAR(6)  NOT NULL,
    EstadoDgii           NVARCHAR(20) NOT NULL CONSTRAINT DF_Factura_Dgii DEFAULT (N'Pendiente')
                         CONSTRAINT CK_Factura_Dgii CHECK (EstadoDgii IN (N'Pendiente', N'Aceptado', N'AceptadoCondicional', N'Rechazado', N'Contingencia')),
    TrackId              VARCHAR(64)   NULL,
    MensajeDgii          NVARCHAR(500) NULL,
    FacturaReferenciaId  BIGINT NULL CONSTRAINT FK_Factura_Referencia REFERENCES caja.Factura(FacturaId),   -- E34
    Anulada              BIT NOT NULL CONSTRAINT DF_Factura_Anulada DEFAULT (0),
    Fecha                DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Factura_Fecha DEFAULT (SYSDATETIMEOFFSET()),
    RowVersion           ROWVERSION NOT NULL
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Factura_Sesion')
    CREATE INDEX IX_Factura_Sesion ON caja.Factura (SesionId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Factura_Dgii')
    CREATE INDEX IX_Factura_Dgii ON caja.Factura (EstadoDgii) WHERE EstadoDgii IN (N'Pendiente', N'Contingencia');
GO

-- Cada línea se entrega al Core como un pago (con su propio TransaccionId: reenviarla no la duplica).
IF OBJECT_ID(N'caja.LineaFactura', N'U') IS NULL
CREATE TABLE caja.LineaFactura
(
    FacturaId              BIGINT NOT NULL CONSTRAINT FK_Linea_Factura REFERENCES caja.Factura(FacturaId),
    Linea                  INT NOT NULL,
    TasaCodigo             VARCHAR(20)   NOT NULL,
    Descripcion            NVARCHAR(200) NOT NULL,
    Cantidad               INT NOT NULL CONSTRAINT CK_Linea_Cantidad CHECK (Cantidad BETWEEN 1 AND 100),
    PrecioUnitario         DECIMAL(18,2) NOT NULL,
    ItbisPorcentaje        DECIMAL(5,2)  NOT NULL,
    Itbis                  DECIMAL(18,2) NOT NULL,
    Total                  DECIMAL(18,2) NOT NULL,
    TransaccionId          UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Linea_Transaccion DEFAULT (NEWID())
                           CONSTRAINT UQ_Linea_Transaccion UNIQUE,
    EstadoSincronizacion   NVARCHAR(12) NOT NULL CONSTRAINT DF_Linea_Sync DEFAULT (N'Pendiente')
                           CONSTRAINT CK_Linea_Sync CHECK (EstadoSincronizacion IN
                               (N'Pendiente', N'Recibido', N'Aplicado', N'Duplicado', N'Rechazado', N'NoAplica')),
    IntentosSincronizacion INT NOT NULL CONSTRAINT DF_Linea_Intentos DEFAULT (0),
    UltimoError            NVARCHAR(500) NULL,
    FechaSincronizacion    DATETIMEOFFSET(0) NULL,
    CONSTRAINT PK_LineaFactura PRIMARY KEY (FacturaId, Linea)
);
GO

IF OBJECT_ID(N'caja.Pago', N'U') IS NULL
CREATE TABLE caja.Pago
(
    PagoId         BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Pago PRIMARY KEY,
    FacturaId      BIGINT NOT NULL CONSTRAINT FK_Pago_Factura REFERENCES caja.Factura(FacturaId),
    MedioPago      NVARCHAR(20)  NOT NULL CONSTRAINT CK_Pago_Medio CHECK (MedioPago IN (N'Efectivo', N'Tarjeta', N'Transferencia')),
    Monto          DECIMAL(18,2) NOT NULL CONSTRAINT CK_Pago_Monto CHECK (Monto >= 0),
    MontoRecibido  DECIMAL(18,2) NULL,      -- solo efectivo
    Cambio         DECIMAL(18,2) NULL,
    Referencia     NVARCHAR(50)  NULL,      -- número de transferencia o del voucher
    Autorizacion   NVARCHAR(20)  NULL       -- código de autorización del POS
);
GO

/* ----------------------------- ARQUEOS ------------------------------------- */
-- Arqueo ciego: el cajero declara lo que contó sin ver lo esperado (RF-CAJ-03 paso 2).
IF OBJECT_ID(N'caja.Arqueo', N'U') IS NULL
CREATE TABLE caja.Arqueo
(
    SesionId                INT NOT NULL CONSTRAINT PK_Arqueo PRIMARY KEY CONSTRAINT FK_Arqueo_Sesion REFERENCES caja.Sesion(SesionId),
    EfectivoDeclarado       DECIMAL(18,2) NOT NULL,
    TarjetaDeclarado        DECIMAL(18,2) NOT NULL,
    TransferenciaDeclarado  DECIMAL(18,2) NOT NULL,
    EfectivoEsperado        DECIMAL(18,2) NOT NULL,
    TarjetaEsperado         DECIMAL(18,2) NOT NULL,
    TransferenciaEsperado   DECIMAL(18,2) NOT NULL,
    Justificacion           NVARCHAR(500) NULL,
    FechaArqueo             DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Arqueo_Fecha DEFAULT (SYSDATETIMEOFFSET())
);
GO

/* ----------------------------- AUDITORÍA DE CAJA --------------------------- */
IF OBJECT_ID(N'caja.Evento', N'U') IS NULL
CREATE TABLE caja.Evento
(
    EventoId   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Evento PRIMARY KEY,
    Fecha      DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_Evento_Fecha DEFAULT (SYSDATETIMEOFFSET()),
    Usuario    NVARCHAR(50)  NOT NULL,
    Accion     NVARCHAR(50)  NOT NULL,
    Detalle    NVARCHAR(500) NULL
);
GO

CREATE OR ALTER TRIGGER caja.TR_Evento_SoloInsercion
ON caja.Evento
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 58900, N'La auditoría de caja no permite modificar ni borrar registros.', 1;
END;
GO

-- Ninguna factura puede asociarse a una sesión cerrada (RF-CAJ-03 paso 4)
CREATE OR ALTER TRIGGER caja.TR_Factura_SesionAbierta
ON caja.Factura
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i INNER JOIN caja.Sesion s ON s.SesionId = i.SesionId WHERE s.Estado = N'Cerrada')
        THROW 58901, N'No se pueden emitir comprobantes en una sesión de caja cerrada.', 1;
END;
GO

PRINT N'02 - Tablas de Caja creadas.';
GO
