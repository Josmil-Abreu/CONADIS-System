/* =============================================================================
   CONADIS.Core - 06: Valoración CIF, dictamen, apelaciones y carnets
   RF-CORE-02 (valoración multidimensional) y RF-CORE-03 (dictamen y carnet).
   ============================================================================= */
USE CONADIS_CoreDB;
GO

/* ----------------------------- VALORACIÓN CIF (RF-CORE-02) ----------------- */
-- Abre (o devuelve) la evaluación en curso del expediente.
CREATE OR ALTER PROCEDURE core.ppEvaluacion_Iniciar
    @ExpedienteId  INT,
    @UsuarioId     INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Estado NVARCHAR(20), @CentroExpediente INT, @CentroUsuario INT, @Rol NVARCHAR(50);
    DECLARE @EvaluacionId INT, @CitaId INT, @Username NVARCHAR(50);

    SELECT @Estado = Estado, @CentroExpediente = CentroId FROM core.Expediente WHERE ExpedienteId = @ExpedienteId;
    SELECT @CentroUsuario = u.CentroId, @Rol = r.Nombre, @Username = u.Username
    FROM seg.Usuario u INNER JOIN seg.Rol r ON r.RolId = u.RolId WHERE u.UsuarioId = @UsuarioId;

    IF @Estado IS NULL THROW 52000, N'El expediente no existe.', 1;
    IF @Estado <> N'EnEvaluacion'
        THROW 53001, N'Solo se valoran expedientes en estado En Evaluación (con cita asignada).', 1;
    IF @Rol = N'Evaluador' AND ISNULL(@CentroUsuario, -1) <> ISNULL(@CentroExpediente, -2)
        THROW 53002, N'Este expediente pertenece a otro centro de valoración.', 1;

    BEGIN TRANSACTION;
        SELECT TOP (1) @EvaluacionId = EvaluacionId
        FROM core.EvaluacionCIF WITH (UPDLOCK, HOLDLOCK)
        WHERE ExpedienteId = @ExpedienteId AND Estado IN (N'Borrador', N'Completa')
        ORDER BY EvaluacionId DESC;

        IF @EvaluacionId IS NULL
        BEGIN
            SELECT TOP (1) @CitaId = CitaId FROM core.Cita
            WHERE ExpedienteId = @ExpedienteId AND Estado = N'Programada' ORDER BY CitaId DESC;

            INSERT INTO core.EvaluacionCIF (ExpedienteId, CitaId) VALUES (@ExpedienteId, @CitaId);
            SET @EvaluacionId = SCOPE_IDENTITY();

            DECLARE @EntidadId NVARCHAR(50) = CAST(@EvaluacionId AS NVARCHAR(50));
            EXEC audit.ppEvento_Registrar @Usuario = @Username, @Accion = N'EvaluacionIniciada',
                 @Entidad = N'EvaluacionCIF', @EntidadId = @EntidadId;
        END
    COMMIT TRANSACTION;

    SELECT @EvaluacionId AS EvaluacionId;
END;
GO

-- Devuelve 3 conjuntos: cabecera, codificaciones y firmas por disciplina.
CREATE OR ALTER PROCEDURE core.ppEvaluacion_Obtener
    @EvaluacionId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ev.EvaluacionId, ev.ExpedienteId, e.NumeroExpediente, CONCAT(p.Nombres, N' ', p.Apellidos) AS NombreCompleto,
           e.CentroId, ev.FechaEvaluacion, ev.WhodasCognicion, ev.WhodasMovilidad, ev.WhodasAutocuidado,
           ev.WhodasRelaciones, ev.WhodasActividades, ev.WhodasParticipacion, ev.PuntajeWhodas,
           ev.GradoPropuesto, ev.TipoPropuesto, ev.VersionBaremo, ev.Estado, ev.Observaciones, ev.RowVersion,
           d.Resultado AS Dictamen
    FROM core.EvaluacionCIF ev
    INNER JOIN core.Expediente e ON e.ExpedienteId = ev.ExpedienteId
    INNER JOIN core.Persona p ON p.PersonaId = e.PersonaId
    LEFT JOIN core.Dictamen d ON d.EvaluacionId = ev.EvaluacionId
    WHERE ev.EvaluacionId = @EvaluacionId;

    SELECT c.Codigo, cc.Componente, cc.Descripcion, c.Disciplina, c.Calificador1, c.Calificador2,
           c.EsFacilitador, c.Observacion, u.NombreCompleto AS Evaluador
    FROM core.CodificacionCIF c
    INNER JOIN cat.CodigoCIF cc ON cc.Codigo = c.Codigo
    INNER JOIN seg.Usuario u ON u.UsuarioId = c.UsuarioId
    WHERE c.EvaluacionId = @EvaluacionId
    ORDER BY cc.Componente, c.Codigo;

    SELECT pe.Disciplina, u.NombreCompleto AS Evaluador, pe.FechaFirma
    FROM core.ParticipacionEvaluador pe
    INNER JOIN seg.Usuario u ON u.UsuarioId = pe.UsuarioId
    WHERE pe.EvaluacionId = @EvaluacionId;
END;
GO

CREATE OR ALTER PROCEDURE core.ppEvaluacion_GuardarCodificacion
    @EvaluacionId   INT,
    @UsuarioId      INT,
    @Codigo         VARCHAR(10),
    @Calificador1   TINYINT,
    @Calificador2   TINYINT = NULL,
    @EsFacilitador  BIT = 0,
    @Observacion    NVARCHAR(300) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Disciplina NVARCHAR(20), @Componente CHAR(1), @EstadoEval NVARCHAR(20), @Username NVARCHAR(50);

    SELECT @Disciplina = Disciplina, @Username = Username FROM seg.Usuario WHERE UsuarioId = @UsuarioId;
    SELECT @Componente = Componente FROM cat.CodigoCIF WHERE Codigo = LOWER(@Codigo) AND Activo = 1;
    SELECT @EstadoEval = Estado FROM core.EvaluacionCIF WHERE EvaluacionId = @EvaluacionId;

    IF @Disciplina IS NULL
        THROW 53010, N'Solo un evaluador con disciplina asignada puede codificar.', 1;
    IF @Componente IS NULL
        THROW 53011, N'El código CIF no existe en el catálogo. Use el buscador para ver sugerencias.', 1;
    IF @EstadoEval IS NULL
        THROW 53012, N'La evaluación no existe.', 1;
    IF @EstadoEval = N'Consolidada'
        THROW 53013, N'La evaluación ya fue consolidada y no admite cambios.', 1;
    IF @Calificador1 NOT IN (0, 1, 2, 3, 4, 8, 9) OR (@Calificador2 IS NOT NULL AND @Calificador2 NOT IN (0, 1, 2, 3, 4, 8, 9))
        THROW 53014, N'Calificador fuera de rango: use 0, 1, 2, 3, 4, 8 (sin especificar) o 9 (no aplicable).', 1;
    IF @EsFacilitador = 1 AND @Componente <> 'e'
        THROW 53015, N'Solo los factores ambientales (e) pueden marcarse como facilitador.', 1;
    IF EXISTS (SELECT 1 FROM core.ParticipacionEvaluador WHERE EvaluacionId = @EvaluacionId AND Disciplina = @Disciplina)
        THROW 53016, N'Su disciplina ya firmó esta evaluación; no puede modificar sus codificaciones.', 1;

    SET @Codigo = LOWER(@Codigo);
    -- Calificador 2 = capacidad: solo tiene sentido en Actividades y participación (d)
    IF @Componente <> 'd' SET @Calificador2 = NULL;

    IF EXISTS (SELECT 1 FROM core.CodificacionCIF WHERE EvaluacionId = @EvaluacionId AND Codigo = @Codigo AND Disciplina = @Disciplina)
        UPDATE core.CodificacionCIF
        SET Calificador1 = @Calificador1, Calificador2 = @Calificador2, EsFacilitador = @EsFacilitador,
            Observacion = @Observacion, UsuarioId = @UsuarioId
        WHERE EvaluacionId = @EvaluacionId AND Codigo = @Codigo AND Disciplina = @Disciplina;
    ELSE
        INSERT INTO core.CodificacionCIF (EvaluacionId, Codigo, Disciplina, Calificador1, Calificador2, EsFacilitador, Observacion, UsuarioId)
        VALUES (@EvaluacionId, @Codigo, @Disciplina, @Calificador1, @Calificador2, @EsFacilitador, @Observacion, @UsuarioId);

    -- Cualquier cambio invalida una firma "Completa" previa
    UPDATE core.EvaluacionCIF SET Estado = N'Borrador' WHERE EvaluacionId = @EvaluacionId AND Estado = N'Completa';

    DECLARE @EntidadId NVARCHAR(50) = CAST(@EvaluacionId AS NVARCHAR(50));
    DECLARE @Detalle NVARCHAR(2000) = CONCAT(@Codigo, N' (', @Disciplina, N')');
    EXEC audit.ppEvento_Registrar @Usuario = @Username, @Accion = N'CodificacionCifGuardada',
         @Entidad = N'EvaluacionCIF', @EntidadId = @EntidadId, @Detalle = @Detalle;
END;
GO

CREATE OR ALTER PROCEDURE core.ppEvaluacion_QuitarCodificacion
    @EvaluacionId  INT,
    @UsuarioId     INT,
    @Codigo        VARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Disciplina NVARCHAR(20) = (SELECT Disciplina FROM seg.Usuario WHERE UsuarioId = @UsuarioId);

    IF EXISTS (SELECT 1 FROM core.EvaluacionCIF WHERE EvaluacionId = @EvaluacionId AND Estado = N'Consolidada')
        THROW 53013, N'La evaluación ya fue consolidada y no admite cambios.', 1;
    IF EXISTS (SELECT 1 FROM core.ParticipacionEvaluador WHERE EvaluacionId = @EvaluacionId AND Disciplina = @Disciplina)
        THROW 53016, N'Su disciplina ya firmó esta evaluación; no puede modificar sus codificaciones.', 1;

    DELETE FROM core.CodificacionCIF
    WHERE EvaluacionId = @EvaluacionId AND Codigo = LOWER(@Codigo) AND Disciplina = @Disciplina;
END;
GO

-- WHODAS 2.0 por dominio, puntaje global y grado propuesto según el baremo vigente.
-- @RowVersion: control de concurrencia optimista (dos evaluadores editando a la vez).
CREATE OR ALTER PROCEDURE core.ppEvaluacion_RegistrarWhodas
    @EvaluacionId   INT,
    @RowVersion     BINARY(8),
    @Cognicion      DECIMAL(5,2),
    @Movilidad      DECIMAL(5,2),
    @Autocuidado    DECIMAL(5,2),
    @Relaciones     DECIMAL(5,2),
    @Actividades    DECIMAL(5,2),
    @Participacion  DECIMAL(5,2),
    @Observaciones  NVARCHAR(1000) = NULL,
    @Usuario        NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Version VARCHAR(20), @Global DECIMAL(5,2), @Grado NVARCHAR(20);

    SELECT @Version = Version FROM cat.Baremo WHERE Vigente = 1;
    IF @Version IS NULL
        THROW 53020, N'No hay un baremo vigente configurado. El cálculo automático no es posible; avise al administrador.', 1;

    -- Puntaje global: promedio de los 6 dominios (versión simplificada del algoritmo WHODAS 2.0)
    SET @Global = ROUND((@Cognicion + @Movilidad + @Autocuidado + @Relaciones + @Actividades + @Participacion) / 6.0, 2);

    SELECT @Grado = r.Grado
    FROM cat.BaremoRango r INNER JOIN cat.Baremo b ON b.BaremoId = r.BaremoId
    WHERE b.Vigente = 1 AND @Global BETWEEN r.PuntajeDesde AND r.PuntajeHasta;

    UPDATE core.EvaluacionCIF
    SET WhodasCognicion = @Cognicion, WhodasMovilidad = @Movilidad, WhodasAutocuidado = @Autocuidado,
        WhodasRelaciones = @Relaciones, WhodasActividades = @Actividades, WhodasParticipacion = @Participacion,
        PuntajeWhodas = @Global, GradoPropuesto = @Grado, VersionBaremo = @Version,
        Observaciones = ISNULL(@Observaciones, Observaciones)
    WHERE EvaluacionId = @EvaluacionId AND RowVersion = @RowVersion AND Estado <> N'Consolidada';

    IF @@ROWCOUNT = 0
    BEGIN
        IF EXISTS (SELECT 1 FROM core.EvaluacionCIF WHERE EvaluacionId = @EvaluacionId AND Estado = N'Consolidada')
            THROW 53013, N'La evaluación ya fue consolidada y no admite cambios.', 1;
        THROW 53021, N'Otro evaluador modificó esta evaluación mientras usted trabajaba. Recargue la página para ver sus cambios.', 1;
    END

    DECLARE @EntidadId NVARCHAR(50) = CAST(@EvaluacionId AS NVARCHAR(50));
    DECLARE @Detalle NVARCHAR(2000) = CONCAT(N'WHODAS global ', @Global, N', grado propuesto ', ISNULL(@Grado, N'(sin grado)'), N', baremo ', @Version);
    EXEC audit.ppEvento_Registrar @Usuario = @Usuario, @Accion = N'WhodasRegistrado',
         @Entidad = N'EvaluacionCIF', @EntidadId = @EntidadId, @Detalle = @Detalle;

    SELECT @Global AS PuntajeWhodas, @Grado AS GradoPropuesto, @Version AS VersionBaremo;
END;
GO

-- Firma de la disciplina del evaluador. Con las 3 disciplinas firmadas la evaluación queda Completa.
CREATE OR ALTER PROCEDURE core.ppEvaluacion_Firmar
    @EvaluacionId  INT,
    @UsuarioId     INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Disciplina NVARCHAR(20), @Username NVARCHAR(50);
    SELECT @Disciplina = Disciplina, @Username = Username FROM seg.Usuario WHERE UsuarioId = @UsuarioId;

    IF @Disciplina IS NULL THROW 53010, N'Solo un evaluador con disciplina asignada puede firmar.', 1;
    IF EXISTS (SELECT 1 FROM core.EvaluacionCIF WHERE EvaluacionId = @EvaluacionId AND Estado = N'Consolidada')
        THROW 53013, N'La evaluación ya fue consolidada y no admite cambios.', 1;
    IF NOT EXISTS (SELECT 1 FROM core.CodificacionCIF WHERE EvaluacionId = @EvaluacionId AND Disciplina = @Disciplina)
        THROW 53030, N'Registre al menos una codificación CIF de su disciplina antes de firmar.', 1;
    IF EXISTS (SELECT 1 FROM core.ParticipacionEvaluador WHERE EvaluacionId = @EvaluacionId AND Disciplina = @Disciplina)
        THROW 53031, N'Su disciplina ya firmó esta evaluación.', 1;

    BEGIN TRANSACTION;
        INSERT INTO core.ParticipacionEvaluador (EvaluacionId, Disciplina, UsuarioId)
        VALUES (@EvaluacionId, @Disciplina, @UsuarioId);

        IF (SELECT COUNT(*) FROM core.ParticipacionEvaluador WHERE EvaluacionId = @EvaluacionId) = 3
            UPDATE core.EvaluacionCIF SET Estado = N'Completa' WHERE EvaluacionId = @EvaluacionId;

        DECLARE @EntidadId NVARCHAR(50) = CAST(@EvaluacionId AS NVARCHAR(50));
        EXEC audit.ppEvento_Registrar @Usuario = @Username, @Accion = N'EvaluacionFirmada',
             @Entidad = N'EvaluacionCIF', @EntidadId = @EntidadId, @Detalle = @Disciplina;
    COMMIT TRANSACTION;
END;
GO

-- Consolidar: exige las 3 disciplinas, al menos un dominio "d" con desempeño y el WHODAS.
-- Si falta algo, el mensaje lista las secciones pendientes (RF-CORE-02).
CREATE OR ALTER PROCEDURE core.ppEvaluacion_Consolidar
    @EvaluacionId   INT,
    @TipoPropuesto  NVARCHAR(20),
    @Usuario        NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Faltantes NVARCHAR(400) = N'';
    DECLARE @Estado NVARCHAR(20), @ExpedienteId INT, @CitaId INT, @Whodas DECIMAL(5,2);

    SELECT @Estado = Estado, @ExpedienteId = ExpedienteId, @CitaId = CitaId, @Whodas = PuntajeWhodas
    FROM core.EvaluacionCIF WHERE EvaluacionId = @EvaluacionId;

    IF @Estado IS NULL THROW 53012, N'La evaluación no existe.', 1;
    IF @Estado = N'Consolidada' THROW 53013, N'La evaluación ya fue consolidada.', 1;

    IF NOT EXISTS (SELECT 1 FROM core.ParticipacionEvaluador WHERE EvaluacionId = @EvaluacionId AND Disciplina = N'Medica')
        SET @Faltantes += N' firma médica;';
    IF NOT EXISTS (SELECT 1 FROM core.ParticipacionEvaluador WHERE EvaluacionId = @EvaluacionId AND Disciplina = N'Psicologica')
        SET @Faltantes += N' firma psicológica;';
    IF NOT EXISTS (SELECT 1 FROM core.ParticipacionEvaluador WHERE EvaluacionId = @EvaluacionId AND Disciplina = N'Social')
        SET @Faltantes += N' firma de trabajo social;';
    IF NOT EXISTS (SELECT 1 FROM core.CodificacionCIF c INNER JOIN cat.CodigoCIF cc ON cc.Codigo = c.Codigo
                   WHERE c.EvaluacionId = @EvaluacionId AND cc.Componente = 'd' AND c.Calificador1 IN (0, 1, 2, 3, 4))
        SET @Faltantes += N' al menos un dominio de Actividades y participación (d) con calificador de desempeño;';
    IF @Whodas IS NULL
        SET @Faltantes += N' puntajes WHODAS 2.0;';
    IF @TipoPropuesto IS NULL OR @TipoPropuesto NOT IN (N'Fisica', N'Visual', N'Auditiva', N'Intelectual', N'Psicosocial', N'Multiple')
        SET @Faltantes += N' tipo de discapacidad propuesto;';

    IF LEN(@Faltantes) > 0
    BEGIN
        DECLARE @Msg NVARCHAR(500) = CONCAT(N'No se puede consolidar. Falta:', @Faltantes);
        THROW 53040, @Msg, 1;
    END

    BEGIN TRANSACTION;
        UPDATE core.EvaluacionCIF SET Estado = N'Consolidada', TipoPropuesto = @TipoPropuesto WHERE EvaluacionId = @EvaluacionId;
        IF @CitaId IS NOT NULL UPDATE core.Cita SET Estado = N'Realizada' WHERE CitaId = @CitaId;

        INSERT INTO core.HistorialEstado (ExpedienteId, EstadoAnterior, EstadoNuevo, Unidad, Usuario, Comentario)
        VALUES (@ExpedienteId, N'EnEvaluacion', N'EnEvaluacion', N'Valoración y Certificación', @Usuario,
                N'Valoración consolidada: lista para dictamen.');

        DECLARE @EntidadId NVARCHAR(50) = CAST(@EvaluacionId AS NVARCHAR(50));
        EXEC audit.ppEvento_Registrar @Usuario = @Usuario, @Accion = N'EvaluacionConsolidada',
             @Entidad = N'EvaluacionCIF', @EntidadId = @EntidadId;
    COMMIT TRANSACTION;
END;
GO

/* ----------------------------- DICTAMEN (RF-CORE-03) ----------------------- */
CREATE OR ALTER PROCEDURE core.ppDictamen_Pendientes
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ev.EvaluacionId, e.ExpedienteId, e.NumeroExpediente, CONCAT(p.Nombres, N' ', p.Apellidos) AS NombreCompleto,
           ev.PuntajeWhodas, ev.GradoPropuesto, ev.TipoPropuesto, ev.FechaEvaluacion,
           (SELECT COUNT(*) FROM core.VotoDictamen v WHERE v.EvaluacionId = ev.EvaluacionId) AS Votos
    FROM core.EvaluacionCIF ev
    INNER JOIN core.Expediente e ON e.ExpedienteId = ev.ExpedienteId
    INNER JOIN core.Persona p ON p.PersonaId = e.PersonaId
    WHERE ev.Estado = N'Consolidada'
      AND e.Estado = N'EnEvaluacion'
      AND NOT EXISTS (SELECT 1 FROM core.Dictamen d WHERE d.EvaluacionId = ev.EvaluacionId)
    ORDER BY ev.FechaEvaluacion;
END;
GO

CREATE OR ALTER PROCEDURE core.ppDictamen_Votos
    @EvaluacionId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT u.NombreCompleto AS Miembro, v.Voto, v.Grado, v.Tipo, v.Motivo, v.FechaVoto
    FROM core.VotoDictamen v INNER JOIN seg.Usuario u ON u.UsuarioId = v.UsuarioId
    WHERE v.EvaluacionId = @EvaluacionId
    ORDER BY v.FechaVoto;
END;
GO

-- Voto de un miembro del comité. Al alcanzar el quórum (cat.Parametro QuorumDictamen)
-- con votos coincidentes se emite el dictamen. El grado calculado es solo una propuesta (RINV-05).
CREATE OR ALTER PROCEDURE core.ppDictamen_Votar
    @EvaluacionId      INT,
    @UsuarioId         INT,
    @Voto              NVARCHAR(10),       -- Aprobar | Rechazar
    @Grado             NVARCHAR(20) = NULL,
    @Tipo              NVARCHAR(20) = NULL,
    @Motivo            NVARCHAR(500) = NULL,
    @Permanente        BIT = 0,
    @FechaVencimiento  DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Quorum INT = ISNULL(TRY_CAST((SELECT Valor FROM cat.Parametro WHERE Clave = 'QuorumDictamen') AS INT), 2);
    DECLARE @Vigencia INT = ISNULL(TRY_CAST((SELECT Valor FROM cat.Parametro WHERE Clave = 'VigenciaCarnetAnios') AS INT), 5);
    DECLARE @Plazo INT = ISNULL(TRY_CAST((SELECT Valor FROM cat.Parametro WHERE Clave = 'PlazoApelacionDias') AS INT), 30);
    DECLARE @EstadoEval NVARCHAR(20), @ExpedienteId INT, @GradoProp NVARCHAR(20), @TipoProp NVARCHAR(20);
    DECLARE @PersonaId INT, @Username NVARCHAR(50), @Coincidentes INT, @Resultado NVARCHAR(20);

    SELECT @EstadoEval = ev.Estado, @ExpedienteId = ev.ExpedienteId, @GradoProp = ev.GradoPropuesto,
           @TipoProp = ev.TipoPropuesto, @PersonaId = e.PersonaId
    FROM core.EvaluacionCIF ev INNER JOIN core.Expediente e ON e.ExpedienteId = ev.ExpedienteId
    WHERE ev.EvaluacionId = @EvaluacionId;
    SELECT @Username = Username FROM seg.Usuario WHERE UsuarioId = @UsuarioId;

    IF @EstadoEval IS NULL THROW 53012, N'La evaluación no existe.', 1;
    IF @EstadoEval <> N'Consolidada' THROW 53050, N'El comité solo dictamina evaluaciones consolidadas.', 1;
    IF EXISTS (SELECT 1 FROM core.Dictamen WHERE EvaluacionId = @EvaluacionId)
        THROW 53051, N'Esta evaluación ya tiene dictamen.', 1;
    IF @Voto NOT IN (N'Aprobar', N'Rechazar') THROW 53052, N'El voto debe ser Aprobar o Rechazar.', 1;
    IF @Voto = N'Rechazar' AND (@Motivo IS NULL OR LEN(@Motivo) < 10)
        THROW 53053, N'El rechazo exige un motivo fundamentado (mínimo 10 caracteres).', 1;

    IF @Voto = N'Aprobar'
    BEGIN
        SET @Grado = ISNULL(@Grado, @GradoProp);
        SET @Tipo = ISNULL(@Tipo, @TipoProp);
        IF @Grado IS NULL OR @Tipo IS NULL
            THROW 53054, N'Para aprobar indique el grado y el tipo de discapacidad.', 1;
        IF (@Grado <> ISNULL(@GradoProp, N'') OR @Tipo <> ISNULL(@TipoProp, N'')) AND (@Motivo IS NULL OR LEN(@Motivo) < 10)
            THROW 53055, N'Si ajusta el grado o el tipo propuestos debe justificarlo.', 1;
    END
    ELSE
        SELECT @Grado = NULL, @Tipo = NULL;

    BEGIN TRANSACTION;
        IF EXISTS (SELECT 1 FROM core.VotoDictamen WHERE EvaluacionId = @EvaluacionId AND UsuarioId = @UsuarioId)
            UPDATE core.VotoDictamen
            SET Voto = @Voto, Grado = @Grado, Tipo = @Tipo, Motivo = @Motivo, FechaVoto = SYSDATETIMEOFFSET()
            WHERE EvaluacionId = @EvaluacionId AND UsuarioId = @UsuarioId;
        ELSE
            INSERT INTO core.VotoDictamen (EvaluacionId, UsuarioId, Voto, Grado, Tipo, Motivo)
            VALUES (@EvaluacionId, @UsuarioId, @Voto, @Grado, @Tipo, @Motivo);

        SELECT @Coincidentes = COUNT(*) FROM core.VotoDictamen WITH (UPDLOCK, HOLDLOCK)
        WHERE EvaluacionId = @EvaluacionId AND Voto = @Voto
          AND ISNULL(Grado, N'') = ISNULL(@Grado, N'') AND ISNULL(Tipo, N'') = ISNULL(@Tipo, N'');

        DECLARE @EntidadId NVARCHAR(50) = CAST(@EvaluacionId AS NVARCHAR(50));
        EXEC audit.ppEvento_Registrar @Usuario = @Username, @Accion = N'VotoDictamen',
             @Entidad = N'EvaluacionCIF', @EntidadId = @EntidadId, @Detalle = @Voto;

        IF @Coincidentes >= @Quorum
        BEGIN
            DECLARE @Vence DATE = CASE WHEN @Voto = N'Aprobar' AND @Permanente = 0
                                       THEN ISNULL(@FechaVencimiento, DATEADD(YEAR, @Vigencia, CAST(SYSDATETIMEOFFSET() AS DATE))) END;
            SET @Resultado = CASE @Voto WHEN N'Aprobar' THEN N'Aprobado' ELSE N'Rechazado' END;

            INSERT INTO core.Dictamen (EvaluacionId, ExpedienteId, Resultado, Grado, Tipo, Motivo, FechaVencimientoCarnet)
            VALUES (@EvaluacionId, @ExpedienteId, @Resultado, @Grado, @Tipo, @Motivo, @Vence);

            IF @Resultado = N'Aprobado'
                UPDATE core.Expediente SET GradoDiscapacidad = @Grado, TipoDiscapacidad = @Tipo, FechaVencimiento = @Vence
                WHERE ExpedienteId = @ExpedienteId;

            EXEC core.ppExpediente_CambiarEstado @ExpedienteId = @ExpedienteId, @EstadoNuevo = @Resultado,
                 @Unidad = N'Comité de Certificación', @Usuario = @Username, @Comentario = N'Dictamen emitido por quórum.';

            DECLARE @Texto NVARCHAR(1000) = CASE @Resultado
                WHEN N'Aprobado' THEN N'Tu certificación fue aprobada. Te avisaremos cuando tu carnet digital esté listo.'
                ELSE CONCAT(N'Tu solicitud no fue aprobada. Puedes ver el motivo en el portal y apelar dentro de ', @Plazo, N' días.') END;
            EXEC core.ppNotificacion_Encolar @PersonaId = @PersonaId, @Asunto = N'Tu solicitud tiene un dictamen', @Mensaje = @Texto;

            -- Las solicitudes rechazadas se cierran; las aprobadas se cierran al emitir el carnet
            IF @Resultado = N'Rechazado'
                UPDATE core.Solicitud SET Estado = N'Cerrada' WHERE ExpedienteId = @ExpedienteId AND Estado = N'Activa' AND Tipo <> N'Duplicado';
        END
        ELSE
            SET @Resultado = N'PendienteQuorum';
    COMMIT TRANSACTION;

    SELECT @Resultado AS Resultado, @Coincidentes AS VotosCoincidentes, @Quorum AS Quorum;
END;
GO

/* ----------------------------- APELACIONES --------------------------------- */
CREATE OR ALTER PROCEDURE core.ppApelacion_Registrar
    @Numero  VARCHAR(20),
    @Cedula  CHAR(11),
    @Motivo  NVARCHAR(1000)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Plazo INT = ISNULL(TRY_CAST((SELECT Valor FROM cat.Parametro WHERE Clave = 'PlazoApelacionDias') AS INT), 30);
    DECLARE @ExpedienteId INT, @Estado NVARCHAR(20), @FechaRechazo DATETIMEOFFSET(0);
    DECLARE @Usuario NVARCHAR(100) = CONCAT(N'ciudadano:', @Cedula);

    IF @Motivo IS NULL OR LEN(@Motivo) < 20 THROW 53060, N'Explica el motivo de tu apelación (mínimo 20 caracteres).', 1;

    SELECT TOP (1) @ExpedienteId = e.ExpedienteId, @Estado = e.Estado, @FechaRechazo = e.FechaRechazo
    FROM core.Expediente e
    INNER JOIN core.Persona p ON p.PersonaId = e.PersonaId
    LEFT JOIN core.Solicitud s ON s.ExpedienteId = e.ExpedienteId
    WHERE (e.NumeroExpediente = @Numero OR s.NumeroTramite = @Numero)
      AND (p.Cedula = @Cedula OR s.CedulaCuenta = @Cedula
           OR EXISTS (SELECT 1 FROM core.Tutela t WHERE t.PersonaId = p.PersonaId AND t.CedulaTutor = @Cedula));

    IF @ExpedienteId IS NULL THROW 52035, N'No encontramos un trámite con esos datos.', 1;
    IF @Estado <> N'Rechazado' THROW 53061, N'Solo se puede apelar una solicitud rechazada.', 1;
    IF DATEDIFF(DAY, @FechaRechazo, SYSDATETIMEOFFSET()) > @Plazo
        THROW 53062, N'El plazo para apelar venció. El rechazo quedó firme; puedes presentar una nueva solicitud.', 1;

    BEGIN TRANSACTION;
        INSERT INTO core.Apelacion (ExpedienteId, Motivo) VALUES (@ExpedienteId, @Motivo);
        EXEC core.ppExpediente_CambiarEstado @ExpedienteId = @ExpedienteId, @EstadoNuevo = N'EnApelacion',
             @Unidad = N'Comité de Certificación', @Usuario = @Usuario, @Comentario = N'Apelación recibida.';
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE core.ppApelacion_Pendientes
AS
BEGIN
    SET NOCOUNT ON;
    SELECT a.ApelacionId, e.ExpedienteId, e.NumeroExpediente, CONCAT(p.Nombres, N' ', p.Apellidos) AS NombreCompleto,
           a.Motivo, a.FechaRegistro
    FROM core.Apelacion a
    INNER JOIN core.Expediente e ON e.ExpedienteId = a.ExpedienteId
    INNER JOIN core.Persona p ON p.PersonaId = e.PersonaId
    WHERE a.Estado = N'Pendiente'
    ORDER BY a.FechaRegistro;
END;
GO

CREATE OR ALTER PROCEDURE core.ppApelacion_Resolver
    @ApelacionId  INT,
    @Admitir      BIT,
    @Resolucion   NVARCHAR(1000),
    @UsuarioId    INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @ExpedienteId INT, @PersonaId INT, @Username NVARCHAR(50);

    IF @Resolucion IS NULL OR LEN(@Resolucion) < 10 THROW 53063, N'La resolución debe estar motivada.', 1;

    SELECT @ExpedienteId = a.ExpedienteId, @PersonaId = e.PersonaId
    FROM core.Apelacion a INNER JOIN core.Expediente e ON e.ExpedienteId = a.ExpedienteId
    WHERE a.ApelacionId = @ApelacionId AND a.Estado = N'Pendiente';
    SELECT @Username = Username FROM seg.Usuario WHERE UsuarioId = @UsuarioId;

    IF @ExpedienteId IS NULL THROW 53064, N'La apelación no existe o ya fue resuelta.', 1;

    BEGIN TRANSACTION;
        UPDATE core.Apelacion
        SET Estado = CASE WHEN @Admitir = 1 THEN N'Admitida' ELSE N'Denegada' END,
            Resolucion = @Resolucion, UsuarioResuelveId = @UsuarioId, FechaResolucion = SYSDATETIMEOFFSET()
        WHERE ApelacionId = @ApelacionId;

        DECLARE @Nuevo NVARCHAR(20) = CASE WHEN @Admitir = 1 THEN N'EnEvaluacion' ELSE N'Rechazado' END;
        EXEC core.ppExpediente_CambiarEstado @ExpedienteId = @ExpedienteId, @EstadoNuevo = @Nuevo,
             @Unidad = N'Comité de Certificación', @Usuario = @Username, @Comentario = @Resolucion;

        DECLARE @Texto NVARCHAR(1000) = CASE WHEN @Admitir = 1
            THEN N'Tu apelación fue admitida. Se realizará una nueva valoración.'
            ELSE N'Tu apelación fue denegada. Puedes ver la resolución en el portal.' END;
        EXEC core.ppNotificacion_Encolar @PersonaId = @PersonaId, @Asunto = N'Resolución de tu apelación', @Mensaje = @Texto;
    COMMIT TRANSACTION;
END;
GO

/* ----------------------------- CARNETS ------------------------------------- */
-- Emite el carnet ya firmado por el servicio de firma (Integración). Nunca se emite sin firma.
-- Primera emisión: Aprobado -> Carnetizado. Duplicado: exige la orden de cobro pagada.
CREATE OR ALTER PROCEDURE core.ppCarnet_Emitir
    @ExpedienteId        INT,
    @CodigoVerificacion  VARCHAR(64),
    @Firma               VARCHAR(600),
    @Usuario             NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Estado NVARCHAR(20), @PersonaId INT, @Vence DATE, @SolicitudDuplicado INT;
    DECLARE @NumeroCarnet VARCHAR(20), @NumeroCertificado VARCHAR(20);
    DECLARE @Hoy DATE = CAST(SYSDATETIMEOFFSET() AS DATE);

    IF @Firma IS NULL OR LEN(@Firma) < 20 THROW 54000, N'No se puede emitir un carnet sin firma digital.', 1;

    BEGIN TRANSACTION;
        SELECT @Estado = Estado, @PersonaId = PersonaId, @Vence = FechaVencimiento
        FROM core.Expediente WITH (UPDLOCK) WHERE ExpedienteId = @ExpedienteId;

        IF @Estado IS NULL THROW 52000, N'El expediente no existe.', 1;

        IF @Estado = N'Carnetizado'
        BEGIN
            SELECT TOP (1) @SolicitudDuplicado = s.SolicitudId
            FROM core.Solicitud s
            INNER JOIN core.OrdenCobro o ON o.Referencia = s.NumeroTramite
            WHERE s.ExpedienteId = @ExpedienteId AND s.Tipo = N'Duplicado' AND s.Estado = N'Activa' AND o.Estado = N'Pagada'
            ORDER BY s.SolicitudId;

            IF @SolicitudDuplicado IS NULL
                THROW 54001, N'Para emitir un duplicado debe existir una solicitud de duplicado con la tasa pagada.', 1;

            UPDATE core.Carnet
            SET Estado = N'Anulado', MotivoAnulacion = N'Reemplazado por duplicado', UsuarioAnula = @Usuario, FechaAnulacion = SYSDATETIMEOFFSET()
            WHERE ExpedienteId = @ExpedienteId AND Estado = N'Vigente';

            UPDATE core.Solicitud SET Estado = N'Cerrada' WHERE SolicitudId = @SolicitudDuplicado;
        END
        ELSE IF @Estado <> N'Aprobado'
            THROW 54002, N'Solo se emite carnet a expedientes aprobados por el comité.', 1;

        EXEC core.ppSecuencia_Siguiente @Prefijo = 'PCD', @Numero = @NumeroCarnet OUTPUT;
        EXEC core.ppSecuencia_Siguiente @Prefijo = 'CERT', @Numero = @NumeroCertificado OUTPUT;

        INSERT INTO core.Carnet (ExpedienteId, NumeroCarnet, NumeroCertificado, CodigoVerificacion, Firma,
                                 FechaEmision, FechaVencimiento, Estado, UsuarioEmite)
        VALUES (@ExpedienteId, @NumeroCarnet, @NumeroCertificado, @CodigoVerificacion, @Firma,
                @Hoy, @Vence, N'Vigente', @Usuario);

        IF @Estado = N'Aprobado'
        BEGIN
            EXEC core.ppExpediente_CambiarEstado @ExpedienteId = @ExpedienteId, @EstadoNuevo = N'Carnetizado',
                 @Unidad = N'Registro y Carnetización', @Usuario = @Usuario, @Comentario = N'Carnet digital emitido y firmado.';
            UPDATE core.Solicitud SET Estado = N'Cerrada'
            WHERE ExpedienteId = @ExpedienteId AND Estado = N'Activa' AND Tipo <> N'Duplicado';
        END
        ELSE
            INSERT INTO core.HistorialEstado (ExpedienteId, EstadoAnterior, EstadoNuevo, Unidad, Usuario, Comentario)
            VALUES (@ExpedienteId, @Estado, @Estado, N'Registro y Carnetización', @Usuario, N'Duplicado de carnet emitido.');

        EXEC core.ppNotificacion_Encolar @PersonaId = @PersonaId, @Asunto = N'Tu carnet digital está listo',
             @Mensaje = N'Ya puedes descargar tu carnet digital desde el Portal Ciudadano.';

        EXEC audit.ppEvento_Registrar @Usuario = @Usuario, @Accion = N'CarnetEmitido',
             @Entidad = N'Carnet', @EntidadId = @NumeroCarnet;
    COMMIT TRANSACTION;

    SELECT @NumeroCarnet AS NumeroCarnet, @NumeroCertificado AS NumeroCertificado, @Hoy AS FechaEmision, @Vence AS FechaVencimiento;
END;
GO

-- Anulación lógica con motivo y autorizador (RINV-04). Requiere rol SupervisorRegistro (se valida en C#).
CREATE OR ALTER PROCEDURE core.ppCarnet_Anular
    @CarnetId  INT,
    @Motivo    NVARCHAR(300),
    @Usuario   NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @ExpedienteId INT, @Numero VARCHAR(20);

    IF @Motivo IS NULL OR LEN(@Motivo) < 10 THROW 54010, N'La anulación exige un motivo (mínimo 10 caracteres).', 1;

    SELECT @ExpedienteId = ExpedienteId, @Numero = NumeroCarnet FROM core.Carnet WHERE CarnetId = @CarnetId AND Estado = N'Vigente';
    IF @ExpedienteId IS NULL THROW 54011, N'El carnet no existe o no está vigente.', 1;

    BEGIN TRANSACTION;
        UPDATE core.Carnet
        SET Estado = N'Anulado', MotivoAnulacion = @Motivo, UsuarioAnula = @Usuario, FechaAnulacion = SYSDATETIMEOFFSET()
        WHERE CarnetId = @CarnetId;

        EXEC core.ppExpediente_CambiarEstado @ExpedienteId = @ExpedienteId, @EstadoNuevo = N'Anulado',
             @Unidad = N'Registro y Carnetización', @Usuario = @Usuario, @Comentario = @Motivo;

        EXEC audit.ppEvento_Registrar @Usuario = @Usuario, @Accion = N'CarnetAnulado',
             @Entidad = N'Carnet', @EntidadId = @Numero, @Detalle = @Motivo;
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE core.ppCarnet_ObtenerVigentePorCedula
    @Cedula CHAR(11)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (1) c.NumeroCarnet, e.NumeroExpediente, p.Cedula, CONCAT(p.Nombres, N' ', p.Apellidos) AS NombreCompleto,
           e.TipoDiscapacidad, e.GradoDiscapacidad, c.FechaEmision, c.FechaVencimiento, c.Estado,
           c.CodigoVerificacion, c.NumeroCertificado
    FROM core.Carnet c
    INNER JOIN core.Expediente e ON e.ExpedienteId = c.ExpedienteId
    INNER JOIN core.Persona p ON p.PersonaId = e.PersonaId
    WHERE p.Cedula = @Cedula AND c.Estado IN (N'Vigente', N'Vencido')
    ORDER BY c.CarnetId DESC;
END;
GO

-- Verificación pública por QR: solo estado, nombre (se enmascara en C#) y fechas (RINV-02).
CREATE OR ALTER PROCEDURE core.ppCarnet_Verificar
    @CodigoVerificacion VARCHAR(64)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT c.Estado, p.Nombres, p.Apellidos, c.FechaEmision, c.FechaVencimiento, c.Firma, c.NumeroCarnet
    FROM core.Carnet c
    INNER JOIN core.Expediente e ON e.ExpedienteId = c.ExpedienteId
    INNER JOIN core.Persona p ON p.PersonaId = e.PersonaId
    WHERE c.CodigoVerificacion = @CodigoVerificacion;
END;
GO

-- Verificación institucional (RF-INT-03): atributos mínimos autorizados.
CREATE OR ALTER PROCEDURE core.ppCarnet_VerificarInstitucional
    @Cedula CHAR(11)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (1)
           CAST(CASE WHEN c.Estado = N'Vigente' THEN 1 ELSE 0 END AS BIT) AS TieneCertificacionVigente,
           e.TipoDiscapacidad, e.GradoDiscapacidad, c.FechaVencimiento
    FROM core.Persona p
    INNER JOIN core.Expediente e ON e.PersonaId = p.PersonaId
    LEFT JOIN core.Carnet c ON c.ExpedienteId = e.ExpedienteId AND c.Estado = N'Vigente'
    WHERE p.Cedula = @Cedula;
END;
GO

-- Proceso diario: carnets vencidos (Carnetizado -> Vencido). Devuelve cuántos venció.
CREATE OR ALTER PROCEDURE core.ppProceso_MarcarVencidos
    @Usuario NVARCHAR(100) = N'proceso-diario'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Hoy DATE = CAST(SYSDATETIMEOFFSET() AS DATE);
    DECLARE @Vencidos TABLE (ExpedienteId INT PRIMARY KEY, PersonaId INT);
    DECLARE @Id INT, @PersonaId INT, @Cantidad INT;

    BEGIN TRANSACTION;
        UPDATE c SET Estado = N'Vencido'
        OUTPUT inserted.ExpedienteId, e.PersonaId INTO @Vencidos (ExpedienteId, PersonaId)
        FROM core.Carnet c INNER JOIN core.Expediente e ON e.ExpedienteId = c.ExpedienteId
        WHERE c.Estado = N'Vigente' AND c.FechaVencimiento IS NOT NULL AND c.FechaVencimiento < @Hoy;

        SET @Cantidad = @@ROWCOUNT;

        WHILE EXISTS (SELECT 1 FROM @Vencidos)
        BEGIN
            SELECT TOP (1) @Id = ExpedienteId, @PersonaId = PersonaId FROM @Vencidos;

            IF EXISTS (SELECT 1 FROM core.Expediente WHERE ExpedienteId = @Id AND Estado = N'Carnetizado')
                EXEC core.ppExpediente_CambiarEstado @ExpedienteId = @Id, @EstadoNuevo = N'Vencido',
                     @Unidad = N'Sistema', @Usuario = @Usuario, @Comentario = N'Vencimiento del carnet.';

            EXEC core.ppNotificacion_Encolar @PersonaId = @PersonaId, @Asunto = N'Tu carnet venció',
                 @Mensaje = N'Tu carnet llegó a su fecha de vencimiento. Puedes solicitar la renovación desde el portal.';

            DELETE FROM @Vencidos WHERE ExpedienteId = @Id;
        END
    COMMIT TRANSACTION;

    SELECT @Cantidad AS CarnetsVencidos;
END;
GO

PRINT N'06 - Procedimientos de valoración, dictamen y carnets creados.';
GO