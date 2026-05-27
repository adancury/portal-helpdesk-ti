using System.Text.Json.Serialization;

namespace PortalHelpdeskTI.Models.Integracoes
{
    public class EfetivarEsbocoDevNfSaidaRequest
    {
        public long NumOrdEntrada { get; set; }

        public int NumEsboco { get; set; }

        public string? NumPedido { get; set; }

        public int CodPropriet { get; set; }

        public string? CodEmitente { get; set; }
        public string? Tipo { get; set; }

        public string? FlagDivergencia { get; set; }

        public string? SerieNF { get; set; }
        public int NumNF { get; set; }
        public string? DataEmissaoNF { get; set; }
        public decimal ValorNF { get; set; }
        public string? DataEntrada { get; set; }

        public List<ItemOrdemEntradaDto> ListaItemOrdemEntrada { get; set; } = new();

        [JsonPropertyName("NUM_ORD_ENT")]
        public long NumOrdEntradaWms
        {
            get => NumOrdEntrada;
            set => NumOrdEntrada = value;
        }

        [JsonPropertyName("NUM_ESBOCO")]
        public int NumEsbocoWms
        {
            get => NumEsboco;
            set => NumEsboco = value;
        }

        [JsonPropertyName("COD_PROPRIET")]
        public int CodProprietWms
        {
            get => CodPropriet;
            set => CodPropriet = value;
        }

        [JsonPropertyName("FLAG_DIVERGENCIA")]
        public string? FlagDivergenciaWms
        {
            get => FlagDivergencia;
            set => FlagDivergencia = value;
        }

        [JsonPropertyName("LISTA_ITENS")]
        public List<ItemOrdemEntradaDto> ListaItensWms
        {
            get => ListaItemOrdemEntrada;
            set => ListaItemOrdemEntrada = value ?? new();
        }
    }

    public class ItemOrdemEntradaDto
    {
        [JsonPropertyName("COD_PRODUTO")]
        public string? CodProduto { get; set; }

        [JsonPropertyName("QTDE_RECEBIDA")]
        public decimal Qtde { get; set; }

        [JsonPropertyName("LISTA_LOTES")]
        public List<LoteOrdemEntradaDto> ListaLoteOrdemEntrada { get; set; } = new();
    }

    public class LoteOrdemEntradaDto
    {
        [JsonPropertyName("NUM_LOTE")]
        public string? NumLote { get; set; }

        [JsonPropertyName("DATA_VALIDADE")]
        public DateTime? DataValidade { get; set; }

        [JsonPropertyName("DATA_FABRICACAO")]
        public DateTime? DataFabricacao { get; set; }

        [JsonPropertyName("QTDE_RECEBIDA")]
        public decimal Qtde { get; set; }
    }
}
