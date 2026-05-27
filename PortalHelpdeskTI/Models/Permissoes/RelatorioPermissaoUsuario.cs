namespace PortalHelpdeskTI.Models.Permissoes
{
    public class RelatorioPermissaoUsuario
    {
        public int Id { get; set; }
        public int UsuarioId { get; set; }
        public string RelatorioKey { get; set; } = "";
        public bool PodeVer { get; set; } = true;
    }
}
