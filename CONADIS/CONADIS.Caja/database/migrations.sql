IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012010604_InitialCaja'
)
BEGIN
    IF SCHEMA_ID(N'caja') IS NULL EXEC(N'CREATE SCHEMA [caja];');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012010604_InitialCaja'
)
BEGIN
    CREATE TABLE [caja].[EventoAuditoria] (
        [Id] uniqueidentifier NOT NULL,
        [Numero] bigint NOT NULL,
        [Usuario] nvarchar(1000) NOT NULL,
        [Accion] nvarchar(1000) NOT NULL,
        [Fecha] datetimeoffset NOT NULL,
        [Previo] nvarchar(1000) NOT NULL,
        [Hash] nvarchar(1000) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_EventoAuditoria] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012010604_InitialCaja'
)
BEGIN
    CREATE TABLE [caja].[Operacion] (
        [Id] uniqueidentifier NOT NULL,
        [Clave] nvarchar(300) NOT NULL,
        [Hash] nvarchar(1000) NOT NULL,
        [Respuesta] nvarchar(1000) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Operacion] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012010604_InitialCaja'
)
BEGIN
    CREATE TABLE [caja].[Outbox] (
        [Id] uniqueidentifier NOT NULL,
        [Tipo] nvarchar(60) NOT NULL,
        [RecursoId] uniqueidentifier NOT NULL,
        [Intentos] int NOT NULL,
        [ProximoIntento] datetimeoffset NOT NULL,
        [Completado] bit NOT NULL,
        [Error] nvarchar(1000) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Outbox] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012010604_InitialCaja'
)
BEGIN
    CREATE TABLE [caja].[SecuenciaENcf] (
        [Id] uniqueidentifier NOT NULL,
        [Tipo] int NOT NULL,
        [Desde] bigint NOT NULL,
        [Hasta] bigint NOT NULL,
        [Siguiente] bigint NOT NULL,
        [Vence] date NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_SecuenciaENcf] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012010604_InitialCaja'
)
BEGIN
    CREATE TABLE [caja].[SesionCaja] (
        [Id] uniqueidentifier NOT NULL,
        [Caja] nvarchar(60) NOT NULL,
        [Sucursal] nvarchar(100) NOT NULL,
        [Cajero] nvarchar(100) NOT NULL,
        [Fecha] date NOT NULL,
        [Fondo] decimal(18,2) NOT NULL,
        [DenominacionesApertura] nvarchar(4000) NOT NULL,
        [DenominacionesCierre] nvarchar(4000) NULL,
        [Estado] int NOT NULL,
        [DeclaradoEfectivo] decimal(18,2) NULL,
        [DeclaradoTarjeta] decimal(18,2) NULL,
        [DeclaradoTransferencia] decimal(18,2) NULL,
        [DeclaradoEnLinea] decimal(18,2) NULL,
        [DiferenciaEfectivo] decimal(18,2) NULL,
        [DiferenciaTarjeta] decimal(18,2) NULL,
        [DiferenciaTransferencia] decimal(18,2) NULL,
        [DiferenciaEnLinea] decimal(18,2) NULL,
        [Justificacion] nvarchar(1000) NULL,
        [Supervisor] nvarchar(1000) NULL,
        [CerradaEn] datetimeoffset NULL,
        [Forzado] bit NOT NULL,
        [FirmaCajero] nvarchar(1000) NULL,
        [FirmaSupervisor] nvarchar(1000) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_SesionCaja] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012010604_InitialCaja'
)
BEGIN
    CREATE TABLE [caja].[Tasa] (
        [Id] uniqueidentifier NOT NULL,
        [Codigo] nvarchar(40) NOT NULL,
        [Descripcion] nvarchar(1000) NOT NULL,
        [Precio] decimal(18,2) NOT NULL,
        [Impuesto] decimal(9,6) NOT NULL,
        [Desde] date NOT NULL,
        [Hasta] date NOT NULL,
        [Cuenta] nvarchar(1000) NOT NULL,
        [Resolucion] nvarchar(1000) NOT NULL,
        [PeriodEnd] datetime2 GENERATED ALWAYS AS ROW END HIDDEN NOT NULL,
        [PeriodStart] datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Tasa] PRIMARY KEY ([Id]),
        PERIOD FOR SYSTEM_TIME([PeriodStart], [PeriodEnd])
    ) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [caja].[TasaHistory]));
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012010604_InitialCaja'
)
BEGIN
    CREATE TABLE [caja].[OrdenCobro] (
        [Id] uniqueidentifier NOT NULL,
        [Numero] nvarchar(30) NOT NULL,
        [Sucursal] nvarchar(1000) NOT NULL,
        [TasaId] uniqueidentifier NOT NULL,
        [Cantidad] int NOT NULL,
        [Documento] nvarchar(1000) NOT NULL,
        [Nombre] nvarchar(1000) NOT NULL,
        [Tipo] int NOT NULL,
        [Tramite] nvarchar(1000) NULL,
        [Pagada] bit NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_OrdenCobro] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrdenCobro_Tasa_TasaId] FOREIGN KEY ([TasaId]) REFERENCES [caja].[Tasa] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012010604_InitialCaja'
)
BEGIN
    CREATE TABLE [caja].[Factura] (
        [Id] uniqueidentifier NOT NULL,
        [SesionCajaId] uniqueidentifier NOT NULL,
        [OrdenCobroId] uniqueidentifier NOT NULL,
        [Tipo] int NOT NULL,
        [ENcf] nvarchar(13) NOT NULL,
        [Documento] nvarchar(1000) NOT NULL,
        [Cliente] nvarchar(1000) NOT NULL,
        [CodigoTasa] nvarchar(1000) NOT NULL,
        [Concepto] nvarchar(1000) NOT NULL,
        [Cuenta] nvarchar(1000) NOT NULL,
        [Cantidad] int NOT NULL,
        [Precio] decimal(18,2) NOT NULL,
        [Subtotal] decimal(18,2) NOT NULL,
        [Itbis] decimal(18,2) NOT NULL,
        [Total] decimal(18,2) NOT NULL,
        [Medio] int NOT NULL,
        [Recibido] decimal(18,2) NOT NULL,
        [Referencia] nvarchar(100) NULL,
        [Fecha] datetimeoffset NOT NULL,
        [FacturaReferenciaId] uniqueidentifier NULL,
        [XmlFirmado] nvarchar(max) NOT NULL,
        [CodigoSeguridad] nvarchar(1000) NOT NULL,
        [QrUrl] nvarchar(1000) NOT NULL,
        [QrPng] varbinary(max) NULL,
        [TrackId] nvarchar(1000) NULL,
        [MensajeDgii] nvarchar(1000) NULL,
        [EstadoDgii] int NOT NULL,
        [EsPrueba] bit NOT NULL,
        [PeriodEnd] datetime2 GENERATED ALWAYS AS ROW END HIDDEN NOT NULL,
        [PeriodStart] datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Factura] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Factura_Factura_FacturaReferenciaId] FOREIGN KEY ([FacturaReferenciaId]) REFERENCES [caja].[Factura] ([Id]),
        CONSTRAINT [FK_Factura_OrdenCobro_OrdenCobroId] FOREIGN KEY ([OrdenCobroId]) REFERENCES [caja].[OrdenCobro] ([Id]),
        CONSTRAINT [FK_Factura_SesionCaja_SesionCajaId] FOREIGN KEY ([SesionCajaId]) REFERENCES [caja].[SesionCaja] ([Id]),
        PERIOD FOR SYSTEM_TIME([PeriodStart], [PeriodEnd])
    ) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [caja].[FacturaHistory]));
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012010604_InitialCaja'
)
BEGIN
    CREATE TABLE [caja].[NotaCredito] (
        [Id] uniqueidentifier NOT NULL,
        [FacturaId] uniqueidentifier NOT NULL,
        [Solicitante] nvarchar(1000) NOT NULL,
        [Motivo] nvarchar(1000) NOT NULL,
        [Aprobador] nvarchar(1000) NULL,
        [ComprobanteId] uniqueidentifier NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_NotaCredito] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_NotaCredito_Factura_FacturaId] FOREIGN KEY ([FacturaId]) REFERENCES [caja].[Factura] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012010604_InitialCaja'
)
BEGIN
    CREATE UNIQUE INDEX [IX_EventoAuditoria_Numero] ON [caja].[EventoAuditoria] ([Numero]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012010604_InitialCaja'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Factura_ENcf] ON [caja].[Factura] ([ENcf]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012010604_InitialCaja'
)
BEGIN
    CREATE INDEX [IX_Factura_FacturaReferenciaId] ON [caja].[Factura] ([FacturaReferenciaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012010604_InitialCaja'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Factura_OrdenCobroId] ON [caja].[Factura] ([OrdenCobroId]) WHERE [Tipo] <> 34');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012010604_InitialCaja'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Factura_Referencia] ON [caja].[Factura] ([Referencia]) WHERE [Referencia] IS NOT NULL AND [Referencia] <> '''' AND [Tipo] <> 34');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012010604_InitialCaja'
)
BEGIN
    CREATE INDEX [IX_Factura_SesionCajaId] ON [caja].[Factura] ([SesionCajaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012010604_InitialCaja'
)
BEGIN
    CREATE UNIQUE INDEX [IX_NotaCredito_FacturaId] ON [caja].[NotaCredito] ([FacturaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012010604_InitialCaja'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Operacion_Clave] ON [caja].[Operacion] ([Clave]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012010604_InitialCaja'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrdenCobro_Numero] ON [caja].[OrdenCobro] ([Numero]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012010604_InitialCaja'
)
BEGIN
    CREATE INDEX [IX_OrdenCobro_TasaId] ON [caja].[OrdenCobro] ([TasaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012010604_InitialCaja'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Outbox_Tipo_RecursoId] ON [caja].[Outbox] ([Tipo], [RecursoId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012010604_InitialCaja'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_SesionCaja_Cajero] ON [caja].[SesionCaja] ([Cajero]) WHERE [Estado] <> 2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012010604_InitialCaja'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_SesionCaja_Sucursal_Caja] ON [caja].[SesionCaja] ([Sucursal], [Caja]) WHERE [Estado] <> 2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012010604_InitialCaja'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tasa_Codigo_Desde] ON [caja].[Tasa] ([Codigo], [Desde]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012010604_InitialCaja'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261012010604_InitialCaja', N'10.0.12');
END;

COMMIT;
GO

