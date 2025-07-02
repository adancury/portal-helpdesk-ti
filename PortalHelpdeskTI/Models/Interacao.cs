using PortalHelpdeskTI.Models;
public class Interacao
{
    public int Id { get; set; }
    public int ChamadoId { get; set; }
    public int UsuarioId { get; set; }
    public DateTime Data { get; set; }
    public string Mensagem { get; set; }

    // Navegação
    public virtual Chamado Chamado { get; set; }
    public virtual Usuario Usuario { get; set; }
}
