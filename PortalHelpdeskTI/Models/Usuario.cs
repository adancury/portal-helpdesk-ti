public class Usuario
{
    public int Id { get; set; }
    public string Nome { get; set; }
    public string Email { get; set; }
    public string SenhaHash { get; set; }
    public string Perfil { get; set; }  // Usuario, Tecnico, Admin
    public bool Ativo { get; set; }
}
