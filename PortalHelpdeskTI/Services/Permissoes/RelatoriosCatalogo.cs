namespace PortalHelpdeskTI.Services.Permissoes
{
    public sealed class RelatorioCatalogoItem
    {
        public string Key { get; init; } = "";
        public string Titulo { get; init; } = "";
        public string Descricao { get; init; } = "";
        public string Departamento { get; init; } = "";
        public string Formato { get; init; } = "Tela";
        public List<string> Tags { get; init; } = new();
        public string? UrlVisualizar { get; init; }
        public string? UrlDownload { get; init; }
    }

    public static class RelatoriosCatalogo
    {
        public static IReadOnlyList<RelatorioCatalogoItem> Todos => new List<RelatorioCatalogoItem>
        {
            new()
            {
                Key = "DADOS_PRODUTOS",
                Titulo = "Dados Produtos",
                Descricao = "Informações cadastrais dos itens de revenda",
                Departamento = "Produtos",
                Formato = "PDF",
                Tags = new() { "Produtos", "PDF" },
                UrlVisualizar = "~/Relatorios/DadosProdutos"
            },
            new()
            {
                Key = "REPRESENTANTES",
                Titulo = "Representantes",
                Descricao = "Lista de representantes de Vendas",
                Departamento = "Comercial",
                Formato = "XLSX",
                Tags = new() { "Comercial", "XLSX" },
                UrlVisualizar = "~/Relatorios/RepresentantesVendas"
            },
            new()
            {
                Key = "MUDANCA_CARTEIRA",
                Titulo = "Mudança de Carteira / Inativação de PN",
                Descricao = "Transfira carteira & Inativar clientes a partir de planilha Excel",
                Departamento = "Comercial",
                Formato = "Tela",
                Tags = new() { "Comercial", "Tela" },
                UrlVisualizar = "~/Relatorios/MudancaCarteira/ServiceLayer"
            },
            new()
            {
                Key = "RUPTURAS_HISTORICO",
                Titulo = "Rupturas - Histórico",
                Descricao = "Dados de rupturas ao longo do período",
                Departamento = "Análises",
                Formato = "Tela",
                Tags = new() { "Análises", "Tela" },
                UrlVisualizar = "~/Relatorios/HistoricoRupturas"
            },
            new()
            {
                Key = "RUPTURAS_PREVISAO",
                Titulo = "Rupturas - Previsão",
                Descricao = "Dados de rupturas futuras",
                Departamento = "Análises",
                Formato = "Tela",
                Tags = new() { "Análises", "Tela" },
                UrlVisualizar = "~/Relatorios/PrevisaoRuptura"
            },
            new()
            {
                Key = "STATUS_INDICADOR",
                Titulo = "Status de Indicador",
                Descricao = "Histórico por Pedido",
                Departamento = "Análises",
                Formato = "Tela",
                Tags = new() { "Análises", "Tela" },
                UrlVisualizar = $"~/Relatorios/StatusIndicador?de={DateTime.Today:yyyy-MM-dd}"
            },
            new()
            {
                Key = "COLABORADORES",
                Titulo = "Colaboradores",
                Descricao = "Lista de Colaboradores",
                Departamento = "RH",
                Formato = "XLSX",
                Tags = new() { "RH", "XLSX" },
                UrlVisualizar = "~/Relatorios/CadColaboradores"
            },
            new()
            {
                Key = "DASH_LIBERACAO_PEDIDOS",
                Titulo = "Liberação de Pedidos",
                Descricao = "Acompanhar as liberações",
                Departamento = "Comercial",
                Formato = "Tela",
                Tags = new() { "Comercial", "Tela" },
                UrlVisualizar = "~/Relatorios/DashboardLiberacaoPedidos"
            },
            new()
            {
                Key = "DASH_SAVING_COMPRAS",
                Titulo = "Pedidos de Compras",
                Descricao = "Solicitações, Pedidos, Pendências, etc.",
                Departamento = "Comercial",
                Formato = "Tela",
                Tags = new() { "Comercial", "Tela" },
                UrlVisualizar = "~/Relatorios/DashboardSavingCompras"
            },
            new()
            {
                Key = "COMISSOES",
                Titulo = "Comissão de Vendas",
                Descricao = "Representantes, Vendedores Internos e Externos",
                Departamento = "Comercial",
                Formato = "Tela",
                Tags = new() { "Comercial", "Tela" },
                UrlVisualizar = "~/Comissoes/Index"
            },
            new()
            {
                Key = "RELATORIO_TEMPO",
                Titulo = "Relatório de Tempo",
                Descricao = "Tempo médio e SLA por chamados",
                Departamento = "TI",
                Formato = "Tela",
                Tags = new() { "TI", "Tela" },
                UrlVisualizar = "~/Relatorios/Tempo"
            },
            new()
            {
                Key = "INDICADOR_TI",
                Titulo = "Indicador TI",
                Descricao = "Indicador mensal de atendimento de TI",
                Departamento = "TI",
                Formato = "Tela",
                Tags = new() { "TI", "Tela" },
                UrlVisualizar = "~/Relatorios/IndicadorTi"
            },
            new()
            {
                Key = "REDISTRIBUICAO_CARTEIRA",
                Titulo = "Redistribuição de Carteira",
                Descricao = "Automatizar a carteira de inativos e leads",
                Departamento = "Comercial",
                Formato = "Tela",
                Tags = new() { "Comercial", "Tela" },
                UrlVisualizar = "~/RedistribuicaoCarteira/Index"
            },
            new()
            {
                Key = "REDISTRIBUICAO_CARTEIRA_APLICAR",
                Titulo = "Redistribuição de Carteira - Aplicar",
                Descricao = "Permite aplicar a redistribuição de carteira no SAP",
                Departamento = "Comercial",
                Formato = "Ação",
                Tags = new() { "Comercial", "Ação" },
                UrlVisualizar = null
            }
        };
    }
}
