namespace PortalHelpdeskTI.Models.Relatorios
{
    public class IndicadorTiDetalheDto
    {
        public int ChamadoId { get; set; }
        public string? Titulo { get; set; }
        public string? Solicitante { get; set; }
        public string? Tecnico { get; set; }

        public string? Categoria { get; set; }
        public string? Subcategoria { get; set; }

        public DateTime DataAbertura { get; set; }
        public DateTime DataConclusao { get; set; }

        public double HorasEstimadas { get; set; }
        public double HorasReais { get; set; }

        public double PerformancePct { get; set; }
        public double PerformancePctCap110 { get; set; }

        public double Nota_1a5 { get; set; }
        public double SatisfacaoPct { get; set; }
        public string? ComentarioAvaliacao { get; set; }
    }
}
