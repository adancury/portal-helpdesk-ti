namespace PortalHelpdeskTI.Models.Relatorios
{
    public class ComissaoExtraVm
    {
        public int Ano { get; set; }
        public int Trimestre { get; set; }

        public string? TipoVendedor { get; set; }
        public List<string> TiposVendedor { get; set; } = new();

        public List<ComissaoExtraLinhaVm> Linhas { get; set; } = new();
    }

    public class ComissaoExtraLinhaVm
    {
        public int ComissaoVendedorId { get; set; }
        public int SlpCode { get; set; }
        public string SlpName { get; set; } = "";
        public string? TipoVendedor { get; set; }

        public int Ano { get; set; }
        public int Trimestre { get; set; }

        public decimal Meta { get; set; }
        public decimal Realizado { get; set; }

        public decimal? DesvioPercentual { get; set; }
        public decimal DesvioValor { get; set; }

        // Colunas “visuais” (igual seu print)
        public bool Atingiu05 { get; set; }
        public bool Atingiu07 { get; set; }
        public bool Atingiu10 { get; set; }

        public decimal Valor05 { get; set; }
        public decimal Valor07 { get; set; }
        public decimal Valor10 { get; set; }

        // Resultado final (não cumulativo)
        public decimal? PercentualAplicado { get; set; }
        public decimal ComissaoExtraValor { get; set; }
    }
}
