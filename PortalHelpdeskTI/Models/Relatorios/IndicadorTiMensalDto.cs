namespace PortalHelpdeskTI.Models.Relatorios
{
    public class IndicadorTiMensalDto
    {
        public DateTime MesRef { get; set; } // 1º dia do mês

        public int QtdeChamados { get; set; }

        public double HorasEstimadasTotal { get; set; }
        public double HorasReaisTotal { get; set; }

        public double PerformancePct { get; set; }        // (Estimado/Real)*100
        public double PerformancePctCap110 { get; set; }  // teto 110

        public double NotaMedia_1a5 { get; set; }
        public double SatisfacaoPct { get; set; }         // (Nota/5)*100

        public double IndicadorFinal { get; set; }        // 80/20
        public bool AtingiuMeta95 => IndicadorFinal >= 95.0;
    }
}
