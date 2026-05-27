namespace PortalHelpdeskTI.Views.Relatorios
{
    public sealed class ReportsIndexVm
    {
        public List<string> Categorias { get; init; } = new();
        public List<ReportItemVm> Itens { get; init; } = new();
        public string? CategoriaAtiva { get; init; }
        public string? Busca { get; init; }

        public sealed class ReportItemVm
        {
            public string Key { get; set; } = "";
            public string Titulo { get; set; } = "";
            public string Descricao { get; set; } = "";
            public string Departamento { get; set; } = "";
            public List<string> Tags { get; set; } = new();
            public string Formato { get; set; } = "Tela";

            public string? UrlVisualizar { get; set; }
            public string? UrlDownload { get; set; }

            public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
            public bool Favorito { get; set; }

            // ============================
            // KPI (para cards com indicador)
            // ============================
            public double? KpiUltimoValorPct { get; set; }   // ex: 97.35
            public string? KpiUltimoMes { get; set; }        // ex: "02/2026"
            public bool? KpiAtingiuMeta { get; set; }        // true/false
            public string? KpiBadgeTexto { get; set; }       // "Meta 95% atingida"
        }
    }
}
