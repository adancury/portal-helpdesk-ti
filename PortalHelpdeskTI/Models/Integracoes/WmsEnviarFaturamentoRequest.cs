namespace PortalHelpdeskTI.Models.Integracoes
{
    public class WmsEnviarFaturamentoRequest
    {
        public int numOrdSaida { get; set; }

        public int numNf { get; set; }

        public decimal valorNF { get; set; }

        public string serieNF { get; set; } = "";

        public string datafaturamento { get; set; } = "";

        public string chavenf { get; set; } = "";

        public int numeroDoc { get; set; }
    }
}