using PortalHelpdeskTI.ViewModels.Relatorios;

namespace PortalHelpdeskTI.Models.Relatorios
{
    public class PrevisaoRupturaVM
    {
        public string ItemCode { get; set; } = "";
        public string ItemName { get; set; } = "";
        public string ForaLinhaStatus { get; set; } = "";
        public string CurvaAbc { get; set; } = "C";
        public decimal Faturamento12Meses { get; set; }

        public decimal EstoqueAtual { get; set; }
        public decimal EmTransito { get; set; }
        public decimal Comprometido { get; set; }
        public decimal ConsumoMedioDia { get; set; }
        public int LeadTimeDias { get; set; }
        public decimal EstoqueProjetado { get; set; }
        public decimal DemandaLeadTime { get; set; }
        public decimal? DiasAteRuptura { get; set; }
        public DateTime? DataRuptura { get; set; }
        public string NivelRisco { get; set; } = "";
        public DateTime? DataProximaCompra { get; set; }
    }

    public class PrevisaoRupturaPage
    {
        public List<PrevisaoRupturaVM> Linhas { get; set; } = new();

        // paginação
        public int Total { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;

        // filtros
        public string? ItemFiltro { get; set; }
        public string? RiscoFiltro { get; set; }
        public int? DiasMax { get; set; }
        public DateTime? UltimaAtualizacao { get; set; }
    }
}
