namespace PortalHelpdeskTI.Models
{
    public class IntegracoesNotas
    {
        // Log de envio
        public int DocEntry { get; set; }
        public int DocNum { get; set; }
        public DateTime? SentAt { get; set; }
        public string StatusEnvio { get; set; } = "";
        public string ErroEnvio { get; set; } = "";

        // Cliente
        public string CardCode { get; set; } = "";
        public string CardName { get; set; } = "";
        public string EmailCliente { get; set; } = "";

        // Nota
        public DateTime? DataDocumento { get; set; }
        public DateTime? DataLancamento { get; set; }
        public DateTime? DataVencimento { get; set; }
        public decimal ValorNota { get; set; }

        public int NumNf { get; set; }
        public string SerieNF { get; set; } = "";
        public string ChaveNf { get; set; } = "";

        // Status documento SAP
        public string StatusDocumento { get; set; } = "";

        // Auxiliares
        public string Observacoes { get; set; } = "";
        public string RefCliente { get; set; } = "";
    }
}