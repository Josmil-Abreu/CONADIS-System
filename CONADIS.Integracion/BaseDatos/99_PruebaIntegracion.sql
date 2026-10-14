/* =============================================================================
   CONADIS.Integracion - 99: Prueba de los procedimientos SIN dejar datos
   Recorre: clientes y convenios -> cola de mensajes (reintentos y fallo definitivo)
   -> pago de la Caja con outbox transaccional -> idempotencia -> cortacircuito
   -> caché -> bitácora y estadísticas.
   Todo ocurre dentro de una transacción que al final se revierte (ROLLBACK).
   ============================================================================= */
USE CONADIS_IntegracionDB;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

DECLARE @Caja INT = (SELECT ClienteId FROM seg.ClienteApi WHERE Nombre = N'Caja');
DECLARE @Web  INT = (SELECT ClienteId FROM seg.ClienteApi WHERE Nombre = N'Web');

PRINT N'1) Clientes: el convenio vigente responde ConvenioVigente = 1; el vencido, 0';
EXEC seg.ppCliente_ObtenerPorHash @HashClave = '3E7E176D49A9738651DDA949B5A94CEEA7CDBA968B5FFC700195D53521165944';
EXEC seg.ppCliente_ObtenerPorHash @HashClave = '9D248663D0CAB29A07753CDAB99D2A629D92E415131A596D9FD5C5BEA9A3C3FF';

PRINT N'2) Cola: encolar dos veces con la misma clave (Encolado = 1 y luego 0)';
EXEC integ.ppMensaje_Encolar @Tipo = 'Jce.Revalidar', @Destino = 'JCE', @Carga = N'{"prueba":1}', @ClaveUnica = 'PRUEBA-1';
EXEC integ.ppMensaje_Encolar @Tipo = 'Jce.Revalidar', @Destino = 'JCE', @Carga = N'{"prueba":1}', @ClaveUnica = 'PRUEBA-1';
DECLARE @Msg BIGINT = (SELECT MensajeId FROM integ.Mensaje WHERE ClaveUnica = 'PRUEBA-1');

PRINT N'3) Tomar el mensaje y fallar: vuelve a Pendiente con espera de 30 s';
EXEC integ.ppMensaje_Tomar @Trabajador = N'prueba', @Destino = 'JCE';
EXEC integ.ppMensaje_RegistrarFallo @MensajeId = @Msg, @Error = N'JCE no respondió (simulado).';

PRINT N'4) Agotar los intentos: pasa a Fallido y genera una alerta Alta';
UPDATE integ.Mensaje SET Estado = 'EnProceso', Intentos = MaxIntentos WHERE MensajeId = @Msg;   -- atajo solo para la prueba
EXEC integ.ppMensaje_RegistrarFallo @MensajeId = @Msg, @Error = N'JCE no respondió (simulado).';
EXEC integ.ppAlerta_ListarPendientes;

PRINT N'5) Reintento manual desde la consola: vuelve a Pendiente';
EXEC integ.ppMensaje_Reintentar @MensajeId = @Msg, @Usuario = N'admin.integracion';
EXEC integ.ppMensaje_Obtener @MensajeId = @Msg;

PRINT N'6) Pago de la Caja: el segundo envío idéntico devuelve EsNuevo = 0 (no se duplica)';
DECLARE @Tx UNIQUEIDENTIFIER = NEWID();
DECLARE @Hash CHAR(64) = REPLICATE('A', 64);
EXEC integ.ppPago_Recibir @TransaccionId = @Tx, @ClienteId = @Caja, @Origen = 'CAJA', @NumeroOrden = 'OC-2026-0000001',
     @NumeroComprobante = 'E320000000001', @Total = 600, @HashCarga = @Hash, @Carga = N'{"prueba":"pago"}';
EXEC integ.ppPago_Recibir @TransaccionId = @Tx, @ClienteId = @Caja, @Origen = 'CAJA', @NumeroOrden = 'OC-2026-0000001',
     @NumeroComprobante = 'E320000000001', @Total = 600, @HashCarga = @Hash, @Carga = N'{"prueba":"pago"}';

PRINT N'7) El pago dejó su mensaje al Core en la cola, y la Web no puede ver un pago de la Caja';
SELECT m.MensajeId, m.Tipo, m.Destino, m.Estado, m.ClaveUnica
FROM integ.PagoRecibido p INNER JOIN integ.Mensaje m ON m.MensajeId = p.MensajeId
WHERE p.TransaccionId = @Tx;
EXEC integ.ppPago_Obtener @TransaccionId = @Tx, @ClienteId = @Web;    -- vacío

PRINT N'8) El Core confirma el pago: Aplicado';
EXEC integ.ppPago_RegistrarRespuestaCore @TransaccionId = @Tx, @Estado = 'Aplicado';
EXEC integ.ppPago_Obtener @TransaccionId = @Tx, @ClienteId = @Caja;

PRINT N'9) Idempotencia: Nueva -> EnProceso -> (completar) -> Repetida -> Conflicto';
DECLARE @HashPeticion CHAR(64) = REPLICATE('C', 64);
EXEC integ.ppIdempotencia_Iniciar @ClienteId = @Web, @Clave = 'clave-prueba', @Ruta = N'/api/v1/solicitudes', @HashPeticion = @HashPeticion;
EXEC integ.ppIdempotencia_Iniciar @ClienteId = @Web, @Clave = 'clave-prueba', @Ruta = N'/api/v1/solicitudes', @HashPeticion = @HashPeticion;
EXEC integ.ppIdempotencia_Completar @ClienteId = @Web, @Clave = 'clave-prueba', @CodigoHttp = 201, @Respuesta = N'{"numeroTramite":"TRA-2026-0000001"}';
EXEC integ.ppIdempotencia_Iniciar @ClienteId = @Web, @Clave = 'clave-prueba', @Ruta = N'/api/v1/solicitudes', @HashPeticion = @HashPeticion;
DECLARE @OtroHash CHAR(64) = REPLICATE('D', 64);
EXEC integ.ppIdempotencia_Iniciar @ClienteId = @Web, @Clave = 'clave-prueba', @Ruta = N'/api/v1/solicitudes', @HashPeticion = @OtroHash;

PRINT N'10) Cortacircuito de la JCE: 5 fallos lo abren (y alertan); luego no deja llamar';
DECLARE @i INT = 1;
WHILE @i <= 5
BEGIN
    EXEC integ.ppCircuito_RegistrarResultado @Servicio = 'JCE', @Exito = 0, @Error = N'Tiempo de espera agotado (simulado).';
    SET @i += 1;
END
EXEC integ.ppCircuito_PuedeLlamar @Servicio = 'JCE';        -- Permitido = 0

PRINT N'11) Pasados los 60 s: una llamada de prueba (SemiAbierto); si funciona, se cierra';
UPDATE integ.Circuito SET AbiertoHasta = DATEADD(SECOND, -1, SYSDATETIMEOFFSET()) WHERE Servicio = 'JCE';  -- atajo de la prueba
EXEC integ.ppCircuito_PuedeLlamar @Servicio = 'JCE';        -- Permitido = 1, SemiAbierto
EXEC integ.ppCircuito_RegistrarResultado @Servicio = 'JCE', @Exito = 1;   -- Cerrado

PRINT N'12) Caché: guardar y leer una validación';
DECLARE @ClaveCache CHAR(64) = REPLICATE('B', 64);
EXEC integ.ppCache_Guardar @Servicio = 'JCE', @Clave = @ClaveCache, @Resultado = 'Validada', @Datos = N'{"nombres":"Prueba"}';
EXEC integ.ppCache_Obtener @Servicio = 'JCE', @Clave = @ClaveCache;

PRINT N'13) Bitácora y estadísticas por servicio';
EXEC integ.ppBitacora_Registrar @Servicio = 'JCE', @Operacion = N'ValidarCedula', @Direccion = 'Saliente',
     @Exito = 1, @Resultado = 'Valida', @Cliente = N'Web', @Proposito = N'Registro de solicitud', @LatenciaMs = 120;
EXEC integ.ppBitacora_Registrar @Servicio = 'JCE', @Operacion = N'ValidarCedula', @Direccion = 'Saliente',
     @Exito = 1, @Resultado = 'Valida', @Cliente = N'Web', @Proposito = N'Registro de solicitud', @DesdeCache = 1;
DECLARE @Desde DATETIMEOFFSET(0) = DATEADD(HOUR, -1, SYSDATETIMEOFFSET());
DECLARE @Hasta DATETIMEOFFSET(0) = DATEADD(HOUR, 1, SYSDATETIMEOFFSET());
EXEC integ.ppBitacora_Estadisticas @Desde = @Desde, @Hasta = @Hasta;

PRINT N'14) Resumen de la cola';
EXEC integ.ppMensaje_Resumen;

ROLLBACK TRANSACTION;
PRINT N'Prueba terminada. Todo se revirtió: la base quedó igual que antes.';
GO
