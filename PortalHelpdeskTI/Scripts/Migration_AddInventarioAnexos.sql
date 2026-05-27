BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260523012958_AddInventarioAnexos'
)
BEGIN
    CREATE TABLE [InventarioAnexos] (
        [Id] int NOT NULL IDENTITY,
        [EquipamentoInventarioId] int NOT NULL,
        [NomeOriginal] nvarchar(260) NOT NULL,
        [NomeArquivo] nvarchar(260) NOT NULL,
        [CaminhoRelativo] nvarchar(500) NOT NULL,
        [ContentType] nvarchar(120) NULL,
        [TamanhoBytes] bigint NOT NULL,
        [CriadoEm] datetime2 NOT NULL,
        CONSTRAINT [PK_InventarioAnexos] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InventarioAnexos_InventarioEquipamentos_EquipamentoInventarioId] FOREIGN KEY ([EquipamentoInventarioId]) REFERENCES [InventarioEquipamentos] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260523012958_AddInventarioAnexos'
)
BEGIN
    CREATE INDEX [IX_InventarioAnexos_EquipamentoInventarioId] ON [InventarioAnexos] ([EquipamentoInventarioId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260523012958_AddInventarioAnexos'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260523012958_AddInventarioAnexos', N'9.0.6');
END;

COMMIT;
GO

