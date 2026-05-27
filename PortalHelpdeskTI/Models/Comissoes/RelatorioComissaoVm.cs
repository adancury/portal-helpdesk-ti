namespace PortalHelpdeskTI.Models.Comissoes
{
    public class RelatorioComissaoVm
    {
        public int SlpCode { get; set; }
        public string SlpName { get; set; } = "";
        public decimal Percentual { get; set; }

        public DateTime DataIni { get; set; }
        public DateTime DataFim { get; set; }

        public decimal ReceitaBruta { get; set; }
        public decimal ReceitaLiquida { get; set; }
        public decimal ComissaoBruta { get; set; }
        public decimal Tributos { get; set; }
        public decimal DescontoCondicionado { get; set; }
        public decimal DescontosRepresentante { get; set; }

        public decimal TotalDescontos => DescontoCondicionado + DescontosRepresentante;

        public decimal ValorReceber => ComissaoBruta
            - Tributos
            - TotalDescontos
            - ComissaoDevolucoes;

        public List<RelatorioComissaoLinhaVm> Linhas { get; set; } = new();
        public List<string> Observacoes { get; set; } = new();
        public List<ComissaoAjuste> DescontosItens { get; set; } = new();

        public decimal ReceitaBrutaDevolucoes { get; set; }
        public decimal ReceitaLiquidaDevolucoes { get; set; }
        public decimal ComissaoDevolucoes { get; set; }

        public List<RelatorioComissaoLinhaVm> Devolucoes { get; set; } = new();
    }

    public class RelatorioComissaoLinhaVm
    {
        public DateTime Data { get; set; }
        public int Nf { get; set; }
        public string ClienteCodigo { get; set; } = "";
        public string ClienteNome { get; set; } = "";

        public decimal VendaBruta { get; set; }         // RBV
        public decimal VendaLiquida { get; set; }       // RLV
        public decimal DescontoMedioPct { get; set; }   // Ex.: 10 = 10%
        public decimal Percentual { get; set; }         // decimal (0.0449)
        public decimal Comissao { get; set; }           // RLV * Percentual (2 casas)
    }

    public class ResumoComissaoVm
    {
        public int Ano { get; set; }
        public int Mes { get; set; }

        public DateTime DataIni { get; set; }
        public DateTime DataFim { get; set; }

        public List<ResumoComissaoLinhaVm> Linhas { get; set; } = new();
    }

    public class ResumoComissaoLinhaVm
    {
        public int SlpCode { get; set; }
        public string SlpName { get; set; } = "";
        public string TipoVendedor { get; set; } = "";
        public string BaseCalculo { get; set; } = "";

        public decimal ReceitaLiquida { get; set; }
        public decimal ComissaoBruta { get; set; }
        public decimal Tributos { get; set; }

        // Aqui será: DescontosRepresentante + DescontoCondicionado (para bater com o Detalhes)
        public decimal Descontos { get; set; }

        // NOVO: devoluções (débito)
        public decimal ComissaoDevolucoes { get; set; }

        public decimal ValorReceber =>
            ComissaoBruta - Tributos - Descontos - ComissaoDevolucoes;

        public int Mes { get; set; }
        public decimal ValorBruto { get; set; }
        public decimal IR { get; set; }
        public decimal ValorLiq { get; set; }
        public decimal ValorComissao { get; set; }


    }
}
