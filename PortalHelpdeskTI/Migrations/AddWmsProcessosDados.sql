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
    WHERE [MigrationId] = N'20250707120635_InitialCreate'
)
BEGIN
    CREATE TABLE [Departamentos] (
        [Id] int NOT NULL IDENTITY,
        [Nome] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_Departamentos] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250707120635_InitialCreate'
)
BEGIN
    CREATE TABLE [TipoChamado] (
        [Id] int NOT NULL IDENTITY,
        [Nome] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_TipoChamado] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250707120635_InitialCreate'
)
BEGIN
    CREATE TABLE [Usuarios] (
        [Id] int NOT NULL IDENTITY,
        [Nome] nvarchar(max) NOT NULL,
        [Email] nvarchar(max) NOT NULL,
        [SenhaHash] nvarchar(max) NOT NULL,
        [Perfil] nvarchar(max) NOT NULL,
        [Ativo] bit NOT NULL,
        [DepartamentoId] int NULL,
        CONSTRAINT [PK_Usuarios] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Usuarios_Departamentos_DepartamentoId] FOREIGN KEY ([DepartamentoId]) REFERENCES [Departamentos] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250707120635_InitialCreate'
)
BEGIN
    CREATE TABLE [CategoriaChamado] (
        [Id] int NOT NULL IDENTITY,
        [Nome] nvarchar(max) NOT NULL,
        [TipoChamadoId] int NOT NULL,
        CONSTRAINT [PK_CategoriaChamado] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CategoriaChamado_TipoChamado_TipoChamadoId] FOREIGN KEY ([TipoChamadoId]) REFERENCES [TipoChamado] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250707120635_InitialCreate'
)
BEGIN
    CREATE TABLE [SubcategoriaChamado] (
        [Id] int NOT NULL IDENTITY,
        [Nome] nvarchar(max) NOT NULL,
        [CategoriaId] int NOT NULL,
        CONSTRAINT [PK_SubcategoriaChamado] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SubcategoriaChamado_CategoriaChamado_CategoriaId] FOREIGN KEY ([CategoriaId]) REFERENCES [CategoriaChamado] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250707120635_InitialCreate'
)
BEGIN
    CREATE TABLE [Chamados] (
        [Id] int NOT NULL IDENTITY,
        [Titulo] nvarchar(max) NOT NULL,
        [Descricao] nvarchar(max) NOT NULL,
        [Prioridade] nvarchar(max) NOT NULL,
        [DataAbertura] datetime2 NOT NULL,
        [Status] nvarchar(max) NOT NULL,
        [UsuarioId] int NOT NULL,
        [TecnicoId] int NULL,
        [TipoChamadoId] int NOT NULL,
        [CategoriaId] int NOT NULL,
        [SubcategoriaId] int NOT NULL,
        [VisualizadoPeloTecnico] bit NOT NULL,
        [VisualizadoPeloSolicitante] bit NOT NULL,
        [Solucao] nvarchar(max) NULL,
        [UsuarioId1] int NULL,
        [UsuarioId2] int NULL,
        [UsuarioId3] int NULL,
        [UsuarioId4] int NULL,
        CONSTRAINT [PK_Chamados] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Chamados_CategoriaChamado_CategoriaId] FOREIGN KEY ([CategoriaId]) REFERENCES [CategoriaChamado] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Chamados_SubcategoriaChamado_SubcategoriaId] FOREIGN KEY ([SubcategoriaId]) REFERENCES [SubcategoriaChamado] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Chamados_TipoChamado_TipoChamadoId] FOREIGN KEY ([TipoChamadoId]) REFERENCES [TipoChamado] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Chamados_Usuarios_TecnicoId] FOREIGN KEY ([TecnicoId]) REFERENCES [Usuarios] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Chamados_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [Usuarios] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Chamados_Usuarios_UsuarioId1] FOREIGN KEY ([UsuarioId1]) REFERENCES [Usuarios] ([Id]),
        CONSTRAINT [FK_Chamados_Usuarios_UsuarioId2] FOREIGN KEY ([UsuarioId2]) REFERENCES [Usuarios] ([Id]),
        CONSTRAINT [FK_Chamados_Usuarios_UsuarioId3] FOREIGN KEY ([UsuarioId3]) REFERENCES [Usuarios] ([Id]),
        CONSTRAINT [FK_Chamados_Usuarios_UsuarioId4] FOREIGN KEY ([UsuarioId4]) REFERENCES [Usuarios] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250707120635_InitialCreate'
)
BEGIN
    CREATE TABLE [Interacoes] (
        [Id] int NOT NULL IDENTITY,
        [ChamadoId] int NOT NULL,
        [UsuarioId] int NOT NULL,
        [Data] datetime2 NOT NULL,
        [Mensagem] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_Interacoes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Interacoes_Chamados_ChamadoId] FOREIGN KEY ([ChamadoId]) REFERENCES [Chamados] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Interacoes_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [Usuarios] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250707120635_InitialCreate'
)
BEGIN
    CREATE TABLE [Anexos] (
        [Id] int NOT NULL IDENTITY,
        [ChamadoId] int NOT NULL,
        [CaminhoArquivo] nvarchar(max) NOT NULL,
        [NomeOriginal] nvarchar(max) NOT NULL,
        [InteracaoId] int NULL,
        CONSTRAINT [PK_Anexos] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Anexos_Chamados_ChamadoId] FOREIGN KEY ([ChamadoId]) REFERENCES [Chamados] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Anexos_Interacoes_InteracaoId] FOREIGN KEY ([InteracaoId]) REFERENCES [Interacoes] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250707120635_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Anexos_ChamadoId] ON [Anexos] ([ChamadoId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250707120635_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Anexos_InteracaoId] ON [Anexos] ([InteracaoId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250707120635_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CategoriaChamado_TipoChamadoId] ON [CategoriaChamado] ([TipoChamadoId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250707120635_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Chamados_CategoriaId] ON [Chamados] ([CategoriaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250707120635_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Chamados_SubcategoriaId] ON [Chamados] ([SubcategoriaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250707120635_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Chamados_TecnicoId] ON [Chamados] ([TecnicoId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250707120635_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Chamados_TipoChamadoId] ON [Chamados] ([TipoChamadoId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250707120635_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Chamados_UsuarioId] ON [Chamados] ([UsuarioId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250707120635_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Chamados_UsuarioId1] ON [Chamados] ([UsuarioId1]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250707120635_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Chamados_UsuarioId2] ON [Chamados] ([UsuarioId2]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250707120635_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Chamados_UsuarioId3] ON [Chamados] ([UsuarioId3]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250707120635_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Chamados_UsuarioId4] ON [Chamados] ([UsuarioId4]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250707120635_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Interacoes_ChamadoId] ON [Interacoes] ([ChamadoId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250707120635_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Interacoes_UsuarioId] ON [Interacoes] ([UsuarioId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250707120635_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SubcategoriaChamado_CategoriaId] ON [SubcategoriaChamado] ([CategoriaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250707120635_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Usuarios_DepartamentoId] ON [Usuarios] ([DepartamentoId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250707120635_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20250707120635_InitialCreate', N'9.0.6');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250708011427_AddCampoDataConclusao'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20250708011427_AddCampoDataConclusao', N'9.0.6');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250708012635_AddCampoDataConclusao2'
)
BEGIN
    ALTER TABLE [Chamados] ADD [DataConclusao] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250708012635_AddCampoDataConclusao2'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20250708012635_AddCampoDataConclusao2', N'9.0.6');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250711162641_AddTemplateEmail'
)
BEGIN
    CREATE TABLE [TemplatesEmail] (
        [Id] int NOT NULL IDENTITY,
        [Tipo] nvarchar(100) NOT NULL,
        [Assunto] nvarchar(255) NOT NULL,
        [CorpoHtml] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_TemplatesEmail] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250711162641_AddTemplateEmail'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20250711162641_AddTemplateEmail', N'9.0.6');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250714172744_AddRelacionamentoTipoCategoria'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20250714172744_AddRelacionamentoTipoCategoria', N'9.0.6');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250714175443_CorrigirRelacionamentoTipoCategoria'
)
BEGIN
    ALTER TABLE [CategoriaChamado] ADD [TipoChamadoId1] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250714175443_CorrigirRelacionamentoTipoCategoria'
)
BEGIN
    CREATE INDEX [IX_CategoriaChamado_TipoChamadoId1] ON [CategoriaChamado] ([TipoChamadoId1]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250714175443_CorrigirRelacionamentoTipoCategoria'
)
BEGIN
    ALTER TABLE [CategoriaChamado] ADD CONSTRAINT [FK_CategoriaChamado_TipoChamado_TipoChamadoId1] FOREIGN KEY ([TipoChamadoId1]) REFERENCES [TipoChamado] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250714175443_CorrigirRelacionamentoTipoCategoria'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20250714175443_CorrigirRelacionamentoTipoCategoria', N'9.0.6');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250717152950_AddRamalToUsuario'
)
BEGIN
    ALTER TABLE [CategoriaChamado] DROP CONSTRAINT [FK_CategoriaChamado_TipoChamado_TipoChamadoId1];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250717152950_AddRamalToUsuario'
)
BEGIN
    DROP INDEX [IX_CategoriaChamado_TipoChamadoId1] ON [CategoriaChamado];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250717152950_AddRamalToUsuario'
)
BEGIN
    DECLARE @var sysname;
    SELECT @var = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CategoriaChamado]') AND [c].[name] = N'TipoChamadoId1');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [CategoriaChamado] DROP CONSTRAINT [' + @var + '];');
    ALTER TABLE [CategoriaChamado] DROP COLUMN [TipoChamadoId1];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250717152950_AddRamalToUsuario'
)
BEGIN
    ALTER TABLE [Usuarios] ADD [Ramal] nvarchar(max) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250717152950_AddRamalToUsuario'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20250717152950_AddRamalToUsuario', N'9.0.6');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250718184425_AddTokenRedefinicaoSenha'
)
BEGIN
    ALTER TABLE [Usuarios] ADD [TokenExpiraEm] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250718184425_AddTokenRedefinicaoSenha'
)
BEGIN
    ALTER TABLE [Usuarios] ADD [TokenRedefinicaoSenha] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250718184425_AddTokenRedefinicaoSenha'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20250718184425_AddTokenRedefinicaoSenha', N'9.0.6');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250718184911_AddTokenRedefinicaoSenha2'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20250718184911_AddTokenRedefinicaoSenha2', N'9.0.6');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260521120000_AddInventarioEquipamentos'
)
BEGIN
    CREATE TABLE [InventarioEquipamentos] (
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
        CONSTRAINT [FK_InventarioEquipamentos_Usuarios_ProprietarioUsuarioId] FOREIGN KEY ([ProprietarioUsuarioId]) REFERENCES [Usuarios] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260521120000_AddInventarioEquipamentos'
)
BEGIN
    CREATE INDEX [IX_InventarioEquipamentos_EnderecoIp] ON [InventarioEquipamentos] ([EnderecoIp]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260521120000_AddInventarioEquipamentos'
)
BEGIN
    CREATE INDEX [IX_InventarioEquipamentos_EnderecoMac] ON [InventarioEquipamentos] ([EnderecoMac]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260521120000_AddInventarioEquipamentos'
)
BEGIN
    CREATE INDEX [IX_InventarioEquipamentos_NumeroSerie] ON [InventarioEquipamentos] ([NumeroSerie]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260521120000_AddInventarioEquipamentos'
)
BEGIN
    CREATE INDEX [IX_InventarioEquipamentos_Patrimonio] ON [InventarioEquipamentos] ([Patrimonio]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260521120000_AddInventarioEquipamentos'
)
BEGIN
    CREATE INDEX [IX_InventarioEquipamentos_ProprietarioUsuarioId] ON [InventarioEquipamentos] ([ProprietarioUsuarioId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260521120000_AddInventarioEquipamentos'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260521120000_AddInventarioEquipamentos', N'9.0.6');
END;

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

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260526160055_AddWmsProcessosDados'
)
BEGIN
    CREATE TABLE [WmsProcessos] (
        [Id] int NOT NULL IDENTITY,
        [Tipo] nvarchar(40) NOT NULL,
        [ChaveProcesso] nvarchar(120) NOT NULL,
        [ChaveItem] nvarchar(220) NOT NULL,
        [Status] nvarchar(80) NULL,
        [StatusAnterior] nvarchar(80) NULL,
        [DataReferencia] datetime2 NULL,
        [CodProprietario] nvarchar(40) NULL,
        [NomeProprietario] nvarchar(180) NULL,
        [NumeroDocumento] nvarchar(80) NULL,
        [NumeroPedido] nvarchar(80) NULL,
        [CodigoProduto] nvarchar(80) NULL,
        [DescricaoProduto] nvarchar(260) NULL,
        [ClienteFornecedor] nvarchar(180) NULL,
        [UsuarioResponsavel] nvarchar(120) NULL,
        [QuantidadePrevista] decimal(18,4) NULL,
        [QuantidadeExecutada] decimal(18,4) NULL,
        [QuantidadeDivergente] decimal(18,4) NULL,
        [PayloadHash] nvarchar(96) NOT NULL,
        [RawJson] nvarchar(max) NOT NULL,
        [CriadoEm] datetime2 NOT NULL,
        [AtualizadoEm] datetime2 NOT NULL,
        [UltimaSincronizacaoEm] datetime2 NOT NULL,
        CONSTRAINT [PK_WmsProcessos] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260526160055_AddWmsProcessosDados'
)
BEGIN
    CREATE TABLE [WmsSyncExecucoes] (
        [Id] int NOT NULL IDENTITY,
        [Tipo] nvarchar(40) NOT NULL,
        [InicioEm] datetime2 NOT NULL,
        [FimEm] datetime2 NULL,
        [Status] nvarchar(30) NOT NULL,
        [RegistrosRecebidos] int NOT NULL,
        [RegistrosNovos] int NOT NULL,
        [RegistrosAlterados] int NOT NULL,
        [Mensagem] nvarchar(500) NULL,
        CONSTRAINT [PK_WmsSyncExecucoes] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260526160055_AddWmsProcessosDados'
)
BEGIN
    CREATE TABLE [WmsProcessoLogs] (
        [Id] int NOT NULL IDENTITY,
        [WmsProcessoId] int NOT NULL,
        [Tipo] nvarchar(40) NOT NULL,
        [ChaveProcesso] nvarchar(120) NOT NULL,
        [ChaveItem] nvarchar(220) NOT NULL,
        [Evento] nvarchar(40) NOT NULL,
        [StatusAnterior] nvarchar(80) NULL,
        [StatusNovo] nvarchar(80) NULL,
        [CamposAlteradosJson] nvarchar(max) NULL,
        [RawJson] nvarchar(max) NOT NULL,
        [CriadoEm] datetime2 NOT NULL,
        CONSTRAINT [PK_WmsProcessoLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WmsProcessoLogs_WmsProcessos_WmsProcessoId] FOREIGN KEY ([WmsProcessoId]) REFERENCES [WmsProcessos] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260526160055_AddWmsProcessosDados'
)
BEGIN
    CREATE INDEX [IX_WmsProcessoLogs_Tipo_ChaveProcesso] ON [WmsProcessoLogs] ([Tipo], [ChaveProcesso]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260526160055_AddWmsProcessosDados'
)
BEGIN
    CREATE INDEX [IX_WmsProcessoLogs_WmsProcessoId_CriadoEm] ON [WmsProcessoLogs] ([WmsProcessoId], [CriadoEm]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260526160055_AddWmsProcessosDados'
)
BEGIN
    CREATE INDEX [IX_WmsProcessos_DataReferencia] ON [WmsProcessos] ([DataReferencia]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260526160055_AddWmsProcessosDados'
)
BEGIN
    CREATE INDEX [IX_WmsProcessos_NumeroDocumento] ON [WmsProcessos] ([NumeroDocumento]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260526160055_AddWmsProcessosDados'
)
BEGIN
    CREATE INDEX [IX_WmsProcessos_NumeroPedido] ON [WmsProcessos] ([NumeroPedido]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260526160055_AddWmsProcessosDados'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WmsProcessos_Tipo_ChaveItem] ON [WmsProcessos] ([Tipo], [ChaveItem]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260526160055_AddWmsProcessosDados'
)
BEGIN
    CREATE INDEX [IX_WmsProcessos_Tipo_Status] ON [WmsProcessos] ([Tipo], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260526160055_AddWmsProcessosDados'
)
BEGIN
    CREATE INDEX [IX_WmsSyncExecucoes_Tipo_InicioEm] ON [WmsSyncExecucoes] ([Tipo], [InicioEm]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260526160055_AddWmsProcessosDados'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260526160055_AddWmsProcessosDados', N'9.0.6');
END;

COMMIT;
GO

