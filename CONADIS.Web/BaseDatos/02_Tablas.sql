/* =============================================================================
   CONADIS.Web - 02: Tablas del Portal Ciudadano
   ============================================================================= */
USE CONADIS_WebDB;
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
IF OBJECT_ID(N'web.Parametro', N'U') IS NULL
CREATE TABLE web.Parametro
(
    Clave        VARCHAR(60)   NOT NULL CONSTRAINT PK_Parametro PRIMARY KEY,
    Valor        NVARCHAR(200) NOT NULL,
    Descripcion  NVARCHAR(300) NOT NULL
);
GO

/* ----------------------------- CUENTAS DEL PORTAL -------------------------- */
-- Una cuenta por cédula. La identidad se valida con la JCE (vía Integración) al registrarse.
-- La contraseña se guarda con PBKDF2-SHA256 (mínimo 12 caracteres, RNF-SEG-03).
IF OBJECT_ID(N'web.Cuenta', N'U') IS NULL
CREATE TABLE web.Cuenta
(
    CuentaId          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Cuenta PRIMARY KEY,
    Cedula            CHAR(11)       NOT NULL CONSTRAINT UQ_Cuenta_Cedula UNIQUE
                                     CONSTRAINT CK_Cuenta_Cedula CHECK (Cedula NOT LIKE '%[^0-9]%'),
    Correo            NVARCHAR(150)  NOT NULL CONSTRAINT UQ_Cuenta_Correo UNIQUE,
    Telefono          NVARCHAR(20)   NULL,
    NombreCompleto    NVARCHAR(200)  NOT NULL,
    PasswordHash      VARCHAR(200)   NOT NULL,
    EstadoIdentidad   NVARCHAR(20)   NOT NULL CONSTRAINT CK_Cuenta_Identidad CHECK (EstadoIdentidad IN (N'Validada', N'Pendiente')),
    CorreoVerificado  BIT            NOT NULL CONSTRAINT DF_Cuenta_Verificado DEFAULT (0),
    TokenVerificacion CHAR(64)       NULL,      -- SHA-256 del enlace enviado por correo (nunca el token en claro)
    VenceVerificacion DATETIMEOFFSET(0) NULL,
    Activa            BIT            NOT NULL CONSTRAINT DF_Cuenta_Activa DEFAULT (1),
    IntentosFallidos  INT            NOT NULL CONSTRAINT DF_Cuenta_Intentos DEFAULT (0),
    BloqueadoHasta    DATETIMEOFFSET(0) NULL,
    UltimoAcceso      DATETIMEOFFSET(0) NULL,
    FechaCreacion     DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Cuenta_Fecha DEFAULT (SYSDATETIMEOFFSET()),
    RowVersion        ROWVERSION     NOT NULL
);
GO

/* ----------------------------- PREFERENCIAS DE ACCESIBILIDAD --------------- */
-- RIU-03: el conmutador de alto contraste y el tamaño de texto se recuerdan por cuenta.
IF OBJECT_ID(N'web.Preferencia', N'U') IS NULL
CREATE TABLE web.Preferencia
(
    CuentaId      INT NOT NULL CONSTRAINT PK_Preferencia PRIMARY KEY
                  CONSTRAINT FK_Preferencia_Cuenta REFERENCES web.Cuenta(CuentaId),
    AltoContraste BIT NOT NULL CONSTRAINT DF_Preferencia_Contraste DEFAULT (0),
    TamanoTexto   INT NOT NULL CONSTRAINT DF_Preferencia_Tamano DEFAULT (100)
                  CONSTRAINT CK_Preferencia_Tamano CHECK (TamanoTexto BETWEEN 100 AND 200),
    FechaCambio   DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Preferencia_Fecha DEFAULT (SYSDATETIMEOFFSET())
);
GO

/* ----------------------------- RECUPERACIÓN DE CONTRASEÑA ------------------ */
IF OBJECT_ID(N'web.Recuperacion', N'U') IS NULL
CREATE TABLE web.Recuperacion
(
    TokenHash      CHAR(64) NOT NULL CONSTRAINT PK_Recuperacion PRIMARY KEY,
    CuentaId       INT NOT NULL CONSTRAINT FK_Recuperacion_Cuenta REFERENCES web.Cuenta(CuentaId),
    VenceEn        DATETIMEOFFSET(0) NOT NULL,
    Usado          BIT NOT NULL CONSTRAINT DF_Recuperacion_Usado DEFAULT (0),
    FechaCreacion  DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Recuperacion_Fecha DEFAULT (SYSDATETIMEOFFSET())
);
GO

/* ----------------------------- BORRADORES DE SOLICITUD --------------------- */
-- El formulario de solicitud tiene varios pasos (identificación, diagnóstico, cita, contacto,
-- revisión). Lo escrito se guarda aquí para continuar más tarde sin perderlo.
IF OBJECT_ID(N'web.Borrador', N'U') IS NULL
CREATE TABLE web.Borrador
(
    CuentaId            INT NOT NULL CONSTRAINT FK_Borrador_Cuenta REFERENCES web.Cuenta(CuentaId),
    Tipo                NVARCHAR(30) NOT NULL,    -- Certificacion | Renovacion | Duplicado | AyudaTecnica | Accesibilidad
    Paso                INT NOT NULL CONSTRAINT CK_Borrador_Paso CHECK (Paso BETWEEN 1 AND 10),
    Datos               NVARCHAR(MAX) NOT NULL CONSTRAINT CK_Borrador_Datos CHECK (ISJSON(Datos) = 1),
    ClaveIdempotencia   VARCHAR(100) NOT NULL,    -- se envía a Integración: "Enviar" dos veces no crea dos solicitudes
    FechaActualizacion  DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Borrador_Fecha DEFAULT (SYSDATETIMEOFFSET()),
    CONSTRAINT PK_Borrador PRIMARY KEY (CuentaId, Tipo)
);
GO

/* ----------------------------- EVENTOS DE SEGURIDAD ------------------------ */
-- Inicios de sesión, bloqueos, cambios de contraseña... (solo inserción, RNF-SEG-06).
IF OBJECT_ID(N'web.EventoSeguridad', N'U') IS NULL
CREATE TABLE web.EventoSeguridad
(
    EventoId   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EventoSeguridad PRIMARY KEY,
    Fecha      DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_Evento_Fecha DEFAULT (SYSDATETIMEOFFSET()),
    CuentaId   INT NULL,
    Evento     NVARCHAR(50)  NOT NULL,
    Ip         VARCHAR(45)   NULL,
    Detalle    NVARCHAR(300) NULL
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EventoSeguridad_Cuenta')
    CREATE INDEX IX_EventoSeguridad_Cuenta ON web.EventoSeguridad (CuentaId, Fecha);
GO

CREATE OR ALTER TRIGGER web.TR_EventoSeguridad_SoloInsercion
ON web.EventoSeguridad
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 57900, N'Los eventos de seguridad no se pueden modificar ni borrar.', 1;
END;
GO

PRINT N'02 - Tablas del Portal creadas.';
GO
