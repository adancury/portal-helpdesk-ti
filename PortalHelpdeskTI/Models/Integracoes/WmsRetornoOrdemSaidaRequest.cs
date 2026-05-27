using System.Text.Json;
using System.Text.Json.Serialization;

namespace PortalHelpdeskTI.Models.Integracoes
{
    public sealed class WmsRetornoOrdemSaidaRequest
    {
        [JsonPropertyName("NUM_ORD_SAI")]
        public int NumOrdSaida { get; set; }

        [JsonPropertyName("COD_PROPRIET")]
        public int CodPropriet { get; set; }

        [JsonPropertyName("FLAG_DIVERGENCIA")]
        public string? FlagDivergencia { get; set; }

        [JsonPropertyName("M3_TOTAL")]
        public decimal M3Total { get; set; }

        [JsonPropertyName("PESO_TOTAL")]
        public decimal PesoTotal { get; set; }

        [JsonPropertyName("VOLUMES_TOTAL")]
        public int VolumesTotal { get; set; }

        [JsonPropertyName("OS_CANCELADA")]
        public string? OsCancelada { get; set; }

        [JsonPropertyName("LISTA_ITENS")]
        public List<WmsRetornoOrdemSaidaItem> ListaItens { get; set; } = new();

        [JsonPropertyName("LISTA_VOLUMES")]
        public List<WmsRetornoOrdemSaidaVolume> ListaVolumes { get; set; } = new();
    }

    public sealed class WmsRetornoOrdemSaidaItem
    {
        [JsonPropertyName("COD_PRODUTO")]
        public string? CodProduto { get; set; }

        [JsonPropertyName("QTDE_EMBAL")]
        public decimal QtdeEmbal { get; set; }

        [JsonPropertyName("QTDE_ATENDIDA")]
        public decimal QtdeAtendida { get; set; }

        [JsonPropertyName("LISTA_LOTES")]
        public List<WmsRetornoOrdemSaidaLote> ListaLotes { get; set; } = new();
    }

    public sealed class WmsRetornoOrdemSaidaLote
    {
        [JsonPropertyName("NUM_LOTE")]
        public string? NumLote { get; set; }

        [JsonPropertyName("DATA_VALIDADE")]
        public DateTime? DataValidade { get; set; }

        [JsonPropertyName("DATA_FABRICACAO")]
        public DateTime? DataFabricacao { get; set; }

        [JsonPropertyName("QTDE_ATENDIDA")]
        public decimal QtdeAtendida { get; set; }
    }

    public sealed class WmsRetornoOrdemSaidaVolume
    {
        [JsonPropertyName("NUM_VOLUME_EXP")]
        public string? NumVolumeExp { get; set; }

        [JsonPropertyName("SEQ_VOLUME")]
        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        public string? SeqVolume { get; set; }

        [JsonPropertyName("TIPO_VOLUME")]
        public string? TipoVolume { get; set; }

        [JsonPropertyName("M3")]
        public decimal M3 { get; set; }

        [JsonPropertyName("PESO")]
        public decimal Peso { get; set; }
    }

    public sealed class FlexibleStringJsonConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => reader.GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture),
                JsonTokenType.True => "true",
                JsonTokenType.False => "false",
                JsonTokenType.Null => null,
                _ => throw new JsonException($"Token {reader.TokenType} nao pode ser convertido para string.")
            };
        }

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (value is null)
                writer.WriteNullValue();
            else
                writer.WriteStringValue(value);
        }
    }
}
