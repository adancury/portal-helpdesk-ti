namespace PortalHelpdeskTI.Models
{
    public class InlineUsuarioDto
    {
        public int Id { get; set; }
        public string Nome { get; set; }
        public string Email { get; set; }
        public string Perfil { get; set; }
        public string Senha { get; set; }  // Nova senha se preenchida
        public int DepartamentoId { get; set; }
        public string Ramal { get; set; }
    }
}
