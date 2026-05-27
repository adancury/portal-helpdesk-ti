using System.ComponentModel.DataAnnotations;

namespace PortalHelpdeskTI.Models.IntegracoesWmsDados
{
    public class WmsProcesso
    {
        public int Id { get; set; }

        [MaxLength(40)]
        public string Tipo { get; set; } = "";

        [MaxLength(120)]
        public string ChaveProcesso { get; set; } = "";

        [MaxLength(220)]
        public string ChaveItem { get; set; } = "";

        [MaxLength(80)]
        public string? Status { get; set; }

        [MaxLength(80)]
        public string? StatusAnterior { get; set; }

        public DateTime? DataReferencia { get; set; }

        [MaxLength(40)]
        public string? CodProprietario { get; set; }

        [MaxLength(180)]
        public string? NomeProprietario { get; set; }

        [MaxLength(80)]
        public string? NumeroDocumento { get; set; }

        [MaxLength(80)]
        public string? NumeroPedido { get; set; }

        [MaxLength(80)]
        public string? CodigoProduto { get; set; }

        [MaxLength(260)]
        public string? DescricaoProduto { get; set; }

        [MaxLength(180)]
        public string? ClienteFornecedor { get; set; }

        [MaxLength(120)]
        public string? UsuarioResponsavel { get; set; }

        public decimal? QuantidadePrevista { get; set; }
        public decimal? QuantidadeExecutada { get; set; }
        public decimal? QuantidadeDivergente { get; set; }

        [MaxLength(96)]
        public string PayloadHash { get; set; } = "";

        public string RawJson { get; set; } = "";

        public DateTime CriadoEm { get; set; }
        public DateTime AtualizadoEm { get; set; }
        public DateTime UltimaSincronizacaoEm { get; set; }

        public List<WmsProcessoLog> Logs { get; set; } = new();
    }
}
