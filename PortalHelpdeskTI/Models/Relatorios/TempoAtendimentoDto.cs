namespace PortalHelpdeskTI.Models.Relatorios
{
    /*public class TempoAtendimentoDto
    {
        public int ChamadoId { get; set; }
        public string? Titulo { get; set; }
        public string? Solicitante { get; set; }
        public string? Tecnico { get; set; }
        public DateTime? DataAbertura { get; set; }
        public DateTime? DataConclusao { get; set; }

        public TimeSpan TempoBruto { get; set; }     // fim - início (sem regras)
        public TimeSpan TempoUtil { get; set; }      // horas úteis - pausas
        public TimeSpan TempoPausado { get; set; }   // horas úteis dentro de pausas (aguardando retorno)

        public string TempoBrutoFmt => ToFriendly(TempoBruto);
        public string TempoUtilFmt => ToFriendly(TempoUtil);
        public string TempoPausadoFmt => ToFriendly(TempoPausado);

        private static string ToFriendly(TimeSpan ts)
        {
            if (ts.TotalMinutes < 1) return $"{ts.Seconds}s";
            if (ts.TotalHours < 1) return $"{(int)ts.TotalMinutes}m";
            var h = (int)ts.TotalHours;
            var m = ts.Minutes;
            return m > 0 ? $"{h}h {m}m" : $"{h}h";
        }
    }*/
    public class TempoAtendimentoDto
    {
        public int ChamadoId { get; set; }
        public string? Titulo { get; set; }
        public string? Solicitante { get; set; }
        public string? Tecnico { get; set; }
        public DateTime? DataAbertura { get; set; }
        public DateTime? DataConclusao { get; set; }

        public TimeSpan TempoBruto { get; set; }     // fim - início (sem regras)
        public TimeSpan TempoUtil { get; set; }      // horas úteis - pausas
        public TimeSpan TempoPausado { get; set; }   // horas úteis dentro de pausas (aguardando retorno)

        // === NOVOS CAMPOS (SLA) ===
        public int SlaPercent { get; set; }              // 0..100 (mesma régua da progressbar)
        public bool SlaDentroPrazo { get; set; }         // (concluído) TempoUtil <= alvo SLA
        public string SlaResumo { get; set; } = "";      // ex.: "3h 20m de 4h"

        // === FORMATADOS (já existentes) ===
        public string TempoBrutoFmt => ToFriendly(TempoBruto);
        public string TempoUtilFmt => ToFriendly(TempoUtil);
        public string TempoPausadoFmt => ToFriendly(TempoPausado);

        private static string ToFriendly(TimeSpan ts)
        {
            if (ts.TotalMinutes < 1) return $"{ts.Seconds}s";
            if (ts.TotalHours < 1) return $"{(int)ts.TotalMinutes}m";
            var h = (int)ts.TotalHours;
            var m = ts.Minutes;
            return m > 0 ? $"{h}h {m}m" : $"{h}h";
        }
    }
}
