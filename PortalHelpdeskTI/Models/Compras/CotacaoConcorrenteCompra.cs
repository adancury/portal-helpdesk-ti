namespace PortalHelpdeskTI.Models.Compras
{
    public class CotacaoConcorrenteCompra
    {
        public int Id { get; set; }

        public int ProcessoCompraId { get; set; }

        public string CardCodeFornecedor { get; set; } = string.Empty;
        public string CardNameFornecedor { get; set; } = string.Empty;
        public decimal ValorCotacaoTotal { get; set; }

        public int? PrazoEntregaDias { get; set; }
        public string? CondicaoPagamento { get; set; }
        public string? Observacoes { get; set; }

        public DateTime CriadoEm { get; set; }
        public int? CriadoPorId { get; set; }

        // (Opcional) Referência ao processo em memória
        public ProcessoCompra? Processo { get; set; }
    }
}
