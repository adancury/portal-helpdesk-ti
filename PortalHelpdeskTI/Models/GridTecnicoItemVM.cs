namespace PortalHelpdeskTI.Models
{
    public sealed class GridTecnicoItemVM
    {
        public PortalHelpdeskTI.Models.Chamado Chamado { get; init; } = default!;
        public int SlaPercent { get; init; }
        public string SlaCss { get; init; } = "bg-success";
        public string SlaLabel { get; init; } = "";
        public bool SlaPaused { get; init; }
    }
}
