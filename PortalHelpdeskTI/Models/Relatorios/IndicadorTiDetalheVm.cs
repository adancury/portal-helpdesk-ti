namespace PortalHelpdeskTI.Models.Relatorios
{
    public class IndicadorTiDetalheVm
    {
        public IndicadorTiMensalDto? Consolidado { get; set; }
        public List<IndicadorTiDetalheDto> Detalhes { get; set; } = new();
    }
}
