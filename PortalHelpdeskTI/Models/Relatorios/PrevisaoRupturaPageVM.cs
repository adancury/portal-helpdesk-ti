using PortalHelpdeskTI.ViewModels.Relatorios;

namespace PortalHelpdeskTI.Models.Relatorios
{
    public class PrevisaoRupturaPageVM
    {
        public List<PrevisaoRupturaVM> Itens { get; set; } = new();

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int Total { get; set; }

        // filtros (pra manter na paginação)
        public string? ItemFiltro { get; set; }
        public string? RiscoFiltro { get; set; }
        public int? DiasMax { get; set; }
    }

}
