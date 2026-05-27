namespace PortalHelpdeskTI.Models.Relatorios
{
    public class AnaliseRupturaVM
    {
        public string ItemCode { get; set; } = "";
        public string ItemName { get; set; } = "";

        public decimal EstoqueAtual { get; set; }
        public decimal EmTransito { get; set; }
        public decimal Comprometido { get; set; }
        public decimal EstoqueProjetado { get; set; }

        public decimal ConsumoHistoricoDia { get; set; }
        public decimal ConsumoIaDia { get; set; }
        public decimal ConsumoMedioDia { get; set; }

        public int LeadTimeDias { get; set; }
        public decimal? DiasAteRuptura { get; set; }
        public DateTime? DataRuptura { get; set; }
        public decimal DemandaLeadTime { get; set; }
        public string NivelRisco { get; set; } = "";

        // textos explicativos
        public string MotivoRisco { get; set; } = "";
        public string ExplicacaoIa { get; set; } = "";
    }
}
