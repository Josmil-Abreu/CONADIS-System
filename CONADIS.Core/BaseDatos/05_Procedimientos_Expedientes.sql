/* =============================================================================
   CONADIS.Core - 05: Personas, solicitudes, expedientes y citas
   RF-CORE-01 (registro de solicitud y Expediente Único) y diagrama de estados 4.3.
   ============================================================================= */
USE CONADIS_CoreDB;
GO

/* ----------------------------- NOTIFICACIONES (interno) -------------------- */
-- Encola un aviso por el canal preferido de la persona y otro dentro del portal.
-- Los mensajes nunca llevan datos clínicos (RINV-01).
CREATE OR ALTER PROCEDURE core.ppNotificacion_Encolar
    @PersonaId  INT,
    @Asunto     NVARCHAR(150),
    @Mensaje    NVARCHAR(1000)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Cedula CHAR(11), @Canal NVARCHAR(10), @Correo NVARCHAR(150), @Telefono NVARCHAR(20);

    SELECT @Cedula = Cedula, @Canal = CanalNotificacion, @Correo = Correo, @Telefono = Telefono
    FROM core.Persona WHERE PersonaId = @PersonaId;

    IF @Canal = N'Correo' AND @Correo IS NOT NULL
        INSERT INTO core.Notificacion (Cedula, Canal, Destinatario, Asunto, Mensaje)
        VALUES (@Cedula, N'Correo', @Correo, @Asunto, @Mensaje);
    ELSE IF @Canal = N'SMS' AND @Telefono IS NOT NULL
        INSERT INTO core.Notificacion (Cedula, Canal, Destinatario, Asunto, Mensaje)
        VALUES (@Cedula, N'SMS', @Telefono, @Asunto, @Mensaje);

    IF @Cedula IS NOT NULL
        INSERT INTO core.Notificacion (Cedula, Canal, Destinatario, Asunto, Mensaje, Estado, FechaEnvio)
        VALUES (@Cedula, N'Portal', @Cedula, @Asunto, @Mensaje, N'Enviada', SYSDATETIMEOFFSET());
END;
GO

/* ----------------------------- TRANSICIONES DE ESTADO (interno) ------------ */
-- Única forma de cambiar el estado de un expediente. Aplica la tabla de transiciones
-- válidas del SRS 4.3; cualquier otra devuelve EXP_TRANSICION_INVALIDA.
CREATE OR ALTER PROCEDURE core.ppExpediente_CambiarEstado
    @ExpedienteId  INT,
    @EstadoNuevo   NVARCHAR(20),
    @Unidad        NVARCHAR(100),
    @Usuario       NVARCHAR(100),
    @Comentario    NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Actual NVARCHAR(20) = (SELECT Estado FROM core.Expediente WITH (UPDLOCK) WHERE ExpedienteId = @ExpedienteId);
    DECLARE @Msg NVARCHAR(400);

    IF @Actual IS NULL
        THROW 52000, N'El expediente no existe.', 1;

    IF NOT EXISTS (
        SELECT 1 FROM (VALUES
            (N'Solicitado',   N'EnEvaluacion'),  -- cita asignada
            (N'Solicitado',   N'Anulado'),       -- identidad inválida según la JCE
            (N'EnEvaluacion', N'Aprobado'),      -- dictamen favorable
            (N'EnEvaluacion', N'Rechazado'),     -- dictamen desfavorable
            (N'Rechazado',    N'EnApelacion'),   -- apelación dentro del plazo
            (N'Rechazado',    N'Solicitado'),    -- nueva solicitud tras rechazo firme
            (N'EnApelacion',  N'EnEvaluacion'),  -- apelación admitida
            (N'EnApelacion',  N'Rechazado'),     -- apelación denegada
            (N'Aprobado',     N'Carnetizado'),   -- carnet emitido y firmado
            (N'Carnetizado',  N'Vencido'),       -- proceso diario
            (N'Carnetizado',  N'Anulado'),       -- anulación motivada
            (N'Vencido',      N'Solicitado'),    -- renovación
            (N'Anulado',      N'Solicitado')     -- nueva solicitud tras anulación
        ) AS t(Desde, Hacia)
        WHERE t.Desde = @Actual AND t.Hacia = @EstadoNuevo)
    BEGIN
        SET @Msg = CONCAT(N'EXP_TRANSICION_INVALIDA: un expediente en estado ', @Actual,
                          N' no puede pasar a ', @EstadoNuevo, N'.');
        THROW 52010, @Msg, 1;
    END

    UPDATE core.Expediente
    SET Estado = @EstadoNuevo,
        FechaActualizacion = SYSDATETIMEOFFSET(),
        FechaRechazo = CASE WHEN @EstadoNuevo = N'Rechazado' AND @Actual <> N'EnApelacion'
                            THEN SYSDATETIMEOFFSET() ELSE FechaRechazo END
    WHERE ExpedienteId = @ExpedienteId;

    INSERT INTO core.HistorialEstado (ExpedienteId, EstadoAnterior, EstadoNuevo, Unidad, Usuario, Comentario)
    VALUES (@ExpedienteId, @Actual, @EstadoNuevo, @Unidad, @Usuario, @Comentario);

    DECLARE @Detalle NVARCHAR(2000) = CONCAT(@Actual, N' -> ', @EstadoNuevo, N'. ', @Comentario);
    EXEC audit.ppEvento_Registrar @Usuario = @Usuario, @Accion = N'CambioEstado',
         @Entidad = N'Expediente', @EntidadId = @ExpedienteId, @Detalle = @Detalle;
END;
GO

/* ----------------------------- REGISTRO DE SOLICITUD (RF-CORE-01) ---------- */
CREATE OR ALTER PROCEDURE core.ppSolicitud_Registrar
    @Canal                    NVARCHAR(20),
    @UsuarioRegistro          NVARCHAR(100),
    @CedulaCuenta             CHAR(11)      = NULL,
    @Tipo                     NVARCHAR(20),
    @Cedula                   CHAR(11)      = NULL,
    @ActaNacimiento           NVARCHAR(100) = NULL,
    @Nombres                  NVARCHAR(100),
    @Apellidos                NVARCHAR(100),
    @FechaNacimiento          DATE          = NULL,
    @Sexo                     TINYINT       = NULL,
    @EstadoIdentidad          NVARCHAR(20),
    @Telefono                 NVARCHAR(20)  = NULL,
    @Correo                   NVARCHAR(150) = NULL,
    @CanalNotificacion        NVARCHAR(10)  = N'Correo',
    @Provincia                NVARCHAR(60)  = NULL,
    @Municipio                NVARCHAR(60)  = NULL,
    @Sector                   NVARCHAR(100) = NULL,
    @Direccion                NVARCHAR(250) = NULL,
    @CedulaTutor              CHAR(11)      = NULL,
    @VinculoTutor             NVARCHAR(50)  = NULL,
    @Establecimiento          NVARCHAR(200) = NULL,
    @NumeroCertificadoMedico  NVARCHAR(50)  = NULL,
    @FechaCertificadoMedico   DATE          = NULL,
    @CodigoCie10              VARCHAR(10)   = NULL,
    @EstadoDiagnostico        NVARCHAR(30)  = N'ValidacionManual',
    @CentroPreferidoId        INT           = NULL,
    @FechaCitaPreferida       DATETIMEOFFSET(0) = NULL,
    @Apoyos                   NVARCHAR(300) = NULL,
    @VisitaDomiciliaria       BIT           = 0,
    @MotivoDuplicado          NVARCHAR(50)  = NULL,
    @ConsentimientoDatos      BIT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Msg NVARCHAR(400);
    DECLARE @PersonaId INT, @ExpedienteId INT, @Estado NVARCHAR(20), @NumeroExpediente VARCHAR(20);
    DECLARE @NumeroTramite VARCHAR(20), @NumeroOrden VARCHAR(20), @TramiteActivo VARCHAR(20);

    IF @ConsentimientoDatos = 0
        THROW 52001, N'Debes autorizar el tratamiento de tus datos de salud (Ley 172-13) para continuar.', 1;
    IF @EstadoIdentidad = N'Invalida'
        THROW 52002, N'La JCE indica que la cédula está cancelada o la persona falleció. La solicitud no se puede registrar.', 1;
    IF @Cedula IS NULL AND @ActaNacimiento IS NULL
        THROW 52004, N'Indica la cédula o, si es menor sin cédula, los datos del acta de nacimiento.', 1;
    IF @Tipo = N'Duplicado' AND @MotivoDuplicado IS NULL
        THROW 52005, N'Indica el motivo del duplicado (pérdida, deterioro, robo o cambio de datos).', 1;

    BEGIN TRANSACTION;

    /* 1) Persona: una sola por cédula o acta. Los datos de la JCE prevalecen. */
    SELECT @PersonaId = PersonaId
    FROM core.Persona WITH (UPDLOCK, HOLDLOCK)
    WHERE (@Cedula IS NOT NULL AND Cedula = @Cedula)
       OR (@Cedula IS NULL AND ActaNacimiento = @ActaNacimiento);

    IF @PersonaId IS NULL
    BEGIN
        INSERT INTO core.Persona (Cedula, ActaNacimiento, Nombres, Apellidos, FechaNacimiento, Sexo, Telefono, Correo,
                                  CanalNotificacion, Provincia, Municipio, Sector, Direccion, EstadoIdentidad, FechaValidacionJce)
        VALUES (@Cedula, @ActaNacimiento, @Nombres, @Apellidos, @FechaNacimiento, @Sexo, @Telefono, @Correo,
                @CanalNotificacion, @Provincia, @Municipio, @Sector, @Direccion, @EstadoIdentidad,
                CASE WHEN @EstadoIdentidad = N'Validada' THEN SYSDATETIMEOFFSET() END);
        SET @PersonaId = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE core.Persona
        SET Telefono = ISNULL(@Telefono, Telefono),
            Correo = ISNULL(@Correo, Correo),
            CanalNotificacion = ISNULL(@CanalNotificacion, CanalNotificacion),
            Provincia = ISNULL(@Provincia, Provincia),
            Municipio = ISNULL(@Municipio, Municipio),
            Sector = ISNULL(@Sector, Sector),
            Direccion = ISNULL(@Direccion, Direccion),
            -- Solo una validación exitosa de la JCE puede cambiar los datos de identidad
            Nombres = CASE WHEN @EstadoIdentidad = N'Validada' THEN @Nombres ELSE Nombres END,
            Apellidos = CASE WHEN @EstadoIdentidad = N'Validada' THEN @Apellidos ELSE Apellidos END,
            FechaNacimiento = CASE WHEN @EstadoIdentidad = N'Validada' THEN @FechaNacimiento ELSE FechaNacimiento END,
            Sexo = CASE WHEN @EstadoIdentidad = N'Validada' THEN @Sexo ELSE Sexo END,
            EstadoIdentidad = CASE WHEN @EstadoIdentidad = N'Validada' THEN N'Validada' ELSE EstadoIdentidad END,
            FechaValidacionJce = CASE WHEN @EstadoIdentidad = N'Validada' THEN SYSDATETIMEOFFSET() ELSE FechaValidacionJce END
        WHERE PersonaId = @PersonaId;
    END

    IF @CedulaTutor IS NOT NULL AND (@Cedula IS NULL OR @CedulaTutor <> @Cedula)
       AND NOT EXISTS (SELECT 1 FROM core.Tutela WHERE PersonaId = @PersonaId AND CedulaTutor = @CedulaTutor)
        INSERT INTO core.Tutela (PersonaId, CedulaTutor, Vinculo)
        VALUES (@PersonaId, @CedulaTutor, ISNULL(@VinculoTutor, N'Representante'));

    /* 2) Expediente Único: uno por persona. */
    SELECT @ExpedienteId = ExpedienteId, @Estado = Estado, @NumeroExpediente = NumeroExpediente
    FROM core.Expediente WITH (UPDLOCK, HOLDLOCK)
    WHERE PersonaId = @PersonaId;

    /* 3) Reglas por tipo de solicitud. */
    SELECT TOP (1) @TramiteActivo = NumeroTramite
    FROM core.Solicitud
    WHERE ExpedienteId = @ExpedienteId AND Estado = N'Activa' AND Tipo <> N'Duplicado'
    ORDER BY SolicitudId DESC;

    IF @Tipo IN (N'Certificacion', N'Renovacion') AND @Estado IN (N'Solicitado', N'EnEvaluacion', N'Aprobado', N'EnApelacion')
    BEGIN
        SET @Msg = CONCAT(N'Ya existe una solicitud activa (', ISNULL(@TramiteActivo, @NumeroExpediente),
                          N'). Puedes darle seguimiento con ese número.');
        THROW 52003, @Msg, 1;
    END

    IF @Tipo = N'Certificacion' AND @Estado IN (N'Carnetizado', N'Vencido')
        THROW 52006, N'Esta persona ya está certificada. Solicita una renovación o un duplicado del carnet.', 1;

    -- Diagrama de estados 4.3: la renovación parte de un carnet Vencido (Vencido -> Solicitado)
    IF @Tipo = N'Renovacion' AND ISNULL(@Estado, N'') <> N'Vencido'
        THROW 52007, N'La renovación se solicita cuando el carnet está vencido. Si es tu primera vez, elige "Primera vez".', 1;

    IF @Tipo = N'Duplicado' AND ISNULL(@Estado, N'') <> N'Carnetizado'
        THROW 52009, N'Solo se puede pedir duplicado de un carnet vigente.', 1;

    IF @ExpedienteId IS NULL
    BEGIN
        EXEC core.ppSecuencia_Siguiente @Prefijo = 'EXP', @Numero = @NumeroExpediente OUTPUT;
        INSERT INTO core.Expediente (NumeroExpediente, PersonaId, Estado, CentroId)
        VALUES (@NumeroExpediente, @PersonaId, N'Solicitado', @CentroPreferidoId);
        SET @ExpedienteId = SCOPE_IDENTITY();

        INSERT INTO core.HistorialEstado (ExpedienteId, EstadoAnterior, EstadoNuevo, Unidad, Usuario, Comentario)
        VALUES (@ExpedienteId, NULL, N'Solicitado', N'Registro', @UsuarioRegistro, N'Expediente Único creado.');
    END
    ELSE IF @Tipo IN (N'Certificacion', N'Renovacion')
        EXEC core.ppExpediente_CambiarEstado @ExpedienteId = @ExpedienteId, @EstadoNuevo = N'Solicitado',
             @Unidad = N'Registro', @Usuario = @UsuarioRegistro, @Comentario = N'Nueva solicitud registrada.';

    IF @CentroPreferidoId IS NOT NULL
        UPDATE core.Expediente SET CentroId = @CentroPreferidoId WHERE ExpedienteId = @ExpedienteId AND @Tipo <> N'Duplicado';

    /* 4) La solicitud con su número de trámite. */
    EXEC core.ppSecuencia_Siguiente @Prefijo = 'TRA', @Numero = @NumeroTramite OUTPUT;

    INSERT INTO core.Solicitud (NumeroTramite, ExpedienteId, Tipo, Canal, CedulaCuenta, Establecimiento, NumeroCertificadoMedico,
                                FechaCertificadoMedico, CodigoCie10, EstadoDiagnostico, CentroPreferidoId, FechaCitaPreferida,
                                Apoyos, VisitaDomiciliaria, MotivoDuplicado, ConsentimientoDatos, UsuarioRegistro)
    VALUES (@NumeroTramite, @ExpedienteId, @Tipo, @Canal, @CedulaCuenta, @Establecimiento, @NumeroCertificadoMedico,
            @FechaCertificadoMedico, @CodigoCie10, @EstadoDiagnostico, @CentroPreferidoId, @FechaCitaPreferida,
            @Apoyos, @VisitaDomiciliaria, @MotivoDuplicado, @ConsentimientoDatos, @UsuarioRegistro);

    /* 5) Duplicado: genera la orden de cobro de la tasa (RF-WEB-01 paso 5). */
    IF @Tipo = N'Duplicado'
    BEGIN
        DECLARE @TasaId INT, @Monto DECIMAL(18,2), @NombrePagador NVARCHAR(150);
        SELECT @TasaId = TasaId, @Monto = Monto FROM cat.Tasa WHERE Codigo = 'DUP-CARNET' AND Activa = 1;
        IF @TasaId IS NULL
            THROW 52011, N'La tasa de duplicado de carnet no está configurada. Avise al administrador.', 1;

        SET @NombrePagador = CONCAT(@Nombres, N' ', @Apellidos);
        EXEC core.ppSecuencia_Siguiente @Prefijo = 'OC', @Numero = @NumeroOrden OUTPUT;
        INSERT INTO core.OrdenCobro (NumeroOrden, TasaId, Cantidad, MontoUnitario, Total, Documento, NombrePagador, Referencia)
        VALUES (@NumeroOrden, @TasaId, 1, @Monto, @Monto, ISNULL(@CedulaCuenta, @Cedula), @NombrePagador, @NumeroTramite);
    END

    /* 6) Aviso al ciudadano y auditoría. */
    DECLARE @Asunto NVARCHAR(150) = CONCAT(N'Recibimos tu solicitud ', @NumeroTramite);
    DECLARE @Texto NVARCHAR(1000) = CONCAT(N'Tu solicitud ', @NumeroTramite, N' fue registrada. ',
        CASE WHEN @EstadoIdentidad = N'Pendiente' THEN N'Tu identidad será validada automáticamente. ' ELSE N'' END,
        CASE WHEN @NumeroOrden IS NOT NULL THEN CONCAT(N'Para continuar, paga la orden ', @NumeroOrden, N'. ')
             ELSE N'Te avisaremos cuando se asigne tu cita de valoración.' END);
    EXEC core.ppNotificacion_Encolar @PersonaId = @PersonaId, @Asunto = @Asunto, @Mensaje = @Texto;

    DECLARE @Detalle NVARCHAR(2000) = CONCAT(@Tipo, N' por canal ', @Canal, N'. Expediente ', @NumeroExpediente);
    EXEC audit.ppEvento_Registrar @Usuario = @UsuarioRegistro, @Accion = N'SolicitudRegistrada',
         @Entidad = N'Solicitud', @EntidadId = @NumeroTramite, @Detalle = @Detalle;

    COMMIT TRANSACTION;

    SELECT @NumeroTramite AS NumeroTramite, @NumeroExpediente AS NumeroExpediente,
           SYSDATETIMEOFFSET() AS FechaRegistro, @NumeroOrden AS NumeroOrden;
END;
GO

/* ----------------------------- IDENTIDAD Y DIAGNÓSTICO --------------------- */
-- Resultado de una revalidación con la JCE (la hace el módulo de Integración en segundo plano).
CREATE OR ALTER PROCEDURE core.ppPersona_RegistrarValidacionJce
    @Cedula           CHAR(11),
    @Resultado        NVARCHAR(20),     -- Validada | Invalida
    @Nombres          NVARCHAR(100) = NULL,
    @Apellidos        NVARCHAR(100) = NULL,
    @FechaNacimiento  DATE = NULL,
    @Sexo             TINYINT = NULL,
    @Usuario          NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @PersonaId INT, @ExpedienteId INT, @Estado NVARCHAR(20);

    SELECT @PersonaId = PersonaId FROM core.Persona WHERE Cedula = @Cedula;
    IF @PersonaId IS NULL THROW 52020, N'No existe una persona con esa cédula.', 1;

    BEGIN TRANSACTION;
        IF @Resultado = N'Validada'
            UPDATE core.Persona
            SET Nombres = ISNULL(@Nombres, Nombres), Apellidos = ISNULL(@Apellidos, Apellidos),
                FechaNacimiento = ISNULL(@FechaNacimiento, FechaNacimiento), Sexo = ISNULL(@Sexo, Sexo),
                EstadoIdentidad = N'Validada', FechaValidacionJce = SYSDATETIMEOFFSET()
            WHERE PersonaId = @PersonaId;
        ELSE IF @Resultado = N'Invalida'
        BEGIN
            UPDATE core.Persona SET EstadoIdentidad = N'Invalida', FechaValidacionJce = SYSDATETIMEOFFSET()
            WHERE PersonaId = @PersonaId;

            -- Cédula cancelada o persona fallecida: la solicitud se rechaza con motivo (RF-CORE-01)
            SELECT @ExpedienteId = ExpedienteId, @Estado = Estado FROM core.Expediente WHERE PersonaId = @PersonaId;
            IF @Estado = N'Solicitado'
            BEGIN
                UPDATE core.Solicitud SET Estado = N'Anulada' WHERE ExpedienteId = @ExpedienteId AND Estado = N'Activa';
                EXEC core.ppExpediente_CambiarEstado @ExpedienteId = @ExpedienteId, @EstadoNuevo = N'Anulado',
                     @Unidad = N'Integración JCE', @Usuario = @Usuario,
                     @Comentario = N'La JCE informó cédula cancelada o persona fallecida.';
            END
        END
        ELSE
            THROW 52021, N'Resultado de validación no válido.', 1;

        DECLARE @EntidadId NVARCHAR(50) = CAST(@PersonaId AS NVARCHAR(50));
        EXEC audit.ppEvento_Registrar @Usuario = @Usuario, @Accion = N'ValidacionJce',
             @Entidad = N'Persona', @EntidadId = @EntidadId, @Detalle = @Resultado;
    COMMIT TRANSACTION;
END;
GO

-- Personas cuya identidad sigue pendiente (reintento cada 15 min durante 24 h)
CREATE OR ALTER PROCEDURE core.ppPersona_ListarIdentidadPendiente
    @HorasMaximas INT = 24
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.PersonaId, p.Cedula, p.Nombres, p.Apellidos, e.NumeroExpediente, p.FechaCreacion,
           CASE WHEN p.FechaCreacion < DATEADD(HOUR, -@HorasMaximas, SYSDATETIMEOFFSET()) THEN 1 ELSE 0 END AS RequiereAnalista
    FROM core.Persona p
    LEFT JOIN core.Expediente e ON e.PersonaId = p.PersonaId
    WHERE p.EstadoIdentidad = N'Pendiente' AND p.Cedula IS NOT NULL
    ORDER BY p.FechaCreacion;
END;
GO

-- El analista verificó el documento físico (validación manual)
CREATE OR ALTER PROCEDURE core.ppPersona_AprobarIdentidadManual
    @PersonaId   INT,
    @Usuario     NVARCHAR(100),
    @Comentario  NVARCHAR(500)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE core.Persona SET EstadoIdentidad = N'Manual'
    WHERE PersonaId = @PersonaId AND EstadoIdentidad = N'Pendiente';

    IF @@ROWCOUNT = 0 THROW 52022, N'La identidad de esta persona no está pendiente de validación.', 1;

    DECLARE @EntidadId NVARCHAR(50) = CAST(@PersonaId AS NVARCHAR(50));
    EXEC audit.ppEvento_Registrar @Usuario = @Usuario, @Accion = N'IdentidadValidadaManualmente',
         @Entidad = N'Persona', @EntidadId = @EntidadId, @Detalle = @Comentario;
END;
GO

CREATE OR ALTER PROCEDURE core.ppSolicitud_RegistrarValidacionMsp
    @NumeroTramite  VARCHAR(20),
    @Estado         NVARCHAR(30),
    @Usuario        NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE core.Solicitud SET EstadoDiagnostico = @Estado WHERE NumeroTramite = @NumeroTramite;
    IF @@ROWCOUNT = 0 THROW 52023, N'La solicitud no existe.', 1;

    EXEC audit.ppEvento_Registrar @Usuario = @Usuario, @Accion = N'ValidacionMsp',
         @Entidad = N'Solicitud', @EntidadId = @NumeroTramite, @Detalle = @Estado;
END;
GO

/* ----------------------------- CONSULTAS DEL PANEL INTERNO ----------------- */
CREATE OR ALTER PROCEDURE core.ppExpediente_Buscar
    @Texto     NVARCHAR(100) = NULL,   -- número, cédula o nombre
    @Estado    NVARCHAR(20)  = NULL,
    @CentroId  INT           = NULL    -- evaluadores: solo su centro (RNF-SEG-05)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (500)
           e.ExpedienteId, e.NumeroExpediente, p.Cedula, CONCAT(p.Nombres, N' ', p.Apellidos) AS NombreCompleto,
           e.Estado, e.TipoDiscapacidad, e.GradoDiscapacidad, c.Nombre AS Centro, p.EstadoIdentidad,
           e.FechaActualizacion
    FROM core.Expediente e
    INNER JOIN core.Persona p ON p.PersonaId = e.PersonaId
    LEFT JOIN cat.CentroValoracion c ON c.CentroId = e.CentroId
    WHERE (@Estado IS NULL OR e.Estado = @Estado)
      AND (@CentroId IS NULL OR e.CentroId = @CentroId)
      AND (@Texto IS NULL
           OR e.NumeroExpediente = @Texto
           OR p.Cedula = REPLACE(@Texto, '-', '')
           OR CONCAT(p.Nombres, N' ', p.Apellidos) LIKE N'%' + @Texto + N'%'
           OR EXISTS (SELECT 1 FROM core.Solicitud s WHERE s.ExpedienteId = e.ExpedienteId AND s.NumeroTramite = @Texto))
    ORDER BY e.FechaActualizacion DESC;
END;
GO

-- Vista consolidada del expediente: devuelve 8 conjuntos de resultados.
CREATE OR ALTER PROCEDURE core.ppExpediente_Obtener
    @ExpedienteId INT
AS
BEGIN
    SET NOCOUNT ON;

    -- 1) Cabecera
    SELECT e.ExpedienteId, e.NumeroExpediente, e.Estado, e.TipoDiscapacidad, e.GradoDiscapacidad, e.FechaVencimiento,
           e.CentroId, c.Nombre AS Centro, e.FechaCreacion, e.FechaActualizacion, e.FechaRechazo,
           p.PersonaId, p.Cedula, p.ActaNacimiento, p.Nombres, p.Apellidos, p.FechaNacimiento, p.Sexo,
           p.Telefono, p.Correo, p.CanalNotificacion, p.Provincia, p.Municipio, p.Sector, p.Direccion,
           p.EstadoIdentidad, p.FechaValidacionJce
    FROM core.Expediente e
    INNER JOIN core.Persona p ON p.PersonaId = e.PersonaId
    LEFT JOIN cat.CentroValoracion c ON c.CentroId = e.CentroId
    WHERE e.ExpedienteId = @ExpedienteId;

    -- 2) Solicitudes
    SELECT s.SolicitudId, s.NumeroTramite, s.Tipo, s.Canal, s.Estado, s.Establecimiento, s.NumeroCertificadoMedico,
           s.CodigoCie10, s.EstadoDiagnostico, s.Apoyos, s.VisitaDomiciliaria, s.MotivoDuplicado,
           s.FechaCitaPreferida, cp.Nombre AS CentroPreferido, s.FechaRegistro, s.UsuarioRegistro
    FROM core.Solicitud s
    LEFT JOIN cat.CentroValoracion cp ON cp.CentroId = s.CentroPreferidoId
    WHERE s.ExpedienteId = @ExpedienteId
    ORDER BY s.SolicitudId DESC;

    -- 3) Historial de estados
    SELECT EstadoAnterior, EstadoNuevo, Unidad, Usuario, Comentario, Fecha
    FROM core.HistorialEstado WHERE ExpedienteId = @ExpedienteId ORDER BY HistorialId;

    -- 4) Citas
    SELECT ci.CitaId, ce.Nombre AS Centro, ci.FechaHora, ci.Domiciliaria, ci.Estado
    FROM core.Cita ci INNER JOIN cat.CentroValoracion ce ON ce.CentroId = ci.CentroId
    WHERE ci.ExpedienteId = @ExpedienteId ORDER BY ci.CitaId DESC;

    -- 5) Evaluaciones
    SELECT ev.EvaluacionId, ev.FechaEvaluacion, ev.Estado, ev.PuntajeWhodas, ev.GradoPropuesto, ev.TipoPropuesto,
           ev.VersionBaremo, d.Resultado AS Dictamen
    FROM core.EvaluacionCIF ev LEFT JOIN core.Dictamen d ON d.EvaluacionId = ev.EvaluacionId
    WHERE ev.ExpedienteId = @ExpedienteId ORDER BY ev.EvaluacionId DESC;

    -- 6) Carnets
    SELECT CarnetId, NumeroCarnet, NumeroCertificado, FechaEmision, FechaVencimiento, Estado, MotivoAnulacion
    FROM core.Carnet WHERE ExpedienteId = @ExpedienteId ORDER BY CarnetId DESC;

    -- 7) Apelaciones
    SELECT ApelacionId, Motivo, Estado, Resolucion, FechaRegistro, FechaResolucion
    FROM core.Apelacion WHERE ExpedienteId = @ExpedienteId ORDER BY ApelacionId DESC;

    -- 8) Órdenes de cobro relacionadas con sus trámites
    SELECT o.NumeroOrden, t.Descripcion AS Concepto, o.Total, o.Estado, o.FechaCreacion, o.FechaPago, o.NumeroComprobante
    FROM core.OrdenCobro o
    INNER JOIN cat.Tasa t ON t.TasaId = o.TasaId
    WHERE o.Referencia IN (SELECT NumeroTramite FROM core.Solicitud WHERE ExpedienteId = @ExpedienteId)
    ORDER BY o.OrdenId DESC;
END;
GO

/* ----------------------------- CONSULTAS DEL CIUDADANO --------------------- */
-- Trámites de una cédula: los suyos y los de las personas que representa.
CREATE OR ALTER PROCEDURE core.ppTramite_ListarPorCedula
    @Cedula CHAR(11)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.NumeroTramite AS Numero,
           CASE s.Tipo WHEN N'Certificacion' THEN N'Certificación y carnet'
                       WHEN N'Renovacion' THEN N'Renovación de carnet'
                       ELSE N'Duplicado de carnet' END AS Tipo,
           CASE WHEN s.Estado = N'Activa' THEN e.Estado ELSE s.Estado END AS Estado,
           e.FechaActualizacion AS Actualizado,
           CONCAT(p.Nombres, N' ', p.Apellidos) AS Persona
    FROM core.Solicitud s
    INNER JOIN core.Expediente e ON e.ExpedienteId = s.ExpedienteId
    INNER JOIN core.Persona p ON p.PersonaId = e.PersonaId
    WHERE p.Cedula = @Cedula
       OR s.CedulaCuenta = @Cedula
       OR EXISTS (SELECT 1 FROM core.Tutela t WHERE t.PersonaId = p.PersonaId AND t.CedulaTutor = @Cedula)
    UNION ALL
    SELECT sv.NumeroTramite,
           CASE sv.Tipo WHEN 'AYUDA_TECNICA' THEN N'Ayuda técnica' ELSE N'Certificación de accesibilidad' END,
           sv.Estado, sv.FechaActualizacion, sv.NombreSolicitante
    FROM core.SolicitudServicio sv
    WHERE sv.Documento = @Cedula
    ORDER BY Actualizado DESC;
END;
GO

-- Un trámite, solo si pertenece a la cédula indicada (titular, tutor o quien lo presentó).
-- Si no pertenece, no devuelve filas: el portal muestra un mensaje genérico (sin enumeración).
-- Devuelve 2 conjuntos: cabecera e historial.
CREATE OR ALTER PROCEDURE core.ppTramite_ObtenerCiudadano
    @Numero  VARCHAR(20),
    @Cedula  CHAR(11)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ExpedienteId INT;

    SELECT TOP (1) @ExpedienteId = e.ExpedienteId
    FROM core.Expediente e
    INNER JOIN core.Persona p ON p.PersonaId = e.PersonaId
    LEFT JOIN core.Solicitud s ON s.ExpedienteId = e.ExpedienteId
    WHERE (e.NumeroExpediente = @Numero OR s.NumeroTramite = @Numero)
      AND (p.Cedula = @Cedula OR s.CedulaCuenta = @Cedula
           OR EXISTS (SELECT 1 FROM core.Tutela t WHERE t.PersonaId = p.PersonaId AND t.CedulaTutor = @Cedula));

    IF @ExpedienteId IS NOT NULL
    BEGIN
        SELECT @Numero AS Numero, e.NumeroExpediente, p.Cedula, CONCAT(p.Nombres, N' ', p.Apellidos) AS NombreCompleto,
               e.Estado, e.FechaCreacion AS FechaRegistro, e.FechaActualizacion, e.TipoDiscapacidad, e.GradoDiscapacidad,
               (SELECT TOP (1) NumeroCarnet FROM core.Carnet c WHERE c.ExpedienteId = e.ExpedienteId AND c.Estado = N'Vigente') AS NumeroCarnet,
               ce.Nombre AS CentroValoracion,
               (SELECT TOP (1) FechaHora FROM core.Cita ci WHERE ci.ExpedienteId = e.ExpedienteId AND ci.Estado = N'Programada' ORDER BY ci.CitaId DESC) AS FechaCita,
               CASE WHEN p.EstadoIdentidad = N'Pendiente' THEN N'Identidad pendiente de validación.' END AS Observaciones
        FROM core.Expediente e
        INNER JOIN core.Persona p ON p.PersonaId = e.PersonaId
        LEFT JOIN cat.CentroValoracion ce ON ce.CentroId = e.CentroId
        WHERE e.ExpedienteId = @ExpedienteId;

        SELECT EstadoNuevo AS Estado, Fecha, CONCAT(Unidad, CASE WHEN Comentario IS NULL THEN N'' ELSE N': ' + Comentario END) AS Comentario
        FROM core.HistorialEstado WHERE ExpedienteId = @ExpedienteId ORDER BY HistorialId;
        RETURN;
    END

    -- Solicitudes de servicio (ayudas técnicas, accesibilidad)
    SELECT sv.NumeroTramite AS Numero, CAST(NULL AS VARCHAR(20)) AS NumeroExpediente, sv.Documento AS Cedula,
           sv.NombreSolicitante AS NombreCompleto, sv.Estado, sv.FechaRegistro, sv.FechaActualizacion,
           CAST(NULL AS NVARCHAR(20)) AS TipoDiscapacidad, CAST(NULL AS NVARCHAR(20)) AS GradoDiscapacidad,
           CAST(NULL AS VARCHAR(20)) AS NumeroCarnet, sv.Unidad AS CentroValoracion,
           CAST(NULL AS DATETIMEOFFSET(0)) AS FechaCita, sv.Resumen AS Observaciones
    FROM core.SolicitudServicio sv
    WHERE sv.NumeroTramite = @Numero AND sv.Documento = @Cedula;

    SELECT sv.Estado, sv.FechaActualizacion AS Fecha, sv.Unidad AS Comentario
    FROM core.SolicitudServicio sv
    WHERE sv.NumeroTramite = @Numero AND sv.Documento = @Cedula;
END;
GO

/* ----------------------------- CITAS --------------------------------------- */
-- Días con cupo en un centro (para proponer citas, CU-01 paso 8)
CREATE OR ALTER PROCEDURE core.ppCita_Disponibilidad
    @CentroId  INT,
    @Desde     DATE,
    @Dias      INT = 15
AS
BEGIN
    SET NOCOUNT ON;
    WITH Dias AS
    (
        SELECT TOP (@Dias) DATEADD(DAY, ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1, @Desde) AS Fecha
        FROM sys.all_objects
    )
    SELECT d.Fecha, c.CapacidadDiaria,
           c.CapacidadDiaria - COUNT(ci.CitaId) AS CuposLibres
    FROM Dias d
    CROSS JOIN cat.CentroValoracion c
    LEFT JOIN core.Cita ci ON ci.CentroId = c.CentroId AND ci.Estado = N'Programada'
                          AND CAST(ci.FechaHora AS DATE) = d.Fecha
    WHERE c.CentroId = @CentroId
      -- Sin sábados ni domingos. 1900-01-01 fue lunes: el resto 5 = sábado y 6 = domingo,
      -- sin depender del idioma del servidor (DATEFIRST).
      AND DATEDIFF(DAY, '19000101', d.Fecha) % 7 NOT IN (5, 6)
    GROUP BY d.Fecha, c.CapacidadDiaria
    HAVING c.CapacidadDiaria - COUNT(ci.CitaId) > 0
    ORDER BY d.Fecha;
END;
GO

-- Asignar cita: Solicitado -> EnEvaluacion (requiere identidad validada o validación manual)
CREATE OR ALTER PROCEDURE core.ppCita_Asignar
    @ExpedienteId  INT,
    @CentroId      INT,
    @FechaHora     DATETIMEOFFSET(0),
    @Domiciliaria  BIT = 0,
    @Usuario       NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Estado NVARCHAR(20), @EstadoIdentidad NVARCHAR(20), @PersonaId INT, @Capacidad INT, @Ocupadas INT;
    DECLARE @CitaId INT;

    IF @FechaHora <= SYSDATETIMEOFFSET()
        THROW 52030, N'La cita debe ser en una fecha futura.', 1;

    BEGIN TRANSACTION;
        SELECT @Estado = e.Estado, @EstadoIdentidad = p.EstadoIdentidad, @PersonaId = p.PersonaId
        FROM core.Expediente e WITH (UPDLOCK)
        INNER JOIN core.Persona p ON p.PersonaId = e.PersonaId
        WHERE e.ExpedienteId = @ExpedienteId;

        IF @Estado IS NULL THROW 52000, N'El expediente no existe.', 1;
        IF @Estado NOT IN (N'Solicitado', N'EnEvaluacion')
            THROW 52031, N'Solo se asignan citas a expedientes en estado Solicitado o En Evaluación.', 1;
        IF @Estado = N'Solicitado' AND @EstadoIdentidad NOT IN (N'Validada', N'Manual')
            THROW 52032, N'La identidad aún no está validada. Valídela con la JCE o apruébela manualmente con el documento físico.', 1;

        SELECT @Capacidad = CapacidadDiaria FROM cat.CentroValoracion WHERE CentroId = @CentroId AND Activo = 1;
        IF @Capacidad IS NULL THROW 52033, N'El centro de valoración no existe o está inactivo.', 1;

        SELECT @Ocupadas = COUNT(*) FROM core.Cita WITH (UPDLOCK, HOLDLOCK)
        WHERE CentroId = @CentroId AND Estado = N'Programada' AND CAST(FechaHora AS DATE) = CAST(@FechaHora AS DATE);
        IF @Ocupadas >= @Capacidad
            THROW 52034, N'No hay cupo ese día en el centro elegido. Elija otra fecha u otro centro.', 1;

        UPDATE core.Cita SET Estado = N'Reprogramada' WHERE ExpedienteId = @ExpedienteId AND Estado = N'Programada';

        INSERT INTO core.Cita (ExpedienteId, CentroId, FechaHora, Domiciliaria, Estado, UsuarioRegistro)
        VALUES (@ExpedienteId, @CentroId, @FechaHora, @Domiciliaria, N'Programada', @Usuario);
        SET @CitaId = SCOPE_IDENTITY();

        UPDATE core.Expediente SET CentroId = @CentroId WHERE ExpedienteId = @ExpedienteId;

        IF @Estado = N'Solicitado'
            EXEC core.ppExpediente_CambiarEstado @ExpedienteId = @ExpedienteId, @EstadoNuevo = N'EnEvaluacion',
                 @Unidad = N'Valoración y Certificación', @Usuario = @Usuario, @Comentario = N'Cita de valoración asignada.';

        DECLARE @Texto NVARCHAR(1000) = CONCAT(N'Tu cita de valoración es el ', FORMAT(@FechaHora, 'dd/MM/yyyy HH:mm', 'es-DO'),
            CASE WHEN @Domiciliaria = 1 THEN N' (visita domiciliaria).' ELSE N'.' END);
        EXEC core.ppNotificacion_Encolar @PersonaId = @PersonaId, @Asunto = N'Cita de valoración asignada', @Mensaje = @Texto;
    COMMIT TRANSACTION;

    SELECT @CitaId AS CitaId;
END;
GO

-- El ciudadano pide otra fecha desde el portal
CREATE OR ALTER PROCEDURE core.ppCita_ReprogramarCiudadano
    @Numero      VARCHAR(20),
    @Cedula      CHAR(11),
    @NuevaFecha  DATETIMEOFFSET(0)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @ExpedienteId INT, @CentroId INT, @Domiciliaria BIT, @Capacidad INT, @Ocupadas INT;

    IF @NuevaFecha <= SYSDATETIMEOFFSET() THROW 52030, N'La cita debe ser en una fecha futura.', 1;

    SELECT TOP (1) @ExpedienteId = e.ExpedienteId
    FROM core.Expediente e
    INNER JOIN core.Persona p ON p.PersonaId = e.PersonaId
    LEFT JOIN core.Solicitud s ON s.ExpedienteId = e.ExpedienteId
    WHERE (e.NumeroExpediente = @Numero OR s.NumeroTramite = @Numero)
      AND (p.Cedula = @Cedula OR s.CedulaCuenta = @Cedula
           OR EXISTS (SELECT 1 FROM core.Tutela t WHERE t.PersonaId = p.PersonaId AND t.CedulaTutor = @Cedula));

    IF @ExpedienteId IS NULL THROW 52035, N'No encontramos un trámite con esos datos.', 1;

    BEGIN TRANSACTION;
        SELECT TOP (1) @CentroId = CentroId, @Domiciliaria = Domiciliaria
        FROM core.Cita WHERE ExpedienteId = @ExpedienteId AND Estado = N'Programada' ORDER BY CitaId DESC;

        IF @CentroId IS NULL
        BEGIN
            -- Aún sin cita asignada: se guarda como preferencia para el analista
            UPDATE core.Solicitud SET FechaCitaPreferida = @NuevaFecha
            WHERE ExpedienteId = @ExpedienteId AND Estado = N'Activa';
        END
        ELSE
        BEGIN
            SELECT @Capacidad = CapacidadDiaria FROM cat.CentroValoracion WHERE CentroId = @CentroId;
            SELECT @Ocupadas = COUNT(*) FROM core.Cita WITH (UPDLOCK, HOLDLOCK)
            WHERE CentroId = @CentroId AND Estado = N'Programada' AND CAST(FechaHora AS DATE) = CAST(@NuevaFecha AS DATE);
            IF @Ocupadas >= @Capacidad
                THROW 52034, N'No hay cupo ese día en el centro elegido. Elija otra fecha u otro centro.', 1;

            UPDATE core.Cita SET Estado = N'Reprogramada' WHERE ExpedienteId = @ExpedienteId AND Estado = N'Programada';
            INSERT INTO core.Cita (ExpedienteId, CentroId, FechaHora, Domiciliaria, Estado, UsuarioRegistro)
            VALUES (@ExpedienteId, @CentroId, @NuevaFecha, @Domiciliaria, N'Programada', CONCAT(N'ciudadano:', @Cedula));
        END

        INSERT INTO core.HistorialEstado (ExpedienteId, EstadoAnterior, EstadoNuevo, Unidad, Usuario, Comentario)
        SELECT ExpedienteId, Estado, Estado, N'Portal Ciudadano', CONCAT(N'ciudadano:', @Cedula), N'Cita reprogramada a pedido del ciudadano.'
        FROM core.Expediente WHERE ExpedienteId = @ExpedienteId;
    COMMIT TRANSACTION;
END;
GO

-- Agenda de un centro en un día (los evaluadores solo ven su centro)
CREATE OR ALTER PROCEDURE core.ppCita_Agenda
    @CentroId  INT,
    @Fecha     DATE
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ci.CitaId, ci.FechaHora, ci.Domiciliaria, ci.Estado, e.ExpedienteId, e.NumeroExpediente,
           CONCAT(p.Nombres, N' ', p.Apellidos) AS NombreCompleto
    FROM core.Cita ci
    INNER JOIN core.Expediente e ON e.ExpedienteId = ci.ExpedienteId
    INNER JOIN core.Persona p ON p.PersonaId = e.PersonaId
    WHERE ci.CentroId = @CentroId AND CAST(ci.FechaHora AS DATE) = @Fecha AND ci.Estado IN (N'Programada', N'Realizada')
    ORDER BY ci.FechaHora;
END;
GO

PRINT N'05 - Procedimientos de expedientes creados.';
GO