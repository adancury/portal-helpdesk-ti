namespace PortalHelpdeskTI.Models.Permissoes
{
    public class RelatorioPermissaoDepartamento
    {
        public int Id { get; set; }
        public int DepartamentoId { get; set; }
        public string RelatorioKey { get; set; } = "";
        public bool PodeVer { get; set; } = true;
    }
}
