namespace PortalHelpdeskTI.Models.Permissoes
{
    public class RelatorioCatalogo
    {
        public int Id { get; set; }
        public string Key { get; set; } = "";
        public string Titulo { get; set; } = "";
        public string? Descricao { get; set; }
        public string? Departamento { get; set; }
        public string? UrlVisualizar { get; set; }
        public bool Ativo { get; set; } = true;
        public int Ordem { get; set; }
    }
}
