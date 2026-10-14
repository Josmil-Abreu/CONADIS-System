/* =============================================================================
   CONADIS.Core - 02: Tablas
   Reglas del SRS 3.8 aplicadas:
     - Claves foráneas declaradas, sin borrado en cascada (ON DELETE NO ACTION).
     - CHECK para dominios cerrados (estados, calificadores CIF, montos >= 0).
     - ROWVERSION en las raíces de agregado (concurrencia optimista).
     - Fechas en DATETIMEOFFSET, montos en DECIMAL(18,2), textos en NVARCHAR.
     - Tablas temporales (historial automático) en Persona, Expediente y Tasa.
   ============================================================================= */
USE CONADIS_CoreDB;
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

/* ----------------------------- SEGURIDAD ----------------------------------- */
IF OBJECT_ID(N'seg.Rol', N'U') IS NULL
CREATE TABLE seg.Rol
(
    RolId       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Rol PRIMARY KEY,
    Nombre      NVARCHAR(50)  NOT NULL CONSTRAINT UQ_Rol_Nombre UNIQUE,
    Descripcion NVARCHAR(200) NOT NULL
);
GO

IF OBJECT_ID(N'cat.CentroValoracion', N'U') IS NULL
CREATE TABLE cat.CentroValoracion
(
    CentroId         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CentroValoracion PRIMARY KEY,
    Nombre           NVARCHAR(150) NOT NULL CONSTRAINT UQ_Centro_Nombre UNIQUE,
    Provincia        NVARCHAR(60)  NOT NULL,
    CapacidadDiaria  INT NOT NULL CONSTRAINT CK_Centro_Capacidad CHECK (CapacidadDiaria BETWEEN 1 AND 500),
    Activo           BIT NOT NULL CONSTRAINT DF_Centro_Activo DEFAULT (1)
);
GO

IF OBJECT_ID(N'seg.Usuario', N'U') IS NULL
CREATE TABLE seg.Usuario
(
    UsuarioId         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Usuario PRIMARY KEY,
    Username          NVARCHAR(50)  NOT NULL CONSTRAINT UQ_Usuario_Username UNIQUE,
    PasswordHash      VARCHAR(200)  NOT NULL,
    NombreCompleto    NVARCHAR(150) NOT NULL,
    Correo            NVARCHAR(150) NOT NULL,
    RolId             INT NOT NULL CONSTRAINT FK_Usuario_Rol REFERENCES seg.Rol(RolId),
    -- Solo para evaluadores: disciplina y centro (RNF-SEG-05: solo ven su centro)
    Disciplina        NVARCHAR(20) NULL CONSTRAINT CK_Usuario_Disciplina CHECK (Disciplina IN (N'Medica', N'Psicologica', N'Social')),
    CentroId          INT NULL CONSTRAINT FK_Usuario_Centro REFERENCES cat.CentroValoracion(CentroId),
    Activo            BIT NOT NULL CONSTRAINT DF_Usuario_Activo DEFAULT (1),
    IntentosFallidos  INT NOT NULL CONSTRAINT DF_Usuario_Intentos DEFAULT (0),
    BloqueadoHasta    DATETIMEOFFSET(0) NULL,
    UltimoAcceso      DATETIMEOFFSET(0) NULL,
    FechaCreacion     DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Usuario_Fecha DEFAULT (SYSDATETIMEOFFSET()),
    RowVersion        ROWVERSION NOT NULL
);
GO

/* ----------------------------- CATÁLOGOS ----------------------------------- */
IF OBJECT_ID(N'cat.CodigoCIF', N'U') IS NULL
CREATE TABLE cat.CodigoCIF
(
    Codigo      VARCHAR(10)   NOT NULL CONSTRAINT PK_CodigoCIF PRIMARY KEY,
    Componente  CHAR(1)       NOT NULL CONSTRAINT CK_CodigoCIF_Componente CHECK (Componente IN ('b', 's', 'd', 'e')),
    Descripcion NVARCHAR(300) NOT NULL,
    Activo      BIT NOT NULL CONSTRAINT DF_CodigoCIF_Activo DEFAULT (1)
);
GO

IF OBJECT_ID(N'cat.Baremo', N'U') IS NULL
CREATE TABLE cat.Baremo
(
    BaremoId     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Baremo PRIMARY KEY,
    Version      VARCHAR(20) NOT NULL CONSTRAINT UQ_Baremo_Version UNIQUE,
    Vigente      BIT NOT NULL,
    FechaDesde   DATE NOT NULL
);
GO

IF OBJECT_ID(N'cat.BaremoRango', N'U') IS NULL
CREATE TABLE cat.BaremoRango
(
    BaremoId      INT NOT NULL CONSTRAINT FK_BaremoRango_Baremo REFERENCES cat.Baremo(BaremoId),
    PuntajeDesde  DECIMAL(5,2) NOT NULL,
    PuntajeHasta  DECIMAL(5,2) NOT NULL,
    Grado         NVARCHAR(20) NOT NULL CONSTRAINT CK_BaremoRango_Grado CHECK (Grado IN (N'Leve', N'Moderado', N'Severo', N'Profundo')),
    CONSTRAINT PK_BaremoRango PRIMARY KEY (BaremoId, PuntajeDesde),
    CONSTRAINT CK_BaremoRango_Rango CHECK (PuntajeDesde >= 0 AND PuntajeHasta <= 100 AND PuntajeDesde <= PuntajeHasta)
);
GO

-- Parámetros de negocio editables sin recompilar (RNF-MAN-05)
IF OBJECT_ID(N'cat.Parametro', N'U') IS NULL
CREATE TABLE cat.Parametro
(
    Clave       VARCHAR(50)   NOT NULL CONSTRAINT PK_Parametro PRIMARY KEY,
    Valor       NVARCHAR(200) NOT NULL,
    Descripcion NVARCHAR(300) NOT NULL
);
GO

-- Catálogo oficial de tasas (RF-CAJ-01). Tabla temporal: guarda cada cambio de precio.
IF OBJECT_ID(N'cat.Tasa', N'U') IS NULL
CREATE TABLE cat.Tasa
(
    TasaId           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Tasa PRIMARY KEY,
    Codigo           VARCHAR(20)   NOT NULL CONSTRAINT UQ_Tasa_Codigo UNIQUE,
    Descripcion      NVARCHAR(200) NOT NULL,
    Monto            DECIMAL(18,2) NOT NULL CONSTRAINT CK_Tasa_Monto CHECK (Monto >= 0),
    ItbisPorcentaje  DECIMAL(5,2)  NOT NULL CONSTRAINT DF_Tasa_Itbis DEFAULT (0)
                                   CONSTRAINT CK_Tasa_Itbis CHECK (ItbisPorcentaje BETWEEN 0 AND 100),
    Resolucion       NVARCHAR(100) NOT NULL,
    Activa           BIT NOT NULL CONSTRAINT DF_Tasa_Activa DEFAULT (1),
    ValidoDesde      DATETIME2(0) GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
    ValidoHasta      DATETIME2(0) GENERATED ALWAYS AS ROW END HIDDEN NOT NULL,
    PERIOD FOR SYSTEM_TIME (ValidoDesde, ValidoHasta)
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = cat.TasaHistorial));
GO

/* ----------------------------- PERSONAS Y EXPEDIENTES ---------------------- */
IF OBJECT_ID(N'core.Persona', N'U') IS NULL
CREATE TABLE core.Persona
(
    PersonaId            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Persona PRIMARY KEY,
    Cedula               CHAR(11)      NULL,     -- nula solo en menores sin cédula
    ActaNacimiento       NVARCHAR(100) NULL,     -- libro/folio/acta/año/oficialía
    Nombres              NVARCHAR(100) NOT NULL,
    Apellidos            NVARCHAR(100) NOT NULL,
    FechaNacimiento      DATE NULL,
    Sexo                 TINYINT NULL CONSTRAINT CK_Persona_Sexo CHECK (Sexo IN (1, 2)),
    Telefono             NVARCHAR(20)  NULL,
    Correo               NVARCHAR(150) NULL,
    CanalNotificacion    NVARCHAR(10)  NOT NULL CONSTRAINT DF_Persona_Canal DEFAULT (N'Correo')
                                       CONSTRAINT CK_Persona_Canal CHECK (CanalNotificacion IN (N'Correo', N'SMS', N'Portal')),
    Provincia            NVARCHAR(60)  NULL,
    Municipio            NVARCHAR(60)  NULL,
    Sector               NVARCHAR(100) NULL,
    Direccion            NVARCHAR(250) NULL,
    EstadoIdentidad      NVARCHAR(20)  NOT NULL CONSTRAINT DF_Persona_EstadoId DEFAULT (N'Pendiente')
                                       CONSTRAINT CK_Persona_EstadoId CHECK (EstadoIdentidad IN (N'Validada', N'Pendiente', N'Invalida', N'Manual')),
    FechaValidacionJce   DATETIMEOFFSET(0) NULL,
    FechaCreacion        DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Persona_Fecha DEFAULT (SYSDATETIMEOFFSET()),
    RowVersion           ROWVERSION NOT NULL,
    ValidoDesde          DATETIME2(0) GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
    ValidoHasta          DATETIME2(0) GENERATED ALWAYS AS ROW END HIDDEN NOT NULL,
    PERIOD FOR SYSTEM_TIME (ValidoDesde, ValidoHasta),
    CONSTRAINT CK_Persona_Identificador CHECK (Cedula IS NOT NULL OR ActaNacimiento IS NOT NULL)
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = core.PersonaHistorial));
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Persona_Cedula')
    CREATE UNIQUE INDEX UX_Persona_Cedula ON core.Persona(Cedula) WHERE Cedula IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Persona_Acta')
    CREATE UNIQUE INDEX UX_Persona_Acta ON core.Persona(ActaNacimiento) WHERE ActaNacimiento IS NOT NULL;
GO

-- Tutor o representante legal de una persona (RF-CORE-01)
IF OBJECT_ID(N'core.Tutela', N'U') IS NULL
CREATE TABLE core.Tutela
(
    TutelaId       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Tutela PRIMARY KEY,
    PersonaId      INT NOT NULL CONSTRAINT FK_Tutela_Persona REFERENCES core.Persona(PersonaId),
    CedulaTutor    CHAR(11) NOT NULL,
    Vinculo        NVARCHAR(50) NOT NULL,
    FechaRegistro  DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Tutela_Fecha DEFAULT (SYSDATETIMEOFFSET()),
    CONSTRAINT UQ_Tutela UNIQUE (PersonaId, CedulaTutor)
);
GO

IF OBJECT_ID(N'core.Expediente', N'U') IS NULL
CREATE TABLE core.Expediente
(
    ExpedienteId        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Expediente PRIMARY KEY,
    NumeroExpediente    VARCHAR(20) NOT NULL CONSTRAINT UQ_Expediente_Numero UNIQUE,
    PersonaId           INT NOT NULL CONSTRAINT UQ_Expediente_Persona UNIQUE
                            CONSTRAINT FK_Expediente_Persona REFERENCES core.Persona(PersonaId),
    Estado              NVARCHAR(20) NOT NULL CONSTRAINT CK_Expediente_Estado CHECK (Estado IN
                            (N'Solicitado', N'EnEvaluacion', N'Aprobado', N'Rechazado', N'EnApelacion', N'Carnetizado', N'Vencido', N'Anulado')),
    TipoDiscapacidad    NVARCHAR(20) NULL CONSTRAINT CK_Expediente_Tipo CHECK (TipoDiscapacidad IN
                            (N'Fisica', N'Visual', N'Auditiva', N'Intelectual', N'Psicosocial', N'Multiple')),
    GradoDiscapacidad   NVARCHAR(20) NULL CONSTRAINT CK_Expediente_Grado CHECK (GradoDiscapacidad IN
                            (N'Leve', N'Moderado', N'Severo', N'Profundo')),
    FechaVencimiento    DATE NULL,      -- nula = permanente
    CentroId            INT NULL CONSTRAINT FK_Expediente_Centro REFERENCES cat.CentroValoracion(CentroId),
    FechaRechazo        DATETIMEOFFSET(0) NULL,
    FechaCreacion       DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Expediente_Creacion DEFAULT (SYSDATETIMEOFFSET()),
    FechaActualizacion  DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Expediente_Actualizacion DEFAULT (SYSDATETIMEOFFSET()),
    RowVersion          ROWVERSION NOT NULL,
    ValidoDesde         DATETIME2(0) GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
    ValidoHasta         DATETIME2(0) GENERATED ALWAYS AS ROW END HIDDEN NOT NULL,
    PERIOD FOR SYSTEM_TIME (ValidoDesde, ValidoHasta)
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = core.ExpedienteHistorial));
GO

-- Cada trámite de certificación, renovación o duplicado (número TRA-AAAA-NNNNNNN)
IF OBJECT_ID(N'core.Solicitud', N'U') IS NULL
CREATE TABLE core.Solicitud
(
    SolicitudId              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Solicitud PRIMARY KEY,
    NumeroTramite            VARCHAR(20) NOT NULL CONSTRAINT UQ_Solicitud_Numero UNIQUE,
    ExpedienteId             INT NOT NULL CONSTRAINT FK_Solicitud_Expediente REFERENCES core.Expediente(ExpedienteId),
    Tipo                     NVARCHAR(20) NOT NULL CONSTRAINT CK_Solicitud_Tipo CHECK (Tipo IN (N'Certificacion', N'Renovacion', N'Duplicado')),
    Canal                    NVARCHAR(20) NOT NULL CONSTRAINT CK_Solicitud_Canal CHECK (Canal IN (N'Web', N'Presencial')),
    CedulaCuenta             CHAR(11) NULL,          -- quien la presentó (titular o tutor)
    Establecimiento          NVARCHAR(200) NULL,
    NumeroCertificadoMedico  NVARCHAR(50)  NULL,
    FechaCertificadoMedico   DATE NULL,
    CodigoCie10              VARCHAR(10)   NULL,
    EstadoDiagnostico        NVARCHAR(30)  NOT NULL CONSTRAINT DF_Solicitud_EstDiag DEFAULT (N'ValidacionManual')
                                           CONSTRAINT CK_Solicitud_EstDiag CHECK (EstadoDiagnostico IN (N'Validado', N'NoCoincide', N'NoEncontrado', N'ValidacionManual')),
    CentroPreferidoId        INT NULL CONSTRAINT FK_Solicitud_Centro REFERENCES cat.CentroValoracion(CentroId),
    FechaCitaPreferida       DATETIMEOFFSET(0) NULL,
    Apoyos                   NVARCHAR(300) NULL,
    VisitaDomiciliaria       BIT NOT NULL CONSTRAINT DF_Solicitud_Visita DEFAULT (0),
    MotivoDuplicado          NVARCHAR(50)  NULL,
    ConsentimientoDatos      BIT NOT NULL CONSTRAINT CK_Solicitud_Consentimiento CHECK (ConsentimientoDatos = 1),
    Estado                   NVARCHAR(20) NOT NULL CONSTRAINT DF_Solicitud_Estado DEFAULT (N'Activa')
                                          CONSTRAINT CK_Solicitud_Estado CHECK (Estado IN (N'Activa', N'Cerrada', N'Anulada')),
    UsuarioRegistro          NVARCHAR(100) NOT NULL,
    FechaRegistro            DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Solicitud_Fecha DEFAULT (SYSDATETIMEOFFSET())
);
GO

IF OBJECT_ID(N'core.Cita', N'U') IS NULL
CREATE TABLE core.Cita
(
    CitaId          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Cita PRIMARY KEY,
    ExpedienteId    INT NOT NULL CONSTRAINT FK_Cita_Expediente REFERENCES core.Expediente(ExpedienteId),
    CentroId        INT NOT NULL CONSTRAINT FK_Cita_Centro REFERENCES cat.CentroValoracion(CentroId),
    FechaHora       DATETIMEOFFSET(0) NOT NULL,
    Domiciliaria    BIT NOT NULL CONSTRAINT DF_Cita_Domiciliaria DEFAULT (0),
    Estado          NVARCHAR(20) NOT NULL CONSTRAINT CK_Cita_Estado CHECK (Estado IN (N'Programada', N'Realizada', N'Cancelada', N'Reprogramada')),
    UsuarioRegistro NVARCHAR(100) NOT NULL,
    FechaRegistro   DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Cita_Fecha DEFAULT (SYSDATETIMEOFFSET())
);
GO

/* ----------------------------- VALORACIÓN CIF ------------------------------ */
IF OBJECT_ID(N'core.EvaluacionCIF', N'U') IS NULL
CREATE TABLE core.EvaluacionCIF
(
    EvaluacionId     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EvaluacionCIF PRIMARY KEY,
    ExpedienteId     INT NOT NULL CONSTRAINT FK_Evaluacion_Expediente REFERENCES core.Expediente(ExpedienteId),
    CitaId           INT NULL CONSTRAINT FK_Evaluacion_Cita REFERENCES core.Cita(CitaId),
    FechaEvaluacion  DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Evaluacion_Fecha DEFAULT (SYSDATETIMEOFFSET()),
    -- WHODAS 2.0: 6 dominios (0-100) y puntaje global
    WhodasCognicion       DECIMAL(5,2) NULL,
    WhodasMovilidad       DECIMAL(5,2) NULL,
    WhodasAutocuidado     DECIMAL(5,2) NULL,
    WhodasRelaciones      DECIMAL(5,2) NULL,
    WhodasActividades     DECIMAL(5,2) NULL,
    WhodasParticipacion   DECIMAL(5,2) NULL,
    PuntajeWhodas         DECIMAL(5,2) NULL,
    GradoPropuesto   NVARCHAR(20) NULL CONSTRAINT CK_Evaluacion_Grado CHECK (GradoPropuesto IN (N'Leve', N'Moderado', N'Severo', N'Profundo')),
    TipoPropuesto    NVARCHAR(20) NULL CONSTRAINT CK_Evaluacion_Tipo CHECK (TipoPropuesto IN
                         (N'Fisica', N'Visual', N'Auditiva', N'Intelectual', N'Psicosocial', N'Multiple')),
    VersionBaremo    VARCHAR(20) NULL,
    Estado           NVARCHAR(20) NOT NULL CONSTRAINT DF_Evaluacion_Estado DEFAULT (N'Borrador')
                         CONSTRAINT CK_Evaluacion_Estado CHECK (Estado IN (N'Borrador', N'Completa', N'Consolidada')),
    Observaciones    NVARCHAR(1000) NULL,
    RowVersion       ROWVERSION NOT NULL,
    CONSTRAINT CK_Evaluacion_Whodas CHECK (
        (WhodasCognicion     IS NULL OR WhodasCognicion     BETWEEN 0 AND 100) AND
        (WhodasMovilidad     IS NULL OR WhodasMovilidad     BETWEEN 0 AND 100) AND
        (WhodasAutocuidado   IS NULL OR WhodasAutocuidado   BETWEEN 0 AND 100) AND
        (WhodasRelaciones    IS NULL OR WhodasRelaciones    BETWEEN 0 AND 100) AND
        (WhodasActividades   IS NULL OR WhodasActividades   BETWEEN 0 AND 100) AND
        (WhodasParticipacion IS NULL OR WhodasParticipacion BETWEEN 0 AND 100))
);
GO

-- Firma de cada disciplina del equipo multidisciplinario
IF OBJECT_ID(N'core.ParticipacionEvaluador', N'U') IS NULL
CREATE TABLE core.ParticipacionEvaluador
(
    EvaluacionId  INT NOT NULL CONSTRAINT FK_Participacion_Evaluacion REFERENCES core.EvaluacionCIF(EvaluacionId),
    Disciplina    NVARCHAR(20) NOT NULL CONSTRAINT CK_Participacion_Disciplina CHECK (Disciplina IN (N'Medica', N'Psicologica', N'Social')),
    UsuarioId     INT NOT NULL CONSTRAINT FK_Participacion_Usuario REFERENCES seg.Usuario(UsuarioId),
    FechaFirma    DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Participacion_Fecha DEFAULT (SYSDATETIMEOFFSET()),
    CONSTRAINT PK_ParticipacionEvaluador PRIMARY KEY (EvaluacionId, Disciplina)
);
GO

IF OBJECT_ID(N'core.CodificacionCIF', N'U') IS NULL
CREATE TABLE core.CodificacionCIF
(
    EvaluacionId   INT NOT NULL CONSTRAINT FK_Codificacion_Evaluacion REFERENCES core.EvaluacionCIF(EvaluacionId),
    Codigo         VARCHAR(10) NOT NULL CONSTRAINT FK_Codificacion_Codigo REFERENCES cat.CodigoCIF(Codigo),
    Disciplina     NVARCHAR(20) NOT NULL CONSTRAINT CK_Codificacion_Disciplina CHECK (Disciplina IN (N'Medica', N'Psicologica', N'Social')),
    Calificador1   TINYINT NOT NULL CONSTRAINT CK_Codificacion_C1 CHECK (Calificador1 IN (0, 1, 2, 3, 4, 8, 9)),
    Calificador2   TINYINT NULL     CONSTRAINT CK_Codificacion_C2 CHECK (Calificador2 IN (0, 1, 2, 3, 4, 8, 9)),
    EsFacilitador  BIT NOT NULL CONSTRAINT DF_Codificacion_Facilitador DEFAULT (0),  -- solo factores ambientales (+)
    Observacion    NVARCHAR(300) NULL,
    UsuarioId      INT NOT NULL CONSTRAINT FK_Codificacion_Usuario REFERENCES seg.Usuario(UsuarioId),
    CONSTRAINT PK_CodificacionCIF PRIMARY KEY (EvaluacionId, Codigo, Disciplina)
);
GO

/* ----------------------------- DICTAMEN Y APELACIÓN ------------------------ */
IF OBJECT_ID(N'core.VotoDictamen', N'U') IS NULL
CREATE TABLE core.VotoDictamen
(
    EvaluacionId  INT NOT NULL CONSTRAINT FK_Voto_Evaluacion REFERENCES core.EvaluacionCIF(EvaluacionId),
    UsuarioId     INT NOT NULL CONSTRAINT FK_Voto_Usuario REFERENCES seg.Usuario(UsuarioId),
    Voto          NVARCHAR(10) NOT NULL CONSTRAINT CK_Voto_Voto CHECK (Voto IN (N'Aprobar', N'Rechazar')),
    Grado         NVARCHAR(20) NULL,
    Tipo          NVARCHAR(20) NULL,
    Motivo        NVARCHAR(500) NULL,
    FechaVoto     DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Voto_Fecha DEFAULT (SYSDATETIMEOFFSET()),
    CONSTRAINT PK_VotoDictamen PRIMARY KEY (EvaluacionId, UsuarioId)
);
GO

IF OBJECT_ID(N'core.Dictamen', N'U') IS NULL
CREATE TABLE core.Dictamen
(
    DictamenId              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Dictamen PRIMARY KEY,
    EvaluacionId            INT NOT NULL CONSTRAINT UQ_Dictamen_Evaluacion UNIQUE
                                CONSTRAINT FK_Dictamen_Evaluacion REFERENCES core.EvaluacionCIF(EvaluacionId),
    ExpedienteId            INT NOT NULL CONSTRAINT FK_Dictamen_Expediente REFERENCES core.Expediente(ExpedienteId),
    Resultado               NVARCHAR(10) NOT NULL CONSTRAINT CK_Dictamen_Resultado CHECK (Resultado IN (N'Aprobado', N'Rechazado')),
    Grado                   NVARCHAR(20) NULL,
    Tipo                    NVARCHAR(20) NULL,
    Motivo                  NVARCHAR(500) NULL,
    FechaVencimientoCarnet  DATE NULL,
    Fecha                   DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Dictamen_Fecha DEFAULT (SYSDATETIMEOFFSET())
);
GO

IF OBJECT_ID(N'core.Apelacion', N'U') IS NULL
CREATE TABLE core.Apelacion
(
    ApelacionId        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Apelacion PRIMARY KEY,
    ExpedienteId       INT NOT NULL CONSTRAINT FK_Apelacion_Expediente REFERENCES core.Expediente(ExpedienteId),
    Motivo             NVARCHAR(1000) NOT NULL,
    Estado             NVARCHAR(20) NOT NULL CONSTRAINT DF_Apelacion_Estado DEFAULT (N'Pendiente')
                           CONSTRAINT CK_Apelacion_Estado CHECK (Estado IN (N'Pendiente', N'Admitida', N'Denegada')),
    Resolucion         NVARCHAR(1000) NULL,
    UsuarioResuelveId  INT NULL CONSTRAINT FK_Apelacion_Usuario REFERENCES seg.Usuario(UsuarioId),
    FechaRegistro      DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Apelacion_Fecha DEFAULT (SYSDATETIMEOFFSET()),
    FechaResolucion    DATETIMEOFFSET(0) NULL
);
GO

/* ----------------------------- CARNETS ------------------------------------- */
IF OBJECT_ID(N'core.Carnet', N'U') IS NULL
CREATE TABLE core.Carnet
(
    CarnetId            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Carnet PRIMARY KEY,
    ExpedienteId        INT NOT NULL CONSTRAINT FK_Carnet_Expediente REFERENCES core.Expediente(ExpedienteId),
    NumeroCarnet        VARCHAR(20) NOT NULL CONSTRAINT UQ_Carnet_Numero UNIQUE,
    NumeroCertificado   VARCHAR(20) NOT NULL CONSTRAINT UQ_Carnet_Certificado UNIQUE,
    CodigoVerificacion  VARCHAR(64) NOT NULL CONSTRAINT UQ_Carnet_Codigo UNIQUE,   -- identificador opaco del QR
    Firma               VARCHAR(600) NOT NULL,                                     -- firma devuelta por Integración
    FechaEmision        DATE NOT NULL,
    FechaVencimiento    DATE NULL,
    Estado              NVARCHAR(10) NOT NULL CONSTRAINT CK_Carnet_Estado CHECK (Estado IN (N'Vigente', N'Vencido', N'Anulado')),
    MotivoAnulacion     NVARCHAR(300) NULL,
    UsuarioEmite        NVARCHAR(100) NOT NULL,
    UsuarioAnula        NVARCHAR(100) NULL,
    FechaAnulacion      DATETIMEOFFSET(0) NULL
);
GO

-- Solo un carnet vigente por expediente (SRS 3.8)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Carnet_UnoVigente')
    CREATE UNIQUE INDEX UX_Carnet_UnoVigente ON core.Carnet(ExpedienteId) WHERE Estado = N'Vigente';
GO

/* ----------------------------- ÓRDENES Y PAGOS ----------------------------- */
IF OBJECT_ID(N'core.OrdenCobro', N'U') IS NULL
CREATE TABLE core.OrdenCobro
(
    OrdenId            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrdenCobro PRIMARY KEY,
    NumeroOrden        VARCHAR(20) NOT NULL CONSTRAINT UQ_Orden_Numero UNIQUE,
    TasaId             INT NOT NULL CONSTRAINT FK_Orden_Tasa REFERENCES cat.Tasa(TasaId),
    Cantidad           INT NOT NULL CONSTRAINT CK_Orden_Cantidad CHECK (Cantidad BETWEEN 1 AND 100),
    MontoUnitario      DECIMAL(18,2) NOT NULL CONSTRAINT CK_Orden_Unitario CHECK (MontoUnitario >= 0),
    Total              DECIMAL(18,2) NOT NULL CONSTRAINT CK_Orden_Total CHECK (Total >= 0),
    Documento          VARCHAR(11)   NOT NULL,     -- cédula (11) o RNC (9)
    NombrePagador      NVARCHAR(150) NOT NULL,
    Referencia         VARCHAR(20)   NULL,         -- número de trámite relacionado
    Estado             NVARCHAR(10)  NOT NULL CONSTRAINT DF_Orden_Estado DEFAULT (N'Pendiente')
                                     CONSTRAINT CK_Orden_Estado CHECK (Estado IN (N'Pendiente', N'Pagada', N'Anulada')),
    FechaCreacion      DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Orden_Fecha DEFAULT (SYSDATETIMEOFFSET()),
    FechaPago          DATETIMEOFFSET(0) NULL,
    NumeroComprobante  VARCHAR(20) NULL
);
GO

-- Pagos recibidos desde la Caja o el portal (vía Integración). TransaccionId = idempotencia (RIC-05).
IF OBJECT_ID(N'core.Pago', N'U') IS NULL
CREATE TABLE core.Pago
(
    PagoId             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Pago PRIMARY KEY,
    TransaccionId      UNIQUEIDENTIFIER NOT NULL CONSTRAINT UQ_Pago_Transaccion UNIQUE,
    Origen             VARCHAR(10)   NOT NULL CONSTRAINT CK_Pago_Origen CHECK (Origen IN ('CAJA', 'WEB')),
    OrdenId            INT NULL CONSTRAINT FK_Pago_Orden REFERENCES core.OrdenCobro(OrdenId),
    TasaCodigo         VARCHAR(20)   NOT NULL,
    Cantidad           INT NOT NULL,
    Total              DECIMAL(18,2) NOT NULL CONSTRAINT CK_Pago_Total CHECK (Total >= 0),
    MedioPago          NVARCHAR(20)  NOT NULL CONSTRAINT CK_Pago_Medio CHECK (MedioPago IN (N'Efectivo', N'Tarjeta', N'Transferencia', N'EnLinea')),
    Documento          VARCHAR(11)   NOT NULL,
    NombrePagador      NVARCHAR(150) NOT NULL,
    NumeroComprobante  VARCHAR(20)   NOT NULL,
    Caja               NVARCHAR(50)  NULL,
    Cajero             NVARCHAR(50)  NULL,
    FechaPago          DATETIMEOFFSET(0) NOT NULL,
    FechaRecepcion     DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Pago_Recepcion DEFAULT (SYSDATETIMEOFFSET())
);
GO

/* ----------------------------- SERVICIOS (RF-WEB-03) ----------------------- */
IF OBJECT_ID(N'core.SolicitudServicio', N'U') IS NULL
CREATE TABLE core.SolicitudServicio
(
    ServicioId          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SolicitudServicio PRIMARY KEY,
    NumeroTramite       VARCHAR(20) NOT NULL CONSTRAINT UQ_Servicio_Numero UNIQUE,
    Tipo                VARCHAR(20) NOT NULL CONSTRAINT CK_Servicio_Tipo CHECK (Tipo IN ('AYUDA_TECNICA', 'CERT_ACCESIBILIDAD')),
    Documento           VARCHAR(11) NOT NULL,
    NombreSolicitante   NVARCHAR(150) NOT NULL,
    Correo              NVARCHAR(150) NOT NULL,
    Resumen             NVARCHAR(200) NOT NULL,
    Detalle             NVARCHAR(MAX) NOT NULL,
    Unidad              NVARCHAR(100) NOT NULL,     -- unidad responsable (reglas de enrutamiento)
    Estado              NVARCHAR(20)  NOT NULL CONSTRAINT DF_Servicio_Estado DEFAULT (N'Recibida')
                                      CONSTRAINT CK_Servicio_Estado CHECK (Estado IN (N'Recibida', N'EnRevision', N'Aprobada', N'Rechazada', N'Entregada')),
    OrdenId             INT NULL CONSTRAINT FK_Servicio_Orden REFERENCES core.OrdenCobro(OrdenId),
    FechaRegistro       DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Servicio_Fecha DEFAULT (SYSDATETIMEOFFSET()),
    FechaActualizacion  DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Servicio_Actualizacion DEFAULT (SYSDATETIMEOFFSET())
);
GO

/* ----------------------------- HISTORIAL Y NOTIFICACIONES ------------------ */
IF OBJECT_ID(N'core.HistorialEstado', N'U') IS NULL
CREATE TABLE core.HistorialEstado
(
    HistorialId     BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HistorialEstado PRIMARY KEY,
    ExpedienteId    INT NOT NULL CONSTRAINT FK_Historial_Expediente REFERENCES core.Expediente(ExpedienteId),
    EstadoAnterior  NVARCHAR(20) NULL,
    EstadoNuevo     NVARCHAR(20) NOT NULL,
    Unidad          NVARCHAR(100) NOT NULL,   -- responsable por unidad, no por nombre (RF-WEB-02)
    Usuario         NVARCHAR(100) NOT NULL,
    Comentario      NVARCHAR(500) NULL,
    Fecha           DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Historial_Fecha DEFAULT (SYSDATETIMEOFFSET())
);
GO

-- Notificaciones al ciudadano. Las entrega el módulo de Integración (correo/SMS simulados).
IF OBJECT_ID(N'core.Notificacion', N'U') IS NULL
CREATE TABLE core.Notificacion
(
    NotificacionId  BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Notificacion PRIMARY KEY,
    Cedula          CHAR(11) NULL,
    Canal           NVARCHAR(10) NOT NULL CONSTRAINT CK_Notificacion_Canal CHECK (Canal IN (N'Correo', N'SMS', N'Portal')),
    Destinatario    NVARCHAR(150) NOT NULL,
    Asunto          NVARCHAR(150) NOT NULL,
    Mensaje         NVARCHAR(1000) NOT NULL,    -- sin datos clínicos (RINV-01)
    Estado          NVARCHAR(10) NOT NULL CONSTRAINT DF_Notificacion_Estado DEFAULT (N'Pendiente')
                        CONSTRAINT CK_Notificacion_Estado CHECK (Estado IN (N'Pendiente', N'Enviada', N'Fallida')),
    Intentos        INT NOT NULL CONSTRAINT DF_Notificacion_Intentos DEFAULT (0),
    FechaCreacion   DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Notificacion_Fecha DEFAULT (SYSDATETIMEOFFSET()),
    FechaEnvio      DATETIMEOFFSET(0) NULL
);
GO

/* ----------------------------- NUMERACIÓN ---------------------------------- */
-- Contadores por prefijo y año: EXP, TRA, CERT, PCD, OC
IF OBJECT_ID(N'core.Secuencia', N'U') IS NULL
CREATE TABLE core.Secuencia
(
    Prefijo  VARCHAR(10) NOT NULL,
    Anio     INT NOT NULL,
    Ultimo   INT NOT NULL,
    CONSTRAINT PK_Secuencia PRIMARY KEY (Prefijo, Anio)
);
GO

/* ----------------------------- AUDITORÍA INMUTABLE ------------------------- */
-- Solo inserción y encadenamiento de hashes (RNF-SEG-06). Sin FK a propósito (SRS 3.8).
IF OBJECT_ID(N'audit.Evento', N'U') IS NULL
CREATE TABLE audit.Evento
(
    EventoId       BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Evento PRIMARY KEY,
    Fecha          DATETIMEOFFSET(3) NOT NULL CONSTRAINT DF_Evento_Fecha DEFAULT (SYSDATETIMEOFFSET()),
    Usuario        NVARCHAR(100) NOT NULL,
    Ip             VARCHAR(45)   NULL,
    Accion         NVARCHAR(100) NOT NULL,
    Entidad        NVARCHAR(50)  NOT NULL,
    EntidadId      NVARCHAR(50)  NULL,
    Detalle        NVARCHAR(2000) NULL,
    HashAnterior   VARBINARY(32) NULL,
    Hash           VARBINARY(32) NOT NULL
);
GO

CREATE OR ALTER TRIGGER audit.TR_Evento_SoloInsercion
ON audit.Evento
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 50900, N'La bitácora de auditoría no permite modificar ni borrar registros.', 1;
END;
GO

PRINT N'02 - Tablas creadas.';
GO