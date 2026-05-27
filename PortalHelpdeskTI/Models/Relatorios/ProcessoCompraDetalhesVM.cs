using System;
using System.Collections.Generic;

namespace PortalHelpdeskTI.Models.Relatorios
{
    public class CotacaoConcorrenteLinhaVM
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
    }
    public class ItemSolicitacaoVM
    {
        public string? Description { get; set; }
        public string? DescricaoNFSe { get; set; }     // U_TX_DescmatNfse
        public string? Comentarios { get; set; }       // Comments (linha)
    }
    public class ProcessoCompraDetalhesVM
    {
        public ProcessoCompraLinhaVM Processo { get; set; } = new ProcessoCompraLinhaVM();
        public List<CotacaoConcorrenteLinhaVM> Cotacoes { get; set; } = new List<CotacaoConcorrenteLinhaVM>();
        
        // Itens da Solicitação (PRQ1)
        public List<ItemSolicitacaoVM> ItensSolicitacao { get; set; } = new List<ItemSolicitacaoVM>();
        // Para novo cadastro via formulário
        public CotacaoConcorrenteLinhaVM NovaCotacao { get; set; } = new CotacaoConcorrenteLinhaVM();
    }
}
