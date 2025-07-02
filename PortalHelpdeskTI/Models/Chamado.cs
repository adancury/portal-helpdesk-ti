public class Chamado
{
    public int Id { get; set; }
    public string Titulo { get; set; }
    public string Descricao { get; set; }
    public DateTime DataAbertura { get; set; }
    public string Status { get; set; }
    public string Prioridade { get; set; }
    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; }
    public int? TecnicoId { get; set; }
    public Usuario Tecnico { get; set; }
    public DateTime? DataFechamento { get; set; }
    public virtual ICollection<Interacao> Interacoes { get; set; }
}
