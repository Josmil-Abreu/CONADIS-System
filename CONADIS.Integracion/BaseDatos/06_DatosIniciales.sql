/* =============================================================================
   CONADIS.Integracion - 06: Datos iniciales
   Se puede ejecutar varias veces: solo agrega lo que falta.

   API keys de DEMOSTRACIÓN (en la tabla solo se guarda su SHA-256):
     Portal Web                 conadis-web-demo-2026        ámbito: portal
     Módulo de Caja             conadis-caja-demo-2026       ámbito: caja
     Core (pide firmas)         conadis-core-demo-2026       ámbito: firma
     Institución con convenio   institucion-demo-2026        ámbito: discapacidad.verificar (ve tipo y grado)
     Institución vencida        institucion-vencida-2026     convenio vencido -> debe recibir 403
   Operadores de la consola (contraseña de ambos: Conadis2026!):
     admin.integracion (Administrador)   monitor (Monitor)
   Cambie claves y contraseñas antes de cualquier uso real.
   ============================================================================= */
USE CONADIS_IntegracionDB;
GO

/* ----------------------------- PARÁMETROS ---------------------------------- */
MERGE cfg.Parametro AS destino
USING (VALUES
    ('JceTiempoEsperaSegundos',       N'5',    N'Tiempo máximo de espera de la JCE (RIC-06).'),
    ('MspTiempoEsperaSegundos',       N'5',    N'Tiempo máximo de espera del MSP/SNS (RIC-06).'),
    ('DgiiTiempoEsperaSegundos',      N'15',   N'Tiempo máximo de espera de la DGII (RIC-06).'),
    ('FirmaTiempoEsperaSegundos',     N'10',   N'Tiempo máximo de espera del proveedor de firma digital.'),
    ('PasarelaTiempoEsperaSegundos',  N'15',   N'Tiempo máximo de espera de la pasarela de pagos.'),
    ('CoreTiempoEsperaSegundos',      N'10',   N'Tiempo máximo de espera de la API del Core.'),
    ('ReintentosInmediatosMaximos',   N'3',    N'Reintentos inmediatos con retroceso exponencial antes de encolar (RIC-06).'),
    ('CircuitoFallosParaAbrir',       N'5',    N'Fallos consecutivos que abren el cortacircuito (RF-INT-01).'),
    ('CircuitoSegundosAbierto',       N'60',   N'Segundos que el cortacircuito permanece abierto (RF-INT-01).'),
    ('CacheDiasVigencia',             N'30',   N'Días de vigencia de una validación en caché (RF-INT-01).'),
    ('OutboxIntentosMaximos',         N'8',    N'Intentos de entrega de un mensaje antes de marcarlo Fallido.'),
    ('OutboxSegundosBase',            N'30',   N'Espera tras el primer fallo; se duplica en cada intento.'),
    ('OutboxSegundosMaximo',          N'3600', N'Espera máxima entre reintentos (1 hora).'),
    ('OutboxSegundosMaximoTransitorio', N'30', N'Espera máxima entre reintentos cuando el destino está caído (fallo transitorio).'),
    ('IdempotenciaHorasVigencia',     N'24',   N'Horas que se recuerda una Idempotency-Key (RIC-05).'),
    ('LoginIntentosMaximos',          N'5',    N'Intentos fallidos antes de bloquear a un operador.'),
    ('LoginMinutosBloqueo',           N'15',   N'Minutos de bloqueo de un operador.'),
    ('FirmaCertificadoVence',         N'2027-06-30', N'Vencimiento del certificado de firma; se alerta 30 días antes (RF-INT-03).'),
    ('FirmaDiasAvisoVencimiento',     N'30',   N'Días de antelación para alertar el vencimiento del certificado.')
) AS origen (Clave, Valor, Descripcion)
ON destino.Clave = origen.Clave
WHEN NOT MATCHED THEN INSERT (Clave, Valor, Descripcion) VALUES (origen.Clave, origen.Valor, origen.Descripcion);
GO

/* ----------------------------- SISTEMAS CLIENTE ---------------------------- */
MERGE seg.ClienteApi AS destino
USING (VALUES
    (N'Web',                       N'Canal',       '95BC571912335BFD25C94307B9C7722C1FD059068E370D6FA46D7F7B616A06DF', N'portal',                 0, NULL),
    (N'Caja',                      N'Modulo',      'DB0792677B703F086BD57C721D547DC83D36C547481B679F49ECACBC110839D6', N'caja',                   0, NULL),
    (N'Core',                      N'Modulo',      '85D2CCB906AA9CE36C7CEFD4BCD0A91C2ACAF86E10BCC18DFD2AE128EC212C54', N'firma',                  0, NULL),
    (N'Institución de Prueba',     N'Institucion', '3E7E176D49A9738651DDA949B5A94CEEA7CDBA968B5FFC700195D53521165944', N'discapacidad.verificar', 1, CAST('2027-12-31' AS DATE)),
    (N'Institución Convenio Vencido', N'Institucion', '9D248663D0CAB29A07753CDAB99D2A629D92E415131A596D9FD5C5BEA9A3C3FF', N'discapacidad.verificar', 0, CAST('2025-12-31' AS DATE))
) AS origen (Nombre, Tipo, HashClave, Ambitos, VerTipoGrado, ConvenioHasta)
ON destino.Nombre = origen.Nombre
WHEN NOT MATCHED THEN INSERT (Nombre, Tipo, HashClave, Ambitos, VerTipoGrado, ConvenioHasta)
                      VALUES (origen.Nombre, origen.Tipo, origen.HashClave, origen.Ambitos, origen.VerTipoGrado, origen.ConvenioHasta);
GO

/* ----------------------------- OPERADORES ---------------------------------- */
IF NOT EXISTS (SELECT 1 FROM seg.Operador WHERE Username = N'admin.integracion')
    INSERT INTO seg.Operador (Username, PasswordHash, NombreCompleto, Rol)
    VALUES (N'admin.integracion', 'PBKDF2-SHA256$600000$aerm/xsBmOkLbMBzWHp6OA==$Xq5StB1nXMAs/4Qq4xj9hx4y1xLYbzWOXgrTfQ2k5w8=',
            N'Administrador de Integración', N'Administrador');
IF NOT EXISTS (SELECT 1 FROM seg.Operador WHERE Username = N'monitor')
    INSERT INTO seg.Operador (Username, PasswordHash, NombreCompleto, Rol)
    VALUES (N'monitor', 'PBKDF2-SHA256$600000$9MlP9iwCp59hDFCC1K7FUQ==$2W5sSGiOzFTPPl/2KTrB+ZkJ0M6HJ42aH59uIFFy4Wo=',
            N'Monitor de Integración', N'Monitor');
GO

/* ----------------------------- CORTACIRCUITOS ------------------------------ */
MERGE integ.Circuito AS destino
USING (VALUES ('JCE'), ('MSP'), ('DGII'), ('Firma'), ('Pasarela'), ('Core'), ('Correo'), ('SMS')) AS origen (Servicio)
ON destino.Servicio = origen.Servicio
WHEN NOT MATCHED THEN INSERT (Servicio) VALUES (origen.Servicio);
GO

PRINT N'06 - Datos iniciales cargados.';
GO
