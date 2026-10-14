/* =============================================================================
   CONADIS.Integracion - 05: Interoperabilidad
   - Bitácora de cada intercambio (RF-INT-01 paso 5, RF-INT-03 paso 3) y sus estadísticas.
   - Caché de validaciones de la JCE y el MSP (RF-INT-01 paso 1, vigencia 30 días).
   - Cortacircuitos por servicio externo (RIC-06): 5 fallos seguidos -> abierto 60 s.
   ============================================================================= */
USE CONADIS_IntegracionDB;
GO

/* ----------------------------- BITÁCORA ------------------------------------ */
CREATE OR ALTER PROCEDURE integ.ppBitacora_Registrar
    @Servicio         VARCHAR(20),
    @Operacion        NVARCHAR(60),
    @Direccion        VARCHAR(10),
    @Exito            BIT,
    @Resultado        VARCHAR(30),
    @Cliente          NVARCHAR(100) = NULL,
    @Usuario          NVARCHAR(100) = NULL,
    @Proposito        NVARCHAR(200) = NULL,
    @ReferenciaHash   CHAR(64)      = NULL,
    @CodigoRespuesta  VARCHAR(20)   = NULL,
    @LatenciaMs       INT           = NULL,
    @DesdeCache       BIT           = 0,
    @CorrelacionId    VARCHAR(64)   = NULL,
    @Detalle          NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO integ.Bitacora (Servicio, Operacion, Direccion, Cliente, Usuario, Proposito, ReferenciaHash,
                                Exito, Resultado, CodigoRespuesta, LatenciaMs, DesdeCache, CorrelacionId, Detalle)
    VALUES (@Servicio, @Operacion, @Direccion, @Cliente, @Usuario, @Proposito, UPPER(@ReferenciaHash),
            @Exito, @Resultado, @CodigoRespuesta, @LatenciaMs, @DesdeCache, @CorrelacionId, LEFT(@Detalle, 500));
END;
GO

-- Consulta para la consola y la rendición de cuentas. @ReferenciaHash permite ver
-- todas las consultas hechas sobre una cédula (la aplicación calcula la huella).
CREATE OR ALTER PROCEDURE integ.ppBitacora_Listar
    @Desde           DATETIMEOFFSET(0),
    @Hasta           DATETIMEOFFSET(0),
    @Servicio        VARCHAR(20)   = NULL,
    @Cliente         NVARCHAR(100) = NULL,
    @ReferenciaHash  CHAR(64)      = NULL,
    @SoloFallos      BIT           = 0,
    @Maximo          INT           = 500
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Maximo) BitacoraId, Fecha, Servicio, Operacion, Direccion, Cliente, Usuario, Proposito,
           Exito, Resultado, CodigoRespuesta, LatenciaMs, DesdeCache, CorrelacionId, Detalle
    FROM integ.Bitacora
    WHERE Fecha >= @Desde AND Fecha < @Hasta
      AND (@Servicio IS NULL OR Servicio = @Servicio)
      AND (@Cliente IS NULL OR Cliente = @Cliente)
      AND (@ReferenciaHash IS NULL OR ReferenciaHash = UPPER(@ReferenciaHash))
      AND (@SoloFallos = 0 OR Exito = 0)
    ORDER BY BitacoraId DESC;
END;
GO

-- Indicadores por servicio: volumen, % de éxito, uso de caché y latencias (promedio, máxima y percentil 95).
-- Las respuestas desde caché no cuentan para la latencia (no hubo llamada externa).
CREATE OR ALTER PROCEDURE integ.ppBitacora_Estadisticas
    @Desde  DATETIMEOFFSET(0),
    @Hasta  DATETIMEOFFSET(0)
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH Base AS
    (
        SELECT Servicio, Exito, DesdeCache, LatenciaMs
        FROM integ.Bitacora
        WHERE Fecha >= @Desde AND Fecha < @Hasta
    ),
    Percentil AS
    (
        SELECT DISTINCT Servicio,
               PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY LatenciaMs) OVER (PARTITION BY Servicio) AS LatenciaP95
        FROM Base
        WHERE LatenciaMs IS NOT NULL AND DesdeCache = 0
    )
    SELECT b.Servicio,
           COUNT(*) AS Total,
           SUM(CASE WHEN b.Exito = 1 THEN 1 ELSE 0 END) AS Exitosas,
           SUM(CASE WHEN b.Exito = 0 THEN 1 ELSE 0 END) AS Fallidas,
           SUM(CASE WHEN b.DesdeCache = 1 THEN 1 ELSE 0 END) AS DesdeCache,
           CAST(100.0 * SUM(CASE WHEN b.Exito = 1 THEN 1 ELSE 0 END) / COUNT(*) AS DECIMAL(5,2)) AS PorcentajeExito,
           CAST(AVG(CASE WHEN b.DesdeCache = 0 THEN CAST(b.LatenciaMs AS DECIMAL(18,2)) END) AS DECIMAL(18,2)) AS LatenciaPromedioMs,
           MAX(CASE WHEN b.DesdeCache = 0 THEN b.LatenciaMs END) AS LatenciaMaximaMs,
           CAST(MAX(p.LatenciaP95) AS DECIMAL(18,2)) AS LatenciaP95Ms
    FROM Base b
    LEFT JOIN Percentil p ON p.Servicio = b.Servicio
    GROUP BY b.Servicio
    ORDER BY b.Servicio;
END;
GO

/* ----------------------------- CACHÉ --------------------------------------- */
-- Devuelve el resultado guardado solo si no ha vencido.
CREATE OR ALTER PROCEDURE integ.ppCache_Obtener
    @Servicio  VARCHAR(10),
    @Clave     CHAR(64)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Resultado, Datos, FechaConsulta, VenceEn
    FROM integ.CacheValidacion
    WHERE Servicio = @Servicio AND Clave = UPPER(@Clave) AND VenceEn > SYSDATETIMEOFFSET();
END;
GO

-- Guarda o reemplaza un resultado. @Dias NULL = vigencia del parámetro CacheDiasVigencia (30).
CREATE OR ALTER PROCEDURE integ.ppCache_Guardar
    @Servicio   VARCHAR(10),
    @Clave      CHAR(64),
    @Resultado  VARCHAR(30),
    @Datos      NVARCHAR(MAX) = NULL,
    @Dias       INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Vigencia INT = COALESCE(@Dias,
        TRY_CAST((SELECT Valor FROM cfg.Parametro WHERE Clave = 'CacheDiasVigencia') AS INT), 30);
    DECLARE @Ahora DATETIMEOFFSET(0) = SYSDATETIMEOFFSET();

    BEGIN TRANSACTION;
        UPDATE integ.CacheValidacion WITH (UPDLOCK, HOLDLOCK)
        SET Resultado = @Resultado, Datos = @Datos, FechaConsulta = @Ahora, VenceEn = DATEADD(DAY, @Vigencia, @Ahora)
        WHERE Servicio = @Servicio AND Clave = UPPER(@Clave);

        IF @@ROWCOUNT = 0
            INSERT INTO integ.CacheValidacion (Servicio, Clave, Resultado, Datos, FechaConsulta, VenceEn)
            VALUES (@Servicio, UPPER(@Clave), @Resultado, @Datos, @Ahora, DATEADD(DAY, @Vigencia, @Ahora));
    COMMIT TRANSACTION;
END;
GO

-- Por ejemplo, si la JCE informa que una cédula cambió de estado.
CREATE OR ALTER PROCEDURE integ.ppCache_Invalidar
    @Servicio  VARCHAR(10),
    @Clave     CHAR(64)
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM integ.CacheValidacion WHERE Servicio = @Servicio AND Clave = UPPER(@Clave);
END;
GO

CREATE OR ALTER PROCEDURE integ.ppCache_Purgar
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM integ.CacheValidacion WHERE VenceEn <= SYSDATETIMEOFFSET();
    SELECT @@ROWCOUNT AS Eliminados;
END;
GO

/* ----------------------------- CORTACIRCUITOS ------------------------------ */
-- ¿Se puede llamar al servicio ahora?
--   Cerrado                      -> sí
--   Abierto y aún no vence       -> no (responder "Pendiente" y encolar el reintento)
--   Abierto ya vencido           -> pasa a SemiAbierto y deja pasar ESTA llamada de prueba
--   SemiAbierto con prueba en curso -> no; si la prueba no reportó en 30 s, se permite otra
CREATE OR ALTER PROCEDURE integ.ppCircuito_PuedeLlamar
    @Servicio VARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Ahora DATETIMEOFFSET(0) = SYSDATETIMEOFFSET();
    DECLARE @Estado VARCHAR(12), @Hasta DATETIMEOFFSET(0), @Permitido BIT = 0;

    BEGIN TRANSACTION;
        SELECT @Estado = Estado, @Hasta = AbiertoHasta
        FROM integ.Circuito WITH (UPDLOCK, HOLDLOCK)
        WHERE Servicio = @Servicio;

        IF @Estado IS NULL
        BEGIN
            INSERT INTO integ.Circuito (Servicio) VALUES (@Servicio);
            SET @Estado = 'Cerrado';
        END

        IF @Estado = 'Cerrado'
        BEGIN
            SET @Permitido = 1;
        END
        ELSE IF @Hasta <= @Ahora
        BEGIN
            SET @Estado = 'SemiAbierto';
            SET @Hasta = DATEADD(SECOND, 30, @Ahora);
            SET @Permitido = 1;

            UPDATE integ.Circuito
            SET Estado = @Estado, AbiertoHasta = @Hasta, UltimoCambio = @Ahora
            WHERE Servicio = @Servicio;
        END
    COMMIT TRANSACTION;

    SELECT @Permitido AS Permitido, @Estado AS Estado, @Hasta AS AbiertoHasta;
END;
GO

-- Resultado de una llamada real al servicio. Éxito -> Cerrado. Fallo -> suma uno; al llegar
-- al umbral (o si fallaba la llamada de prueba) se abre y se alerta al administrador.
CREATE OR ALTER PROCEDURE integ.ppCircuito_RegistrarResultado
    @Servicio  VARCHAR(20),
    @Exito     BIT,
    @Error     NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Umbral INT = ISNULL(TRY_CAST((SELECT Valor FROM cfg.Parametro WHERE Clave = 'CircuitoFallosParaAbrir') AS INT), 5);
    DECLARE @Segundos INT = ISNULL(TRY_CAST((SELECT Valor FROM cfg.Parametro WHERE Clave = 'CircuitoSegundosAbierto') AS INT), 60);
    DECLARE @Ahora DATETIMEOFFSET(0) = SYSDATETIMEOFFSET();
    DECLARE @EstadoAntes VARCHAR(12), @Fallos INT;

    BEGIN TRANSACTION;
        SELECT @EstadoAntes = Estado, @Fallos = FallosConsecutivos
        FROM integ.Circuito WITH (UPDLOCK, HOLDLOCK)
        WHERE Servicio = @Servicio;

        IF @EstadoAntes IS NULL
        BEGIN
            INSERT INTO integ.Circuito (Servicio) VALUES (@Servicio);
            SET @EstadoAntes = 'Cerrado';
            SET @Fallos = 0;
        END

        IF @Exito = 1
        BEGIN
            UPDATE integ.Circuito
            SET Estado = 'Cerrado', FallosConsecutivos = 0, AbiertoHasta = NULL,
                UltimoCambio = CASE WHEN @EstadoAntes <> 'Cerrado' THEN @Ahora ELSE UltimoCambio END
            WHERE Servicio = @Servicio;
        END
        ELSE
        BEGIN
            SET @Fallos = @Fallos + 1;

            IF @EstadoAntes = 'SemiAbierto' OR @Fallos >= @Umbral
            BEGIN
                UPDATE integ.Circuito
                SET Estado = 'Abierto', FallosConsecutivos = @Fallos, AbiertoHasta = DATEADD(SECOND, @Segundos, @Ahora),
                    UltimoCambio = @Ahora, UltimoError = LEFT(@Error, 500)
                WHERE Servicio = @Servicio;

                IF @EstadoAntes = 'Cerrado'
                BEGIN
                    DECLARE @Aviso NVARCHAR(500) = CONCAT(N'Circuito ABIERTO para ', @Servicio, N' tras ', @Fallos,
                                                          N' fallos seguidos. Último error: ', LEFT(@Error, 300));
                    EXEC integ.ppAlerta_Registrar @Severidad = 'Media', @Servicio = @Servicio, @Mensaje = @Aviso;
                END
            END
            ELSE
            BEGIN
                UPDATE integ.Circuito
                SET FallosConsecutivos = @Fallos, UltimoError = LEFT(@Error, 500)
                WHERE Servicio = @Servicio;
            END
        END
    COMMIT TRANSACTION;

    SELECT Estado, FallosConsecutivos, AbiertoHasta FROM integ.Circuito WHERE Servicio = @Servicio;
END;
GO

CREATE OR ALTER PROCEDURE integ.ppCircuito_Listar
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Servicio, Estado, FallosConsecutivos, AbiertoHasta, UltimoCambio, UltimoError
    FROM integ.Circuito
    ORDER BY Servicio;
END;
GO

-- Desde la consola: cerrar a mano un circuito (por ejemplo, cuando la JCE avisa que ya restableció su servicio).
CREATE OR ALTER PROCEDURE integ.ppCircuito_Reiniciar
    @Servicio VARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE integ.Circuito
    SET Estado = 'Cerrado', FallosConsecutivos = 0, AbiertoHasta = NULL, UltimoCambio = SYSDATETIMEOFFSET()
    WHERE Servicio = @Servicio;

    IF @@ROWCOUNT = 0
        THROW 56030, N'El servicio no existe.', 1;
END;
GO

PRINT N'05 - Procedimientos de interoperabilidad creados.';
GO
