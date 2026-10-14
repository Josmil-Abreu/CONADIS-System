/* =============================================================================
   CONADIS.Web - 07: Bandeja de salida del portal (tolerancia a caídas)
   Si Integración o el Core están caídos cuando el ciudadano envía algo (solicitud, apelación,
   cambio de cita, ayuda técnica, duplicado) la operación NO se pierde: queda en web.Envio y un
   proceso del portal la reenvía cada 30 s hasta que los demás módulos vuelvan.
   Cada envío lleva su clave de idempotencia: aunque se reintente, Integración no lo duplica.
   También se guarda aquí la validación de identidad (JCE) de las cuentas creadas sin conexión.
   ============================================================================= */
USE CONADIS_WebDB;
GO

IF OBJECT_ID(N'web.Envio', N'U') IS NULL
CREATE TABLE web.Envio
(
    EnvioId            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Envio PRIMARY KEY,
    CuentaId           INT NOT NULL CONSTRAINT FK_Envio_Cuenta REFERENCES web.Cuenta(CuentaId),
    Tipo               VARCHAR(30)   NOT NULL,      -- Solicitud | Servicio | Apelacion | Cita | ValidarIdentidad
    Metodo             VARCHAR(6)    NOT NULL CONSTRAINT CK_Envio_Metodo CHECK (Metodo IN ('GET', 'POST', 'PUT')),
    Ruta               NVARCHAR(300) NOT NULL,
    Carga              NVARCHAR(MAX) NOT NULL,
    ClaveIdempotencia  VARCHAR(150)  NOT NULL CONSTRAINT UQ_Envio_Clave UNIQUE,
    Descripcion        NVARCHAR(200) NOT NULL,
    Estado             VARCHAR(12)   NOT NULL CONSTRAINT DF_Envio_Estado DEFAULT ('Pendiente')
                       CONSTRAINT CK_Envio_Estado CHECK (Estado IN ('Pendiente', 'EnProceso', 'Enviado', 'Rechazado')),
    Intentos           INT NOT NULL CONSTRAINT DF_Envio_Intentos DEFAULT (0),
    ProximoIntento     DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Envio_Proximo DEFAULT (SYSDATETIMEOFFSET()),
    BloqueadoHasta     DATETIMEOFFSET(0) NULL,
    UltimoError        NVARCHAR(500) NULL,
    Respuesta          NVARCHAR(MAX) NULL,
    FechaCreacion      DATETIMEOFFSET(0) NOT NULL CONSTRAINT DF_Envio_Fecha DEFAULT (SYSDATETIMEOFFSET()),
    FechaEnvio         DATETIMEOFFSET(0) NULL
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Envio_Pendientes')
    CREATE INDEX IX_Envio_Pendientes ON web.Envio (Estado, ProximoIntento) INCLUDE (CuentaId);
GO

-- Encola una operación. Si ya existe la misma clave (doble clic, reintento), devuelve la existente.
CREATE OR ALTER PROCEDURE web.ppEnvio_Encolar
    @CuentaId          INT,
    @Tipo              VARCHAR(30),
    @Metodo            VARCHAR(6),
    @Ruta              NVARCHAR(300),
    @Carga             NVARCHAR(MAX),
    @ClaveIdempotencia VARCHAR(150),
    @Descripcion       NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM web.Envio WHERE ClaveIdempotencia = @ClaveIdempotencia)
    BEGIN TRY
        INSERT INTO web.Envio (CuentaId, Tipo, Metodo, Ruta, Carga, ClaveIdempotencia, Descripcion)
        VALUES (@CuentaId, @Tipo, @Metodo, @Ruta, @Carga, @ClaveIdempotencia, LEFT(@Descripcion, 200));
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() NOT IN (2601, 2627) THROW;   -- otra petición la encoló al mismo tiempo
    END CATCH;
    SELECT EnvioId, Estado FROM web.Envio WHERE ClaveIdempotencia = @ClaveIdempotencia;
END;
GO

-- Toma un lote para enviar (READPAST: dos instancias del portal no toman el mismo envío)
CREATE OR ALTER PROCEDURE web.ppEnvio_Tomar
    @Maximo INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Ahora DATETIMEOFFSET(0) = SYSDATETIMEOFFSET();
    DECLARE @Tomados TABLE (EnvioId BIGINT PRIMARY KEY);
    ;WITH Siguientes AS
    (
        SELECT TOP (@Maximo) e.EnvioId, e.Estado, e.Intentos, e.BloqueadoHasta
        FROM web.Envio e WITH (ROWLOCK, READPAST, UPDLOCK)
        WHERE (e.Estado = 'Pendiente' AND e.ProximoIntento <= @Ahora)
           OR (e.Estado = 'EnProceso' AND e.BloqueadoHasta < @Ahora)
        ORDER BY e.ProximoIntento, e.EnvioId
    )
    UPDATE Siguientes
    SET Estado = 'EnProceso', BloqueadoHasta = DATEADD(MINUTE, 2, @Ahora), Intentos = Intentos + 1
    OUTPUT inserted.EnvioId INTO @Tomados;

    SELECT e.EnvioId, e.CuentaId, c.Cedula, e.Tipo, e.Metodo, e.Ruta, e.Carga, e.ClaveIdempotencia, e.Descripcion, e.Intentos
    FROM web.Envio e
    INNER JOIN @Tomados t ON t.EnvioId = e.EnvioId
    INNER JOIN web.Cuenta c ON c.CuentaId = e.CuentaId
    ORDER BY e.EnvioId;
END;
GO

CREATE OR ALTER PROCEDURE web.ppEnvio_Completar
    @EnvioId   BIGINT,
    @Respuesta NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE web.Envio
    SET Estado = 'Enviado', Respuesta = @Respuesta, FechaEnvio = SYSDATETIMEOFFSET(), BloqueadoHasta = NULL, UltimoError = NULL
    WHERE EnvioId = @EnvioId;
END;
GO

-- Transitorio (Integración/Core caídos): vuelve a Pendiente, reintento en 15 a 30 s, nunca se descarta.
-- Permanente (el Core rechazó los datos): Rechazado, el ciudadano lo ve en "Mi actividad".
CREATE OR ALTER PROCEDURE web.ppEnvio_RegistrarFallo
    @EnvioId    BIGINT,
    @Error      NVARCHAR(500),
    @Permanente BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE web.Envio
    SET Estado = CASE WHEN @Permanente = 1 THEN 'Rechazado' ELSE 'Pendiente' END,
        UltimoError = LEFT(@Error, 500), BloqueadoHasta = NULL,
        ProximoIntento = DATEADD(SECOND, CASE WHEN Intentos <= 1 THEN 15 ELSE 30 END, SYSDATETIMEOFFSET())
    WHERE EnvioId = @EnvioId;
END;
GO

-- Lo que el ciudadano tiene pendiente de envío (se muestra en "Mis trámites")
CREATE OR ALTER PROCEDURE web.ppEnvio_ListarPorCuenta
    @CuentaId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT EnvioId, Tipo, Descripcion, Estado, Intentos, ISNULL(UltimoError, N'') AS UltimoError,
           FechaCreacion, FechaEnvio
    FROM web.Envio
    WHERE CuentaId = @CuentaId AND Tipo <> 'ValidarIdentidad'
      AND (Estado IN ('Pendiente', 'EnProceso', 'Rechazado') OR FechaEnvio > DATEADD(DAY, -1, SYSDATETIMEOFFSET()))
    ORDER BY EnvioId DESC;
END;
GO

-- Resultado de la validación con la JCE de una cuenta creada sin conexión
CREATE OR ALTER PROCEDURE web.ppCuenta_ActualizarIdentidad
    @CuentaId        INT,
    @NombreCompleto  NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE web.Cuenta SET EstadoIdentidad = N'Validada', NombreCompleto = @NombreCompleto WHERE CuentaId = @CuentaId;
    EXEC web.ppEvento_Registrar @CuentaId = @CuentaId, @Evento = N'IdentidadValidada', @Detalle = N'Validada con la JCE al volver la conexión.';
END;
GO

PRINT N'07 - Bandeja de salida del portal creada.';
GO
