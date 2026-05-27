using System.ComponentModel.DataAnnotations.Schema;

namespace PortalHelpdeskTI.Models;
[Table("Usuarios")]
public class Usuario
{
    public int Id { get; set; }
    public string Nome { get; set; }
    public string Email { get; set; }
    public string Ramal { get; set; }
    public string SenhaHash { get; set; }
    public string Perfil { get; set; }
    public bool Ativo { get; set; }
    public bool EmailConfirmado { get; set; }
    public string? TokenAtivacao { get; set; }
    public DateTime? TokenAtivacaoExpiraEm { get; set; }
    public ICollection<Chamado> Chamados { get; set; }
    public ICollection<Chamado> ChamadosAssumidos { get; set; }
    public ICollection<Chamado> ChamadosAtendidos { get; set; }
    public ICollection<Chamado> ChamadosCriados { get; set; }
    public int? DepartamentoId { get; set; }
    public Departamento? Departamento { get; set; }
    public string? TokenRedefinicaoSenha { get; set; }
    public DateTime? TokenExpiraEm { get; set; }
}
