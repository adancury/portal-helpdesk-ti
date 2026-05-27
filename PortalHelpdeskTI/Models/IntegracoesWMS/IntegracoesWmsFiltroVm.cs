namespace PortalHelpdeskTI.ViewModels.IntegracoesWms
{
    public sealed class IntegracoesWmsFiltroVm
    {
        public string Aba { get; set; } = "envios"; // envios | retornos

        public DateTime DataIni { get; set; } = DateTime.Today;
        public DateTime DataFim { get; set; } = DateTime.Today;
        public string? Status { get; set; }
        public string? Metodo { get; set; } // para envios (worker)
        public string? Texto { get; set; }  // busca no MESSAGE

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;

        public bool AutoRefresh { get; set; } = true;
    }
}
