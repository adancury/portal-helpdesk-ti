IF OBJECT_ID(N'dbo.InventarioEquipamentos', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[InventarioEquipamentos] (
        [Id] int NOT NULL IDENTITY,
        [Tipo] nvarchar(40) NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [OrigemCadastro] nvarchar(30) NOT NULL,
        [NomeEquipamento] nvarchar(120) NULL,
        [Fabricante] nvarchar(80) NULL,
        [Modelo] nvarchar(80) NULL,
        [NumeroSerie] nvarchar(80) NULL,
        [Patrimonio] nvarchar(80) NULL,
        [EnderecoIp] nvarchar(45) NULL,
        [EnderecoMac] nvarchar(30) NULL,
        [Hostname] nvarchar(120) NULL,
        [SistemaOperacional] nvarchar(120) NULL,
        [Localizacao] nvarchar(120) NULL,
        [ProprietarioUsuarioId] int NULL,
        [ProprietarioNomeManual] nvarchar(160) NULL,
        [ProprietarioEmailManual] nvarchar(160) NULL,
        [ProprietarioDepartamentoManual] nvarchar(120) NULL,
        [CriadoEm] datetime2 NOT NULL,
        [AtualizadoEm] datetime2 NOT NULL,
        [UltimaDescobertaEm] datetime2 NULL,
        [Observacoes] nvarchar(max) NULL,
        CONSTRAINT [PK_InventarioEquipamentos] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InventarioEquipamentos_Usuarios_ProprietarioUsuarioId]
            FOREIGN KEY ([ProprietarioUsuarioId]) REFERENCES [dbo].[Usuarios] ([Id]) ON DELETE SET NULL
    );

    CREATE INDEX [IX_InventarioEquipamentos_EnderecoIp]
        ON [dbo].[InventarioEquipamentos] ([EnderecoIp]);

    CREATE INDEX [IX_InventarioEquipamentos_EnderecoMac]
        ON [dbo].[InventarioEquipamentos] ([EnderecoMac]);

    CREATE INDEX [IX_InventarioEquipamentos_NumeroSerie]
        ON [dbo].[InventarioEquipamentos] ([NumeroSerie]);

    CREATE INDEX [IX_InventarioEquipamentos_Patrimonio]
        ON [dbo].[InventarioEquipamentos] ([Patrimonio]);

    CREATE INDEX [IX_InventarioEquipamentos_ProprietarioUsuarioId]
        ON [dbo].[InventarioEquipamentos] ([ProprietarioUsuarioId]);
END;

IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM [dbo].[__EFMigrationsHistory]
       WHERE [MigrationId] = N'20260521120000_AddInventarioEquipamentos'
   )
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260521120000_AddInventarioEquipamentos', N'9.0.6');
END;
