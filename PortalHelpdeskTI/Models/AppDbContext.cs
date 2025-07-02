using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;


public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Usuario> Usuarios { get; set; }
    public DbSet<Chamado> Chamados { get; set; }
    public DbSet<Interacao> Interacoes { get; set; }
    public DbSet<Anexo> Anexos { get; set; }
}
