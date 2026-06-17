using PortalHelpdeskTI.Models.IntegracoesWmsDados;

namespace PortalHelpdeskTI.ViewModels.IntegracoesWms
{
    public class WmsProcessosFiltroVm
    {
        public string? Tipo { get; set; }
        public string? Status { get; set; }
        public string? Texto { get; set; }
        public DateTime? DataIni { get; set; } = DateTime.Today.AddDays(-7);
        public DateTime? DataFim { get; set; } = DateTime.Today;
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public bool AutoRefresh { get; set; } = true;
        public string OrdenarPor { get; set; } = "referencia";
        public string Direcao { get; set; } = "desc";
    }

    public class WmsProcessosIndexVm
    {
        public WmsProcessosFiltroVm Filtro { get; set; } = new();
        public List<WmsProcesso> Itens { get; set; } = new();
        public List<WmsProcessosGrupoVm> Grupos { get; set; } = new();
        public bool ExibirAgrupado { get; set; }
        public int Total { get; set; }
        public List<string> Tipos { get; set; } = new();
        public List<string> Status { get; set; } = new();
        public List<WmsSyncExecucao> UltimasExecucoes { get; set; } = new();
        public int TotalAbertos { get; set; }
        public int TotalHoje { get; set; }
        public int TotalAlterados24h { get; set; }
        public int TotalErrosSync24h { get; set; }
    }

    public class WmsProcessosGrupoVm
    {
        public string Tipo { get; set; } = "";
        public string ChaveProcesso { get; set; } = "";
        public string? Status { get; set; }
        public DateTime? DataReferencia { get; set; }
        public string? NumeroPedido { get; set; }
        public string? NumeroDocumento { get; set; }
        public string TipoMovimentacao { get; set; } = "";
        public string TipoDocumento { get; set; } = "";
        public int TotalItens { get; set; }
        public decimal? QuantidadePrevista { get; set; }
        public decimal? QuantidadeExecutada { get; set; }
        public decimal? QuantidadeDivergente { get; set; }
        public DateTime AtualizadoEm { get; set; }
        public List<WmsProcessosStatusResumoVm> StatusResumo { get; set; } = new();
        public List<WmsProcesso> Itens { get; set; } = new();
    }

    public class WmsProcessosStatusResumoVm
    {
        public string Status { get; set; } = "";
        public string Descricao { get; set; } = "";
        public int Quantidade { get; set; }
    }

    public class WmsProcessoDetalheVm
    {
        public WmsProcesso Processo { get; set; } = new();
        public List<WmsProcessoLog> Logs { get; set; } = new();
        public string TipoMovimentacao { get; set; } = "";
        public string TipoDocumento { get; set; } = "";
    }
}
