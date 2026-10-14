/* =============================================================================
   CONADIS.Integracion - 02: Tablas
   Lo que guarda Integración (el "Sistema Nervioso Digital" entre canales y Core):
     - Quién puede llamarla (Web, Caja, Core, instituciones con convenio).
     - La cola de mensajes (outbox) para que ningún mensaje se pierda si el Core
       o un servicio externo está caído (RIC-07).
     - La bitácora de cada intercambio con JCE, MSP, DGII, firma, pasarela y Core (RF-INT-01/03).
     - La caché de validaciones (30 días, RF-INT-01) y el estado de los cortacircuitos (RIC-06).
     - Los pagos recibidos de la Caja y las claves de idempotencia (RIC-05).
   Privacidad: las cédulas NUNCA se guardan en claro aquí; se guarda su huella HMAC-SHA256
   (64 caracteres hexadecimales), que permite buscar sin poder leer el número.
   ============================================================================= */
USE CONADIS_IntegracionDB;
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
IF OBJECT_ID(N'cfg.Parametro', N'U') IS NULL
CREATE TABLE cfg.Parametro
(
    Clave        VARCHAR(60)   NOT NULL CONSTRAINT PK_Parametro PRIMARY KEY,
    Valor        NVARCHAR(200) NOT NULL,
    Descripcion  NVARCHAR(300) NOT NULL,
    FechaCambio  DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Parametro_Fecha DEFAULT (SYSDATETIMEOFFSET()),
    UsuarioCambio NVARCHAR(100) NOT NULL CONSTRAINT DF_Parametro_Usuario DEFAULT (N'sistema')
);
GO

/* ----------------------------- SISTEMAS CLIENTE ---------------------------- */
-- Cada sistema que llama a Integración tiene su API key (se guarda solo su SHA-256)
-- y sus "ámbitos" (qué puede hacer), separados por espacios:
--   portal                  -> el portal Web
--   caja                    -> el módulo de Caja
--   firma                   -> el Core pidiendo firmas digitales
--   discapacidad.verificar  -> instituciones con convenio (RF-INT-03, NORTIC A4)
IF OBJECT_ID(N'seg.ClienteApi', N'U') IS NULL
CREATE TABLE seg.ClienteApi
(
    ClienteId      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ClienteApi PRIMARY KEY,
    Nombre         NVARCHAR(100) NOT NULL CONSTRAINT UQ_ClienteApi_Nombre UNIQUE,
    Tipo           NVARCHAR(20)  NOT NULL CONSTRAINT CK_ClienteApi_Tipo CHECK (Tipo IN (N'Canal', N'Modulo', N'Institucion')),
    HashClave      CHAR(64)      NOT NULL CONSTRAINT UQ_ClienteApi_Hash UNIQUE
                                 CONSTRAINT CK_ClienteApi_Hash CHECK (HashClave NOT LIKE '%[^0-9A-F]%'),
    Ambitos        NVARCHAR(300) NOT NULL,
    -- Solo instituciones: si el convenio autoriza ver tipo y grado, y hasta cuándo está vigente
    VerTipoGrado   BIT  NOT NULL CONSTRAINT DF_ClienteApi_VerTipoGrado DEFAULT (0),
    ConvenioHasta  DATE NULL,
    Activo         BIT  NOT NULL CONSTRAINT DF_ClienteApi_Activo DEFAULT (1),
    FechaCreacion  DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_ClienteApi_Fecha DEFAULT (SYSDATETIMEOFFSET()),
    CONSTRAINT CK_ClienteApi_Convenio CHECK (Tipo <> N'Institucion' OR ConvenioHasta IS NOT NULL)
);
GO

/* ----------------------------- OPERADORES DE LA CONSOLA -------------------- */
-- Personal técnico que supervisa la integración (consola de monitoreo, más adelante).
IF OBJECT_ID(N'seg.Operador', N'U') IS NULL
CREATE TABLE seg.Operador
(
    OperadorId        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Operador PRIMARY KEY,
    Username          NVARCHAR(50)  NOT NULL CONSTRAINT UQ_Operador_Username UNIQUE,
    PasswordHash      VARCHAR(200)  NOT NULL,
    NombreCompleto    NVARCHAR(150) NOT NULL,
    Rol               NVARCHAR(20)  NOT NULL CONSTRAINT CK_Operador_Rol CHECK (Rol IN (N'Administrador', N'Monitor')),
    Activo            BIT NOT NULL CONSTRAINT DF_Operador_Activo DEFAULT (1),
    IntentosFallidos  INT NOT NULL CONSTRAINT DF_Operador_Intentos DEFAULT (0),
    BloqueadoHasta    DATETIMEOFFSET(0) NULL,
    UltimoAcceso      DATETIMEOFFSET(0) NULL,
    FechaCreacion     DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Operador_Fecha DEFAULT (SYSDATETIMEOFFSET())
);
GO

/* ----------------------------- COLA DE MENSAJES (OUTBOX) ------------------- */
-- Todo lo que no puede hacerse en el momento (Core caído, JCE lenta...) se guarda aquí
-- y el servicio de Windows lo reintenta con espera creciente (30 s, 1 min, 2 min, 4 min...).
IF OBJECT_ID(N'integ.Mensaje', N'U') IS NULL
CREATE TABLE integ.Mensaje
(
    MensajeId       BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Mensaje PRIMARY KEY,
    Tipo            VARCHAR(60)    NOT NULL,     -- ej.: Core.RegistrarPago, Core.ValidacionJce
    Destino         VARCHAR(20)    NOT NULL CONSTRAINT CK_Mensaje_Destino
                    CHECK (Destino IN ('Core', 'JCE', 'MSP', 'DGII', 'Firma', 'Pasarela', 'Correo', 'SMS')),
    Carga           NVARCHAR(MAX)  NOT NULL CONSTRAINT CK_Mensaje_Carga CHECK (ISJSON(Carga) = 1),
    ClaveUnica      VARCHAR(100)   NULL,         -- evita encolar dos veces lo mismo
    Estado          VARCHAR(15)    NOT NULL CONSTRAINT DF_Mensaje_Estado DEFAULT ('Pendiente')
                    CONSTRAINT CK_Mensaje_Estado CHECK (Estado IN ('Pendiente', 'EnProceso', 'Completado', 'Fallido')),
    Intentos        INT NOT NULL CONSTRAINT DF_Mensaje_Intentos DEFAULT (0),
    MaxIntentos     INT NOT NULL CONSTRAINT CK_Mensaje_MaxIntentos CHECK (MaxIntentos BETWEEN 1 AND 50),
    ProximoIntento  DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Mensaje_Proximo DEFAULT (SYSDATETIMEOFFSET()),
    BloqueadoPor    NVARCHAR(100)  NULL,         -- trabajador que lo está procesando
    BloqueadoHasta  DATETIMEOFFSET(0) NULL,      -- si el trabajador muere, otro lo toma al vencer
    UltimoError     NVARCHAR(1000) NULL,
    CorrelacionId   VARCHAR(64)    NULL,
    FechaCreacion   DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_Mensaje_Fecha DEFAULT (SYSDATETIMEOFFSET()),
    FechaCompletado DATETIMEOFFSET(3) NULL
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Mensaje_ClaveUnica')
    CREATE UNIQUE INDEX UX_Mensaje_ClaveUnica ON integ.Mensaje (ClaveUnica) WHERE ClaveUnica IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Mensaje_Pendientes')
    CREATE INDEX IX_Mensaje_Pendientes ON integ.Mensaje (Estado, ProximoIntento) INCLUDE (Destino, BloqueadoHasta);
GO

/* ----------------------------- PAGOS RECIBIDOS ----------------------------- */
-- Cada cobro que llega de la Caja (o del portal) se guarda aquí JUNTO con su mensaje
-- hacia el Core, en la misma transacción (outbox transaccional). Si el Core está caído,
-- la Caja ya tiene su respuesta y el pago llegará al Core cuando vuelva.
IF OBJECT_ID(N'integ.PagoRecibido', N'U') IS NULL
CREATE TABLE integ.PagoRecibido
(
    TransaccionId      UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_PagoRecibido PRIMARY KEY,
    ClienteId          INT NOT NULL CONSTRAINT FK_PagoRecibido_Cliente REFERENCES seg.ClienteApi(ClienteId),
    Origen             VARCHAR(10)   NOT NULL CONSTRAINT CK_PagoRecibido_Origen CHECK (Origen IN ('CAJA', 'WEB')),
    NumeroOrden        VARCHAR(20)   NULL,
    NumeroComprobante  VARCHAR(20)   NOT NULL,
    Total              DECIMAL(18,2) NOT NULL CONSTRAINT CK_PagoRecibido_Total CHECK (Total >= 0),
    HashCarga          CHAR(64)      NOT NULL,   -- SHA-256 del pago: mismo Id con otro contenido = conflicto
    Estado             VARCHAR(15)   NOT NULL CONSTRAINT DF_PagoRecibido_Estado DEFAULT ('Recibido')
                       CONSTRAINT CK_PagoRecibido_Estado CHECK (Estado IN ('Recibido', 'Aplicado', 'Duplicado', 'Rechazado')),
    MensajeId          BIGINT NOT NULL CONSTRAINT FK_PagoRecibido_Mensaje REFERENCES integ.Mensaje(MensajeId),
    Detalle            NVARCHAR(500) NULL,       -- motivo si el Core lo rechazó
    FechaRecepcion     DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_PagoRecibido_Fecha DEFAULT (SYSDATETIMEOFFSET()),
    FechaConfirmacion  DATETIMEOFFSET(3) NULL
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PagoRecibido_Fecha')
    CREATE INDEX IX_PagoRecibido_Fecha ON integ.PagoRecibido (FechaRecepcion) INCLUDE (ClienteId, Estado);
GO

/* ----------------------------- IDEMPOTENCIA (RIC-05) ----------------------- */
-- Un POST con el encabezado Idempotency-Key repetido devuelve la MISMA respuesta guardada
-- en lugar de volver a ejecutarse (ej.: el ciudadano pulsa "Enviar" dos veces).
IF OBJECT_ID(N'integ.Idempotencia', N'U') IS NULL
CREATE TABLE integ.Idempotencia
(
    ClienteId      INT NOT NULL CONSTRAINT FK_Idempotencia_Cliente REFERENCES seg.ClienteApi(ClienteId),
    Clave          VARCHAR(100)  NOT NULL,
    Ruta           NVARCHAR(200) NOT NULL,
    HashPeticion   CHAR(64)      NOT NULL,
    Estado         VARCHAR(15)   NOT NULL CONSTRAINT CK_Idempotencia_Estado CHECK (Estado IN ('EnProceso', 'Completada')),
    CodigoHttp     INT           NULL,
    Respuesta      NVARCHAR(MAX) NULL,
    FechaCreacion  DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Idempotencia_Fecha DEFAULT (SYSDATETIMEOFFSET()),
    VenceEn        DATETIMEOFFSET(0) NOT NULL,
    CONSTRAINT PK_Idempotencia PRIMARY KEY (ClienteId, Clave)
);
GO

/* ----------------------------- BITÁCORA DE INTEROPERABILIDAD --------------- */
-- Una fila por cada intercambio (RF-INT-01 paso 5, RF-INT-03 paso 3): fecha, quién, propósito,
-- resultado y latencia. Solo inserción: nadie puede modificarla ni borrarla.
IF OBJECT_ID(N'integ.Bitacora', N'U') IS NULL
CREATE TABLE integ.Bitacora
(
    BitacoraId      BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Bitacora PRIMARY KEY,
    Fecha           DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_Bitacora_Fecha DEFAULT (SYSDATETIMEOFFSET()),
    Servicio        VARCHAR(20)   NOT NULL CONSTRAINT CK_Bitacora_Servicio
                    CHECK (Servicio IN ('JCE', 'MSP', 'DGII', 'Firma', 'Pasarela', 'Core', 'Verificacion', 'Correo', 'SMS')),
    Operacion       NVARCHAR(60)  NOT NULL,
    Direccion       VARCHAR(10)   NOT NULL CONSTRAINT CK_Bitacora_Direccion CHECK (Direccion IN ('Saliente', 'Entrante')),
    Cliente         NVARCHAR(100) NULL,      -- sistema que originó la consulta (Web, Caja, institución...)
    Usuario         NVARCHAR(100) NULL,      -- persona detrás, si se conoce
    Proposito       NVARCHAR(200) NULL,
    ReferenciaHash  CHAR(64)      NULL,      -- huella HMAC de la cédula o documento consultado
    Exito           BIT           NOT NULL,
    Resultado       VARCHAR(30)   NOT NULL,  -- Valida, Invalida, Pendiente, Ok, Error, TiempoAgotado, CircuitoAbierto, Denegado...
    CodigoRespuesta VARCHAR(20)   NULL,
    LatenciaMs      INT           NULL CONSTRAINT CK_Bitacora_Latencia CHECK (LatenciaMs >= 0),
    DesdeCache      BIT           NOT NULL CONSTRAINT DF_Bitacora_Cache DEFAULT (0),
    CorrelacionId   VARCHAR(64)   NULL,
    Detalle         NVARCHAR(500) NULL       -- nunca datos clínicos ni biométricos
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Bitacora_Fecha')
    CREATE INDEX IX_Bitacora_Fecha ON integ.Bitacora (Fecha) INCLUDE (Servicio, Exito, LatenciaMs);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Bitacora_Referencia')
    CREATE INDEX IX_Bitacora_Referencia ON integ.Bitacora (ReferenciaHash) WHERE ReferenciaHash IS NOT NULL;
GO

CREATE OR ALTER TRIGGER integ.TR_Bitacora_SoloInsercion
ON integ.Bitacora
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 56900, N'La bitácora de interoperabilidad no permite modificar ni borrar registros.', 1;
END;
GO

/* ----------------------------- CACHÉ DE VALIDACIONES ----------------------- */
-- Resultados definitivos de la JCE y el MSP (RF-INT-01 paso 1: vigencia de 30 días).
-- La clave es la huella HMAC de la consulta, no la cédula.
IF OBJECT_ID(N'integ.CacheValidacion', N'U') IS NULL
CREATE TABLE integ.CacheValidacion
(
    Servicio       VARCHAR(10)   NOT NULL CONSTRAINT CK_Cache_Servicio CHECK (Servicio IN ('JCE', 'MSP')),
    Clave          CHAR(64)      NOT NULL,
    Resultado      VARCHAR(30)   NOT NULL,
    Datos          NVARCHAR(MAX) NULL CONSTRAINT CK_Cache_Datos CHECK (Datos IS NULL OR ISJSON(Datos) = 1),
    FechaConsulta  DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Cache_Fecha DEFAULT (SYSDATETIMEOFFSET()),
    VenceEn        DATETIMEOFFSET(0) NOT NULL,
    CONSTRAINT PK_CacheValidacion PRIMARY KEY (Servicio, Clave)
);
GO

/* ----------------------------- CORTACIRCUITOS (RIC-06) --------------------- */
-- Tras 5 fallos seguidos el circuito se ABRE 60 s: no se llama al servicio caído (respuesta
-- inmediata "Pendiente" + reintento por la cola). Al vencer, pasa a SEMIABIERTO y deja
-- pasar UNA llamada de prueba: si funciona se CIERRA, si falla vuelve a abrirse.
IF OBJECT_ID(N'integ.Circuito', N'U') IS NULL
CREATE TABLE integ.Circuito
(
    Servicio            VARCHAR(20)  NOT NULL CONSTRAINT PK_Circuito PRIMARY KEY,
    Estado              VARCHAR(12)  NOT NULL CONSTRAINT DF_Circuito_Estado DEFAULT ('Cerrado')
                        CONSTRAINT CK_Circuito_Estado CHECK (Estado IN ('Cerrado', 'Abierto', 'SemiAbierto')),
    FallosConsecutivos  INT NOT NULL CONSTRAINT DF_Circuito_Fallos DEFAULT (0),
    AbiertoHasta        DATETIMEOFFSET(0) NULL,
    UltimoCambio        DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Circuito_Cambio DEFAULT (SYSDATETIMEOFFSET()),
    UltimoError         NVARCHAR(500) NULL
);
GO

/* ----------------------------- ALERTAS AL ADMINISTRADOR -------------------- */
-- Circuito abierto, mensaje que agotó sus reintentos, certificado de firma por vencer...
IF OBJECT_ID(N'integ.Alerta', N'U') IS NULL
CREATE TABLE integ.Alerta
(
    AlertaId       BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Alerta PRIMARY KEY,
    Fecha          DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Alerta_Fecha DEFAULT (SYSDATETIMEOFFSET()),
    Severidad      VARCHAR(10)   NOT NULL CONSTRAINT CK_Alerta_Severidad CHECK (Severidad IN ('Alta', 'Media', 'Baja')),
    Servicio       VARCHAR(20)   NOT NULL,
    Mensaje        NVARCHAR(500) NOT NULL,
    Atendida       BIT NOT NULL CONSTRAINT DF_Alerta_Atendida DEFAULT (0),
    AtendidaPor    NVARCHAR(100) NULL,
    FechaAtencion  DATETIMEOFFSET(0) NULL,
    Comentario     NVARCHAR(500) NULL
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Alerta_Pendientes')
    CREATE INDEX IX_Alerta_Pendientes ON integ.Alerta (Atendida, Fecha);
GO

PRINT N'02 - Tablas de Integración creadas.';
GO
