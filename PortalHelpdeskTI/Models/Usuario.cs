using PortalHelpdeskTI.Models;
public class Usuario
{
    public int Id { get; set; }
    public string Nome { get; set; }
    public string Email { get; set; }
    public string SenhaHash { get; set; }
    public string Perfil { get; set; }  // Usuario, Tecnico, Admin
    public bool Ativo { get; set; }
    public ICollection<Chamado> Chamados { get; set; }
    public ICollection<Chamado> ChamadosAssumidos { get; set; }
    public ICollection<Chamado> ChamadosAtendidos { get; set; }
    public ICollection<Chamado> ChamadosCriados { get; set; }
}
