namespace PortalHelpdeskTI.Views.Relatorios
{
    public sealed class ReportItemVm
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Titulo { get; init; } = "";
        public string Descricao { get; init; } = "";
        public string Departamento { get; init; } = ""; // ex.: "Financeiro", "Comercial"
        public List<string> Tags { get; init; } = new(); // ex.: ["PDF", "KPI"]
        public string? UrlVisualizar { get; init; }      // se tiver uma rota para abrir no portal
        public string? UrlDownload { get; init; }        // link direto pro arquivo (PDF/XLSX)
        public string Formato { get; init; } = "PDF";    // "PDF", "XLSX", etc.
        public DateTime AtualizadoEm { get; init; } = DateTime.UtcNow;
        public bool Favorito { get; init; } = false;
    }
}
