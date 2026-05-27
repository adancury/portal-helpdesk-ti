using Microsoft.EntityFrameworkCore;
using PortalHelpdeskTI.Models;
using PortalHelpdeskTI.Models.Comissoes;
using PortalHelpdeskTI.Models.IntegracoesWmsDados;
using PortalHelpdeskTI.Models.Inventario;
using PortalHelpdeskTI.Models.Permissoes; // ✅ NOVO
using PortalHelpdeskTI.Models.Quizzes;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Usuario> Usuarios { get; set; }
    public DbSet<Chamado> Chamados { get; set; }
    public DbSet<Interacao> Interacoes { get; set; }
    public DbSet<Anexo> Anexos { get; set; }
    public DbSet<TipoChamado> TipoChamado { get; set; }
    public DbSet<CategoriaChamado> CategoriaChamado { get; set; }
    public DbSet<SubcategoriaChamado> SubcategoriaChamado { get; set; }
    public DbSet<Departamento> Departamentos { get; set; }
    public DbSet<SmtpConfiguracao> SmtpConfiguracoes { get; set; }
    public DbSet<TemplateEmail> TemplatesEmail { get; set; }
    public DbSet<BaseConhecimentoIA> BaseConhecimentoIA { get; set; }
    public DbSet<PalavraChaveIA> PalavrasChaveIA { get; set; }

    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<QuizQuestao> QuizQuestoes => Set<QuizQuestao>();
    public DbSet<QuizOpcao> QuizOpcoes => Set<QuizOpcao>();
    public DbSet<QuizTentativa> QuizTentativas => Set<QuizTentativa>();
    public DbSet<QuizResposta> QuizRespostas => Set<QuizResposta>();

    public DbSet<AvaliacaoChamado> AvaliacoesChamado { get; set; } = default!;
    public DbSet<PortalHelpdeskTI.Models.ChamadoStatusLog> ChamadoStatusLogs { get; set; } = default!;
    public DbSet<SLAConfiguracao> SLAConfiguracoes { get; set; }

    public DbSet<ComissaoVendedor> ComissaoVendedores => Set<ComissaoVendedor>();
    public DbSet<ComissaoAjuste> ComissaoAjustes => Set<ComissaoAjuste>();
    public DbSet<ComissaoEnvioLog> ComissaoEnvioLogs { get; set; }
    public DbSet<EquipamentoInventario> InventarioEquipamentos => Set<EquipamentoInventario>();
    public DbSet<InventarioAnexo> InventarioAnexos => Set<InventarioAnexo>();
    public DbSet<WmsProcesso> WmsProcessos => Set<WmsProcesso>();
    public DbSet<WmsProcessoLog> WmsProcessoLogs => Set<WmsProcessoLog>();
    public DbSet<WmsSyncExecucao> WmsSyncExecucoes => Set<WmsSyncExecucao>();

    // =========================================================
    // 🔐 PERMISSÕES DE RELATÓRIOS (NOVO)
    // =========================================================
    public DbSet<RelatorioCatalogo> RelatoriosCatalogo => Set<RelatorioCatalogo>();
    public DbSet<RelatorioPermissaoUsuario> RelatorioPermissaoUsuario => Set<RelatorioPermissaoUsuario>();
    public DbSet<RelatorioPermissaoDepartamento> RelatorioPermissaoDepartamento => Set<RelatorioPermissaoDepartamento>();


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // AVALIAÇÃO (índices)
        modelBuilder.Entity<AvaliacaoChamado>()
            .ToTable("AvaliacoesChamado")
            .HasIndex(a => new { a.ChamadoId, a.UsuarioId })
            .IsUnique();

        // CHAMADO
        modelBuilder.Entity<Chamado>()
            .HasOne(c => c.TipoChamado)
            .WithMany()
            .HasForeignKey(c => c.TipoChamadoId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Chamado>()
            .HasOne(c => c.Categoria)
            .WithMany()
            .HasForeignKey(c => c.CategoriaId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Chamado>()
            .HasOne(c => c.Subcategoria)
            .WithMany()
            .HasForeignKey(c => c.SubcategoriaId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chamado -> Usuario (Solicitante)
        modelBuilder.Entity<Chamado>()
            .HasOne(c => c.Usuario)
            .WithMany()
            .HasForeignKey(c => c.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chamado -> Usuario (Técnico)
        modelBuilder.Entity<Chamado>()
            .HasOne(c => c.Tecnico)
            .WithMany()
            .HasForeignKey(c => c.TecnicoId)
            .OnDelete(DeleteBehavior.Restrict);

        // TIPOCHAMADO → CATEGORIACHAMADO
        modelBuilder.Entity<CategoriaChamado>()
            .HasOne(c => c.TipoChamado)
            .WithMany(t => t.Categorias)
            .HasForeignKey(c => c.TipoChamadoId)
            .OnDelete(DeleteBehavior.Restrict);

        // CATEGORIACHAMADO → SUBCATEGORIACHAMADO
        modelBuilder.Entity<SubcategoriaChamado>()
            .HasOne(s => s.Categoria)
            .WithMany(c => c.Subcategorias)
            .HasForeignKey(s => s.CategoriaId)
            .OnDelete(DeleteBehavior.Cascade);

        // INTERAÇÃO
        modelBuilder.Entity<Interacao>()
            .HasOne(i => i.Chamado)
            .WithMany(c => c.Interacoes)
            .HasForeignKey(i => i.ChamadoId)
            .OnDelete(DeleteBehavior.Cascade);

        // ANEXO
        modelBuilder.Entity<Anexo>()
            .HasOne(a => a.Interacao)
            .WithMany(i => i.Anexos)
            .HasForeignKey(a => a.InteracaoId)
            .OnDelete(DeleteBehavior.Restrict);

        // USUÁRIO → DEPARTAMENTO
        modelBuilder.Entity<Usuario>()
            .HasOne(u => u.Departamento)
            .WithMany(d => d.Usuarios)
            .HasForeignKey(u => u.DepartamentoId)
            .OnDelete(DeleteBehavior.Restrict);

        // QUIZ precisions/relacionamentos
        modelBuilder.Entity<Quiz>(e =>
        {
            e.Property(p => p.Titulo).HasMaxLength(200).IsRequired();
            e.Property(p => p.Categoria).HasMaxLength(100);
            e.Property(p => p.NotaCortePercentual).HasPrecision(5, 2);
        });
        modelBuilder.Entity<QuizQuestao>(e =>
        {
            e.Property(p => p.Enunciado).IsRequired();
            e.HasOne(p => p.Quiz).WithMany(q => q.Questoes)
             .HasForeignKey(p => p.QuizId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<QuizOpcao>(e =>
        {
            e.Property(p => p.Texto).IsRequired();
            e.HasOne(p => p.Questao).WithMany(q => q.Opcoes)
             .HasForeignKey(p => p.QuizQuestaoId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<QuizTentativa>(e =>
        {
            e.HasOne(p => p.Quiz).WithMany().HasForeignKey(p => p.QuizId);
        });
        modelBuilder.Entity<QuizResposta>(e =>
        {
            e.HasOne(p => p.Tentativa).WithMany()
             .HasForeignKey(p => p.QuizTentativaId).OnDelete(DeleteBehavior.Cascade);
        });

        // SLA (nome exato da tabela)
        modelBuilder.Entity<SLAConfiguracao>()
            .ToTable("SLAConfiguracoes")
            .HasOne(s => s.Categoria)
            .WithMany()
            .HasForeignKey(s => s.CategoriaId);

        modelBuilder.Entity<Chamado>()
            .Property(c => c.Status)
            .HasMaxLength(30);

        // COMISSÕES
        modelBuilder.Entity<ComissaoVendedor>()
            .HasIndex(x => x.SlpCode)
            .IsUnique();

        modelBuilder.Entity<ComissaoAjuste>()
            .HasIndex(x => new { x.SlpCode, x.DataIni, x.DataFim });

        modelBuilder.Entity<ComissaoAjuste>()
            .Property(x => x.Tipo)
            .HasConversion<string>();

        modelBuilder.Entity<ComissaoVendedor>().ToTable("ComissaoVendedor");
        modelBuilder.Entity<ComissaoAjuste>().ToTable("ComissaoAjuste");

        modelBuilder.Entity<EquipamentoInventario>(e =>
        {
            e.ToTable("InventarioEquipamentos");
            e.HasKey(x => x.Id);
            e.Property(x => x.Tipo).HasMaxLength(40).IsRequired();
            e.Property(x => x.Status).HasMaxLength(40).IsRequired();
            e.Property(x => x.OrigemCadastro).HasMaxLength(30).IsRequired();
            e.Property(x => x.NomeEquipamento).HasMaxLength(120);
            e.Property(x => x.Fabricante).HasMaxLength(80);
            e.Property(x => x.Modelo).HasMaxLength(80);
            e.Property(x => x.NumeroSerie).HasMaxLength(80);
            e.Property(x => x.Patrimonio).HasMaxLength(80);
            e.Property(x => x.EnderecoIp).HasMaxLength(45);
            e.Property(x => x.EnderecoMac).HasMaxLength(30);
            e.Property(x => x.Hostname).HasMaxLength(120);
            e.Property(x => x.SistemaOperacional).HasMaxLength(120);
            e.Property(x => x.Localizacao).HasMaxLength(120);
            e.Property(x => x.ProprietarioNomeManual).HasMaxLength(160);
            e.Property(x => x.ProprietarioEmailManual).HasMaxLength(160);
            e.Property(x => x.ProprietarioDepartamentoManual).HasMaxLength(120);

            e.HasIndex(x => x.EnderecoMac);
            e.HasIndex(x => x.EnderecoIp);
            e.HasIndex(x => x.Patrimonio);
            e.HasIndex(x => x.NumeroSerie);

            e.HasOne(x => x.ProprietarioUsuario)
                .WithMany()
                .HasForeignKey(x => x.ProprietarioUsuarioId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<InventarioAnexo>(e =>
        {
            e.ToTable("InventarioAnexos");
            e.HasKey(x => x.Id);
            e.Property(x => x.NomeOriginal).HasMaxLength(260).IsRequired();
            e.Property(x => x.NomeArquivo).HasMaxLength(260).IsRequired();
            e.Property(x => x.CaminhoRelativo).HasMaxLength(500).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(120);

            e.HasIndex(x => x.EquipamentoInventarioId);

            e.HasOne(x => x.EquipamentoInventario)
                .WithMany(x => x.Anexos)
                .HasForeignKey(x => x.EquipamentoInventarioId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WmsProcesso>(e =>
        {
            e.ToTable("WmsProcessos");
            e.HasKey(x => x.Id);
            e.Property(x => x.Tipo).HasMaxLength(40).IsRequired();
            e.Property(x => x.ChaveProcesso).HasMaxLength(120).IsRequired();
            e.Property(x => x.ChaveItem).HasMaxLength(220).IsRequired();
            e.Property(x => x.Status).HasMaxLength(80);
            e.Property(x => x.StatusAnterior).HasMaxLength(80);
            e.Property(x => x.CodProprietario).HasMaxLength(40);
            e.Property(x => x.NomeProprietario).HasMaxLength(180);
            e.Property(x => x.NumeroDocumento).HasMaxLength(80);
            e.Property(x => x.NumeroPedido).HasMaxLength(80);
            e.Property(x => x.CodigoProduto).HasMaxLength(80);
            e.Property(x => x.DescricaoProduto).HasMaxLength(260);
            e.Property(x => x.ClienteFornecedor).HasMaxLength(180);
            e.Property(x => x.UsuarioResponsavel).HasMaxLength(120);
            e.Property(x => x.PayloadHash).HasMaxLength(96).IsRequired();
            e.Property(x => x.RawJson).IsRequired();
            e.Property(x => x.QuantidadePrevista).HasPrecision(18, 4);
            e.Property(x => x.QuantidadeExecutada).HasPrecision(18, 4);
            e.Property(x => x.QuantidadeDivergente).HasPrecision(18, 4);
            e.HasIndex(x => new { x.Tipo, x.ChaveItem }).IsUnique();
            e.HasIndex(x => new { x.Tipo, x.Status });
            e.HasIndex(x => x.DataReferencia);
            e.HasIndex(x => x.NumeroPedido);
            e.HasIndex(x => x.NumeroDocumento);
        });

        modelBuilder.Entity<WmsProcessoLog>(e =>
        {
            e.ToTable("WmsProcessoLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Tipo).HasMaxLength(40).IsRequired();
            e.Property(x => x.ChaveProcesso).HasMaxLength(120).IsRequired();
            e.Property(x => x.ChaveItem).HasMaxLength(220).IsRequired();
            e.Property(x => x.Evento).HasMaxLength(40).IsRequired();
            e.Property(x => x.StatusAnterior).HasMaxLength(80);
            e.Property(x => x.StatusNovo).HasMaxLength(80);
            e.Property(x => x.RawJson).IsRequired();
            e.HasIndex(x => new { x.WmsProcessoId, x.CriadoEm });
            e.HasIndex(x => new { x.Tipo, x.ChaveProcesso });
            e.HasOne(x => x.WmsProcesso)
                .WithMany(x => x.Logs)
                .HasForeignKey(x => x.WmsProcessoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WmsSyncExecucao>(e =>
        {
            e.ToTable("WmsSyncExecucoes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Tipo).HasMaxLength(40).IsRequired();
            e.Property(x => x.Status).HasMaxLength(30).IsRequired();
            e.Property(x => x.Mensagem).HasMaxLength(500);
            e.HasIndex(x => new { x.Tipo, x.InicioEm });
        });

        // =========================================================
        // 🔐 PERMISSÕES DE RELATÓRIOS (NOVO)
        // =========================================================
        modelBuilder.Entity<RelatorioCatalogo>(e =>
        {
            e.ToTable("RelatoriosCatalogo");
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).HasMaxLength(80).IsRequired();
            e.HasIndex(x => x.Key).IsUnique();

            e.Property(x => x.Titulo).HasMaxLength(200).IsRequired();
            e.Property(x => x.Descricao).HasMaxLength(400);
            e.Property(x => x.Departamento).HasMaxLength(100);
            e.Property(x => x.UrlVisualizar).HasMaxLength(300);
        });

        modelBuilder.Entity<RelatorioPermissaoUsuario>(e =>
        {
            e.ToTable("RelatorioPermissaoUsuario");
            e.HasKey(x => x.Id);

            e.Property(x => x.RelatorioKey).HasMaxLength(80).IsRequired();
            e.HasIndex(x => new { x.UsuarioId, x.RelatorioKey }).IsUnique();

            e.HasOne<Usuario>()
                .WithMany()
                .HasForeignKey(x => x.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne<RelatorioCatalogo>()
                .WithMany()
                .HasForeignKey(x => x.RelatorioKey)
                .HasPrincipalKey(r => r.Key)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RelatorioPermissaoDepartamento>(e =>
        {
            e.ToTable("RelatorioPermissaoDepartamento");
            e.HasKey(x => x.Id);

            e.Property(x => x.RelatorioKey).HasMaxLength(80).IsRequired();
            e.HasIndex(x => new { x.DepartamentoId, x.RelatorioKey }).IsUnique();

            e.HasOne<Departamento>()
                .WithMany()
                .HasForeignKey(x => x.DepartamentoId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne<RelatorioCatalogo>()
                .WithMany()
                .HasForeignKey(x => x.RelatorioKey)
                .HasPrincipalKey(r => r.Key)
                .OnDelete(DeleteBehavior.Cascade);
        });

        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ComissaoVendedor>()
            .ToTable(tb => tb.HasTrigger("TR_ComissaoVendedor_Ativo_ForceParticipa"));
    }
}
