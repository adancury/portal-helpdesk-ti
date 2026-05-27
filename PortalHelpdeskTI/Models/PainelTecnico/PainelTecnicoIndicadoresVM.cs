namespace PortalHelpdeskTI.Models.PainelTecnico
{
    public class PainelTecnicoIndicadoresVM
    {
        public DateTime DataDe { get; set; }
        public DateTime DataAte { get; set; }

        public IndicadoresKpiVM Meu { get; set; } = new();
        public IndicadoresKpiVM Time { get; set; } = new();
        public int PendentesAvaliacao { get; set; } // time-wide

    }

    public class IndicadoresKpiVM
    {
        // SLA (fechados no período)
        public int FechadosDentroSla { get; set; }
        public int FechadosForaSla { get; set; }
        public int TotalFechados { get; set; }
        public decimal PercDentroSla { get; set; } // 0..100

        // Operacional (estado atual)
        public int Abertos { get; set; }
        public int EmAtendimento { get; set; }
        public int Aguardando { get; set; }

        // Qualidade
        public int Reabertos { get; set; } // quantidade de chamados reabertos no período (distinct)
        public decimal? SatisfacaoMedia { get; set; } // 1..5
        public int TotalAvaliacoes { get; set; }

        // Produtividade
        public int FechadosHoje { get; set; }
        public string TempoMedioResolucaoFmt { get; set; } = "-";
        public int EmAndamento { get; set; } // backlog total (status != fechado)
        public int EmAbertoForaSla { get; set; }
        public int ForaSlaTotal { get; set; }

    }
}
