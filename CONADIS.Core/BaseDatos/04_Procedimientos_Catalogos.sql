/* =============================================================================
   CONADIS.Core - 04: Procedimientos de catálogos, parámetros y numeración
   ============================================================================= */
USE CONADIS_CoreDB;
GO

/* ----------------------------- NUMERACIÓN ---------------------------------- */
-- Devuelve el siguiente número con formato PREFIJO-AAAA-NNNNNNN.
-- Debe llamarse dentro de la transacción del procedimiento que lo usa:
-- el bloqueo UPDLOCK/HOLDLOCK impide números repetidos o saltos entre transacciones.
CREATE OR ALTER PROCEDURE core.ppSecuencia_Siguiente
    @Prefijo  VARCHAR(10),
    @Numero   VARCHAR(20) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Anio INT = YEAR(SYSDATETIMEOFFSET());
    DECLARE @Ultimo INT;

    UPDATE core.Secuencia WITH (UPDLOCK, HOLDLOCK)
    SET @Ultimo = Ultimo = Ultimo + 1
    WHERE Prefijo = @Prefijo AND Anio = @Anio;

    IF @Ultimo IS NULL
    BEGIN
        SET @Ultimo = 1;
        INSERT INTO core.Secuencia (Prefijo, Anio, Ultimo) VALUES (@Prefijo, @Anio, @Ultimo);
    END

    SET @Numero = CONCAT(@Prefijo, '-', @Anio, '-', RIGHT(CONCAT('0000000', @Ultimo), 7));
END;
GO

/* ----------------------------- PARÁMETROS ---------------------------------- */
CREATE OR ALTER PROCEDURE cat.ppParametro_Listar
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Clave, Valor, Descripcion FROM cat.Parametro ORDER BY Clave;
END;
GO

CREATE OR ALTER PROCEDURE cat.ppParametro_Actualizar
    @Clave          VARCHAR(50),
    @Valor          NVARCHAR(200),
    @UsuarioAccion  NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Anterior NVARCHAR(200) = (SELECT Valor FROM cat.Parametro WHERE Clave = @Clave);
    IF @Anterior IS NULL THROW 51101, N'El parámetro no existe.', 1;

    UPDATE cat.Parametro SET Valor = @Valor WHERE Clave = @Clave;

    DECLARE @Detalle NVARCHAR(2000) = CONCAT(@Anterior, N' -> ', @Valor);
    EXEC audit.ppEvento_Registrar @Usuario = @UsuarioAccion, @Accion = N'ParametroModificado',
         @Entidad = N'Parametro', @EntidadId = @Clave, @Detalle = @Detalle;
END;
GO

/* ----------------------------- CENTROS DE VALORACIÓN ----------------------- */
CREATE OR ALTER PROCEDURE cat.ppCentro_Listar
    @SoloActivos BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CentroId, Nombre, Provincia, CapacidadDiaria, Activo
    FROM cat.CentroValoracion
    WHERE @SoloActivos = 0 OR Activo = 1
    ORDER BY Nombre;
END;
GO

CREATE OR ALTER PROCEDURE cat.ppCentro_Guardar
    @CentroId         INT = NULL,
    @Nombre           NVARCHAR(150),
    @Provincia        NVARCHAR(60),
    @CapacidadDiaria  INT,
    @Activo           BIT,
    @UsuarioAccion    NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM cat.CentroValoracion WHERE Nombre = @Nombre AND (@CentroId IS NULL OR CentroId <> @CentroId))
        THROW 51111, N'Ya existe un centro con ese nombre.', 1;

    IF @CentroId IS NULL
    BEGIN
        INSERT INTO cat.CentroValoracion (Nombre, Provincia, CapacidadDiaria, Activo)
        VALUES (@Nombre, @Provincia, @CapacidadDiaria, @Activo);
        SET @CentroId = SCOPE_IDENTITY();
    END
    ELSE
        UPDATE cat.CentroValoracion
        SET Nombre = @Nombre, Provincia = @Provincia, CapacidadDiaria = @CapacidadDiaria, Activo = @Activo
        WHERE CentroId = @CentroId;

    EXEC audit.ppEvento_Registrar @Usuario = @UsuarioAccion, @Accion = N'CentroGuardado',
         @Entidad = N'CentroValoracion', @EntidadId = @CentroId, @Detalle = @Nombre;

    SELECT @CentroId AS CentroId;
END;
GO

/* ----------------------------- CATÁLOGO CIF -------------------------------- */
CREATE OR ALTER PROCEDURE cat.ppCodigoCif_Buscar
    @Texto       NVARCHAR(100) = NULL,
    @Componente  CHAR(1) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (50) Codigo, Componente, Descripcion, Activo
    FROM cat.CodigoCIF
    WHERE Activo = 1
      AND (@Componente IS NULL OR Componente = @Componente)
      AND (@Texto IS NULL OR Codigo LIKE @Texto + '%' OR Descripcion LIKE N'%' + @Texto + N'%')
    ORDER BY Codigo;
END;
GO

CREATE OR ALTER PROCEDURE cat.ppCodigoCif_Guardar
    @Codigo         VARCHAR(10),
    @Descripcion    NVARCHAR(300),
    @Activo         BIT,
    @UsuarioAccion  NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Componente CHAR(1) = LOWER(LEFT(@Codigo, 1));
    IF @Componente NOT IN ('b', 's', 'd', 'e') OR LEN(@Codigo) < 2
        THROW 51121, N'El código CIF debe empezar por b, s, d o e seguido de números (ej.: b210).', 1;

    SET @Codigo = LOWER(@Codigo);

    IF EXISTS (SELECT 1 FROM cat.CodigoCIF WHERE Codigo = @Codigo)
        UPDATE cat.CodigoCIF SET Descripcion = @Descripcion, Activo = @Activo WHERE Codigo = @Codigo;
    ELSE
        INSERT INTO cat.CodigoCIF (Codigo, Componente, Descripcion, Activo)
        VALUES (@Codigo, @Componente, @Descripcion, @Activo);

    EXEC audit.ppEvento_Registrar @Usuario = @UsuarioAccion, @Accion = N'CodigoCifGuardado',
         @Entidad = N'CodigoCIF', @EntidadId = @Codigo;
END;
GO

/* ----------------------------- BAREMO -------------------------------------- */
CREATE OR ALTER PROCEDURE cat.ppBaremo_ObtenerVigente
AS
BEGIN
    SET NOCOUNT ON;
    SELECT b.Version, r.PuntajeDesde, r.PuntajeHasta, r.Grado
    FROM cat.Baremo b
    INNER JOIN cat.BaremoRango r ON r.BaremoId = b.BaremoId
    WHERE b.Vigente = 1
    ORDER BY r.PuntajeDesde;
END;
GO

/* ----------------------------- TASAS --------------------------------------- */
CREATE OR ALTER PROCEDURE cat.ppTasa_Listar
    @SoloActivas BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TasaId, Codigo, Descripcion, Monto, ItbisPorcentaje, Resolucion, Activa
    FROM cat.Tasa
    WHERE @SoloActivas = 0 OR Activa = 1
    ORDER BY Descripcion;
END;
GO

CREATE OR ALTER PROCEDURE cat.ppTasa_Guardar
    @TasaId           INT = NULL,
    @Codigo           VARCHAR(20),
    @Descripcion      NVARCHAR(200),
    @Monto            DECIMAL(18,2),
    @ItbisPorcentaje  DECIMAL(5,2),
    @Resolucion       NVARCHAR(100),
    @Activa           BIT,
    @UsuarioAccion    NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Monto < 0 THROW 51131, N'El monto de la tasa no puede ser negativo.', 1;
    IF EXISTS (SELECT 1 FROM cat.Tasa WHERE Codigo = @Codigo AND (@TasaId IS NULL OR TasaId <> @TasaId))
        THROW 51132, N'Ya existe una tasa con ese código.', 1;

    IF @TasaId IS NULL
    BEGIN
        INSERT INTO cat.Tasa (Codigo, Descripcion, Monto, ItbisPorcentaje, Resolucion, Activa)
        VALUES (@Codigo, @Descripcion, @Monto, @ItbisPorcentaje, @Resolucion, @Activa);
        SET @TasaId = SCOPE_IDENTITY();
    END
    ELSE
        UPDATE cat.Tasa
        SET Codigo = @Codigo, Descripcion = @Descripcion, Monto = @Monto,
            ItbisPorcentaje = @ItbisPorcentaje, Resolucion = @Resolucion, Activa = @Activa
        WHERE TasaId = @TasaId;

    DECLARE @Detalle NVARCHAR(2000) = CONCAT(@Codigo, N' = RD$', @Monto);
    EXEC audit.ppEvento_Registrar @Usuario = @UsuarioAccion, @Accion = N'TasaGuardada',
         @Entidad = N'Tasa', @EntidadId = @TasaId, @Detalle = @Detalle;

    SELECT @TasaId AS TasaId;
END;
GO

PRINT N'04 - Procedimientos de catálogos creados.';
GO