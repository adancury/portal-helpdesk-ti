namespace PortalHelpdeskTI.Models.Compras
{
    public class ProcessoCompra
    {
        public int Id { get; set; }

        // Solicitação de Compra (OPRQ)
        public int OPRQDocEntry { get; set; }
        public int OPRQDocNum { get; set; }
        public DateTime DataSolicitacao { get; set; }
        public string? Solicitante { get; set; }
        public string? Departamento { get; set; }

        // Pedido de Compra vencedor (OPOR)
        public int? OPORDocEntry { get; set; }
        public int? OPORDocNum { get; set; }
        public string? CardCodeVencedor { get; set; }
        public string? CardNameVencedor { get; set; }
        public decimal? ValorVencedor { get; set; }
        public string? Moeda { get; set; }
        public DateTime? DataPedido { get; set; }

        // Controle
        public string StatusProcesso { get; set; } = "Em Cotacao";
        public DateTime CriadoEm { get; set; }
        public int? CriadoPorId { get; set; }
        public string? Observacoes { get; set; }

        // Navegação (facilita trabalhar em memória)
        public List<CotacaoConcorrenteCompra> CotacoesConcorrentes { get; set; } = new();
    }
}
