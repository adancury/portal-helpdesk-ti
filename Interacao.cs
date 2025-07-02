public class Interacao
{
    public int Id { get; set; }
    public int ChamadoId { get; set; }
    public Chamado Chamado { get; set; }
    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; }
    public DateTime Data { get; set; }
    public string Mensagem { get; set; }
}
