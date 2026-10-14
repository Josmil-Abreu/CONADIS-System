/* =============================================================================
   CONADIS.Core - 99: Prueba del flujo completo (CU-01) SIN dejar datos
   Recorre: solicitud -> cita -> valoración CIF de 3 disciplinas -> WHODAS ->
   consolidación -> dictamen por quórum -> carnet -> verificación QR -> auditoría.
   Todo ocurre dentro de una transacción que al final se revierte (ROLLBACK).
   Si algún paso falla, SQL Server muestra el mensaje del procedimiento.
   ============================================================================= */
USE CONADIS_CoreDB;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

DECLARE @Centro INT = (SELECT CentroId FROM cat.CentroValoracion WHERE Nombre = N'Centro de Valoración Sede Central');
DECLARE @Medico INT = (SELECT UsuarioId FROM seg.Usuario WHERE Username = N'medico');
DECLARE @Psico  INT = (SELECT UsuarioId FROM seg.Usuario WHERE Username = N'psicologo');
DECLARE @Social INT = (SELECT UsuarioId FROM seg.Usuario WHERE Username = N'social');
DECLARE @C1 INT = (SELECT UsuarioId FROM seg.Usuario WHERE Username = N'comite1');
DECLARE @C2 INT = (SELECT UsuarioId FROM seg.Usuario WHERE Username = N'comite2');

PRINT N'1) Registrar solicitud presencial';
EXEC core.ppSolicitud_Registrar
     @Canal = N'Presencial', @UsuarioRegistro = N'analista', @Tipo = N'Certificacion',
     @Cedula = '00199999991', @Nombres = N'Juan', @Apellidos = N'Prueba Pérez',
     @FechaNacimiento = '1990-05-15', @Sexo = 1, @EstadoIdentidad = N'Validada',
     @Correo = N'juan.prueba@correo.do', @Provincia = N'Distrito Nacional',
     @CentroPreferidoId = @Centro, @ConsentimientoDatos = 1;

DECLARE @Exp INT = (SELECT e.ExpedienteId FROM core.Expediente e
                    INNER JOIN core.Persona p ON p.PersonaId = e.PersonaId WHERE p.Cedula = '00199999991');

PRINT N'2) Asignar cita (Solicitado -> EnEvaluacion)';
DECLARE @Fecha DATETIMEOFFSET(0) = DATEADD(DAY, 3, SYSDATETIMEOFFSET());
EXEC core.ppCita_Asignar @ExpedienteId = @Exp, @CentroId = @Centro, @FechaHora = @Fecha, @Usuario = N'analista';

PRINT N'3) Iniciar valoración y codificar por disciplina';
EXEC core.ppEvaluacion_Iniciar @ExpedienteId = @Exp, @UsuarioId = @Medico;
DECLARE @Ev INT = (SELECT MAX(EvaluacionId) FROM core.EvaluacionCIF WHERE ExpedienteId = @Exp);

EXEC core.ppEvaluacion_GuardarCodificacion @EvaluacionId = @Ev, @UsuarioId = @Medico, @Codigo = 'b730', @Calificador1 = 3;
EXEC core.ppEvaluacion_GuardarCodificacion @EvaluacionId = @Ev, @UsuarioId = @Psico,  @Codigo = 'b152', @Calificador1 = 2;
EXEC core.ppEvaluacion_GuardarCodificacion @EvaluacionId = @Ev, @UsuarioId = @Social, @Codigo = 'd450', @Calificador1 = 3, @Calificador2 = 2;
EXEC core.ppEvaluacion_GuardarCodificacion @EvaluacionId = @Ev, @UsuarioId = @Social, @Codigo = 'e120', @Calificador1 = 3, @EsFacilitador = 1;

PRINT N'4) WHODAS 2.0 (promedio 47.5 => grado propuesto Moderado)';
DECLARE @Rv BINARY(8) = (SELECT RowVersion FROM core.EvaluacionCIF WHERE EvaluacionId = @Ev);
EXEC core.ppEvaluacion_RegistrarWhodas @EvaluacionId = @Ev, @RowVersion = @Rv,
     @Cognicion = 60, @Movilidad = 55, @Autocuidado = 50, @Relaciones = 45, @Actividades = 40, @Participacion = 35,
     @Usuario = N'medico';

PRINT N'5) Firmas de las 3 disciplinas y consolidación';
EXEC core.ppEvaluacion_Firmar @EvaluacionId = @Ev, @UsuarioId = @Medico;
EXEC core.ppEvaluacion_Firmar @EvaluacionId = @Ev, @UsuarioId = @Psico;
EXEC core.ppEvaluacion_Firmar @EvaluacionId = @Ev, @UsuarioId = @Social;
EXEC core.ppEvaluacion_Consolidar @EvaluacionId = @Ev, @TipoPropuesto = N'Fisica', @Usuario = N'medico';

PRINT N'6) Dictamen: el primer voto queda pendiente de quórum, el segundo lo emite';
EXEC core.ppDictamen_Votar @EvaluacionId = @Ev, @UsuarioId = @C1, @Voto = N'Aprobar';
EXEC core.ppDictamen_Votar @EvaluacionId = @Ev, @UsuarioId = @C2, @Voto = N'Aprobar';

PRINT N'7) Emitir carnet (con una firma de ejemplo)';
EXEC core.ppCarnet_Emitir @ExpedienteId = @Exp, @CodigoVerificacion = 'PRUEBA-CODIGO-OPACO-0001',
     @Firma = 'firma-de-prueba-generada-por-integracion', @Usuario = N'analista';

PRINT N'8) Consultas del ciudadano y verificación pública';
DECLARE @Tramite VARCHAR(20) = (SELECT TOP (1) NumeroTramite FROM core.Solicitud WHERE ExpedienteId = @Exp);
EXEC core.ppTramite_ObtenerCiudadano @Numero = @Tramite, @Cedula = '00199999991';
EXEC core.ppCarnet_Verificar @CodigoVerificacion = 'PRUEBA-CODIGO-OPACO-0001';

PRINT N'9) Integridad de la bitácora (debe salir vacío)';
EXEC audit.ppEvento_VerificarCadena;

SELECT Estado AS EstadoFinalDelExpediente FROM core.Expediente WHERE ExpedienteId = @Exp;

ROLLBACK TRANSACTION;
PRINT N'Prueba terminada. Todo se revirtió: la base quedó igual que antes.';
GO