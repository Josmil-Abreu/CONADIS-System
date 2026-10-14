/* =============================================================================
   CONADIS.Integracion - 09: Tolerancia a caídas (prueba "tumbar los demás módulos")
   Antes: tras 8 intentos (~1 hora) un mensaje pasaba a Fallido aunque el Core solo estuviera caído,
   y la espera entre reintentos crecía hasta 1 hora.
   Ahora: si el destino está caído (fallo transitorio) el mensaje NUNCA se da por perdido y se
   reintenta como máximo cada 30 s; al volver el Core se entrega solo. Solo los rechazos del Core
   (datos inválidos) pasan a Fallido.
   ============================================================================= */
USE CONADIS_IntegracionDB;
GO

MERGE cfg.Parametro AS destino
USING (VALUES ('OutboxSegundosMaximoTransitorio', N'30', N'Espera máxima entre reintentos cuando el destino está caído (fallo transitorio).'))
      AS origen (Clave, Valor, Descripcion)
ON destino.Clave = origen.Clave
WHEN NOT MATCHED THEN INSERT (Clave, Valor, Descripcion) VALUES (origen.Clave, origen.Valor, origen.Descripcion);
GO

-- Fallo al entregar: se reprograma con espera creciente (30 s, 60 s, 120 s... hasta 1 hora).
-- @Permanente = 1 (ej.: el destino rechazó los datos) o intentos agotados -> Fallido + alerta.
-- @Transitorio = 1 (el destino está caído): NUNCA pasa a Fallido y la espera no supera
--   OutboxSegundosMaximoTransitorio (30 s): en cuanto el destino vuelve, el mensaje se entrega solo.
CREATE OR ALTER PROCEDURE integ.ppMensaje_RegistrarFallo
    @MensajeId   BIGINT,
    @Error       NVARCHAR(1000),
    @Permanente  BIT = 0,
    @Transitorio BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Intentos INT, @MaxIntentos INT, @Tipo VARCHAR(60), @Destino VARCHAR(20);
    DECLARE @Base INT = ISNULL(TRY_CAST((SELECT Valor FROM cfg.Parametro WHERE Clave = 'OutboxSegundosBase') AS INT), 30);
    DECLARE @Tope INT = ISNULL(TRY_CAST((SELECT Valor FROM cfg.Parametro WHERE Clave = 'OutboxSegundosMaximo') AS INT), 3600);
    IF @Transitorio = 1
        SET @Tope = ISNULL(TRY_CAST((SELECT Valor FROM cfg.Parametro WHERE Clave = 'OutboxSegundosMaximoTransitorio') AS INT), 30);

    SELECT @Intentos = Intentos, @MaxIntentos = MaxIntentos, @Tipo = Tipo, @Destino = Destino
    FROM integ.Mensaje
    WHERE MensajeId = @MensajeId AND Estado = 'EnProceso';

    IF @Intentos IS NULL
        THROW 56011, N'El mensaje no existe o no está en proceso.', 1;

    BEGIN TRANSACTION;
        IF @Permanente = 1 OR (@Intentos >= @MaxIntentos AND @Transitorio = 0)
        BEGIN
            UPDATE integ.Mensaje
            SET Estado = 'Fallido', UltimoError = LEFT(@Error, 1000), BloqueadoPor = NULL, BloqueadoHasta = NULL
            WHERE MensajeId = @MensajeId;

            DECLARE @Aviso NVARCHAR(500) = CONCAT(N'El mensaje ', @MensajeId, N' (', @Tipo, N') no se pudo entregar tras ',
                                                  @Intentos, N' intento(s): ', LEFT(@Error, 300));
            EXEC integ.ppAlerta_Registrar @Severidad = 'Alta', @Servicio = @Destino, @Mensaje = @Aviso;
        END
        ELSE
        BEGIN
            -- 2^(intentos-1) * base, sin pasar del tope (y sin desbordar el entero)
            DECLARE @Espera INT = CASE WHEN @Intentos >= 17 THEN @Tope ELSE @Base * POWER(2, @Intentos - 1) END;
            IF @Espera > @Tope SET @Espera = @Tope;

            UPDATE integ.Mensaje
            SET Estado = 'Pendiente', UltimoError = LEFT(@Error, 1000), BloqueadoPor = NULL, BloqueadoHasta = NULL,
                ProximoIntento = DATEADD(SECOND, @Espera, SYSDATETIMEOFFSET())
            WHERE MensajeId = @MensajeId;
        END
    COMMIT TRANSACTION;

    SELECT Estado, Intentos, ProximoIntento FROM integ.Mensaje WHERE MensajeId = @MensajeId;
END;
GO

-- Mensajes que ya se dieron por fallidos SOLO porque el Core estaba caído: vuelven a la cola.
UPDATE integ.Mensaje
SET Estado = 'Pendiente', Intentos = 0, ProximoIntento = SYSDATETIMEOFFSET()
WHERE Estado = 'Fallido' AND UltimoError LIKE N'Core no disponible%';
GO

PRINT N'09 - Tolerancia a caídas configurada.';
GO
