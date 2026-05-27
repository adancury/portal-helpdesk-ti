namespace PortalHelpdeskTI.ViewModels.Integracoes
{
    public class WmsFilaFaturamentoViewModel
    {
        public string Code { get; set; } = "";

        public int DocEntry { get; set; }

        public int DocNum { get; set; }

        public string TipoEvento { get; set; } = "";

        public int NumOrdSaida { get; set; }

        public int NumNf { get; set; }

        public decimal ValorNF { get; set; }

        public string SerieNF { get; set; } = "";

        public string DataFaturamento { get; set; } = "";

        public string ChaveNf { get; set; } = "";

        public int NumeroDoc { get; set; }

        public int Tentativas { get; set; }

        public string Status { get; set; } = "";
    }
}