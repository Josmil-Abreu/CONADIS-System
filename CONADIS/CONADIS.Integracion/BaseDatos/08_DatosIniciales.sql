/* =============================================================================
   CONADIS.Integracion - 08: Datos iniciales
   Se puede ejecutar varias veces: inserta lo que falta y no pisa lo que se
   haya cambiado a mano (salvo las API keys de demostración, ver abajo).
   ============================================================================= */
USE CONADIS_IntegracionDB;
GO

-- Necesarios para índices filtrados y JSON (Visual Studio ya los trae activos; sqlcmd no)
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

SET NOCOUNT ON;

/* ------------------------------- PARÁMETROS -------------------------------- */
MERGE cat.Parametro AS destino
USING (VALUES
    (N'Cola.MaxIntentos',             N'8',    N'Intentos antes de marcar un mensaje como Muerto.'),
    (N'Cola.EsperaBaseSegundos',      N'30',   N'Espera del primer reintento; se duplica en cada intento.'),
    (N'Cola.EsperaMaximaSegundos',    N'3600', N'Tope de espera entre reintentos.'),
    (N'Cola.TamanoLote',              N'20',   N'Mensajes que toma el Servicio de Windows por vuelta.'),
    (N'Cola.SegundosBloqueo',         N'120',  N'Tiempo que un trabajador reserva un mensaje en proceso.'),
    (N'Cola.SegundosIntervalo',       N'10',   N'Pausa del Servicio de Windows entre vueltas.'),
    (N'Cola.DiasRetencion',           N'30',   N'Días que se conservan los mensajes entregados.'),
    (N'Idempotencia.HorasVigencia',   N'24',   N'Horas que se recuerda una clave de idempotencia.'),
    (N'Cache.SegundosVigencia',       N'900',  N'Vigencia por defecto de una entrada de caché.'),
    (N'Cache.DiasRetencion',          N'7',    N'Días que se conserva una entrada vencida antes de borrarla.')
) AS origen (Clave, Valor, Descripcion)
ON destino.Clave = origen.Clave
WHEN NOT MATCHED THEN
    INSERT (Clave, Valor, Descripcion) VALUES (origen.Clave, origen.Valor, origen.Descripcion)
WHEN MATCHED THEN
    UPDATE SET Descripcion = origen.Descripcion;   -- el valor no se toca: puede haberse ajustado
GO

/* -------------------------------- SERVICIOS -------------------------------- */
-- Todos empiezan simulados. El CORE todavía no tiene API: mientras tanto los
-- mensajes para él se acumulan en la cola y el simulador los confirma.
MERGE snd.Servicio AS destino
USING (VALUES
    (N'CORE',     N'CONADIS Core (expedientes, pagos, carnets)', 5, 60),
    (N'JCE',      N'Junta Central Electoral (cédulas)',          5, 60),
    (N'MSP',      N'Ministerio de Salud Pública (diagnósticos)', 5, 60),
    (N'DGII',     N'DGII (comprobantes fiscales e-CF y RNC)',    5, 120),
    (N'FIRMA',    N'Firma digital',                              5, 60),
    (N'PASARELA', N'Pasarela de pagos en línea',                 3, 120)
) AS origen (Codigo, Nombre, Umbral, Apertura)
ON destino.Codigo = origen.Codigo
WHEN NOT MATCHED THEN
    INSERT (Codigo, Nombre, EsSimulado, UmbralFallos, SegundosApertura)
    VALUES (origen.Codigo, origen.Nombre, 1, origen.Umbral, origen.Apertura)
WHEN MATCHED THEN
    UPDATE SET Nombre = origen.Nombre;
GO

/* ------------------------- CLIENTES (API KEYS DEMO) ------------------------ */
-- SOLO PARA DESARROLLO Y DEMOSTRACIÓN. Las claves en claro son:
--     WEB  -> demo-web-conadis-2026
--     CAJA -> demo-caja-conadis-2026
-- Son las que deben ir en el appsettings.Development.json de la Web y de la Caja.
-- En producción se generan claves nuevas y se registran con seg.ppCliente_Registrar.
-- El hash se calcula igual que en C#: SHA-256 en hexadecimal mayúscula.
DECLARE @HashWeb  CHAR(64) = CONVERT(CHAR(64), HASHBYTES('SHA2_256', CAST('demo-web-conadis-2026'  AS VARCHAR(100))), 2);
DECLARE @HashCaja CHAR(64) = CONVERT(CHAR(64), HASHBYTES('SHA2_256', CAST('demo-caja-conadis-2026' AS VARCHAR(100))), 2);

EXEC seg.ppCliente_Registrar @Codigo = N'WEB',  @Nombre = N'Portal Ciudadano',  @ApiKeyHash = @HashWeb;
EXEC seg.ppCliente_Registrar @Codigo = N'CAJA', @Nombre = N'Módulo de Caja',    @ApiKeyHash = @HashCaja;
GO

PRINT N'08 - Datos iniciales cargados (parámetros, servicios y clientes demo).';
GO
