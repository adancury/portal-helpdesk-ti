namespace PortalHelpdeskTI.Models.Relatorios
{
    public class InativacaoMassaStatus
    {
        public int Percent { get; set; }
        public int Processed { get; set; }
        public int Total { get; set; }
        public int Inativados { get; set; }
        public int ErrosCount => Erros?.Count ?? 0;
        public List<string> Erros { get; set; } = new();
        public bool Done { get; set; }
        public string? Error { get; set; }
    }
}