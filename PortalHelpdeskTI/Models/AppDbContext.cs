using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using PortalHelpdeskTI.Models;


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
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Chamado>()
        .HasOne(c => c.Usuario)
        .WithMany()
        .HasForeignKey(c => c.UsuarioId)
        .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Chamado>()
        .HasOne(c => c.Tecnico)
        .WithMany()
        .HasForeignKey(c => c.TecnicoId)
        .OnDelete(DeleteBehavior.Restrict);


    }

}
