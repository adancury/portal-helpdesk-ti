using System;
using System.Collections.Generic;
using System.Linq;

namespace PortalHelpdeskTI.Helpers
{
    // <-- classe top-level, NÃO aninhada
    public class StatusChange { public DateTime When { get; set; } public string Status { get; set; } = ""; }

    public class SlaCalcResult
    {
        public TimeSpan ElapsedUseful { get; set; }
        public TimeSpan Target { get; set; }
        public int Percent { get; set; }
        public bool Paused { get; set; }
        public string Label { get; set; } = "";
        public string CssClass =>
            Percent >= 100 ? "bg-danger" :
            Percent >= 75 ? "bg-warning" : "bg-success";
    }

    public static class SlaCalculator
    {
        public static List<(DateTime PauseStart, DateTime PauseEnd)> BuildAguardandoPauses(
            DateTime start,
            DateTime endForOpenIntervals,
            IReadOnlyList<StatusChange> timelineOrdered)
        {
            var pauses = new List<(DateTime, DateTime)>();
            if (timelineOrdered.Count == 0) return pauses;

            string curStatus = timelineOrdered[0].Status;
            DateTime? curPauseStart = null;

            if (IsAguardando(curStatus))
                curPauseStart = timelineOrdered[0].When;

            for (int i = 1; i < timelineOrdered.Count; i++)
            {
                var prev = timelineOrdered[i - 1];
                var cur = timelineOrdered[i];

                if (!string.Equals(prev.Status, cur.Status, StringComparison.OrdinalIgnoreCase))
                {
                    if (IsAguardando(prev.Status) && curPauseStart.HasValue)
                    {
                        var s = curPauseStart.Value;
                        var e = cur.When;
                        if (e > s) pauses.Add((s, e));
                        curPauseStart = null;
                    }
                    if (IsAguardando(cur.Status))
                        curPauseStart = cur.When;
                }
                curStatus = cur.Status;
            }

            if (curPauseStart.HasValue)
            {
                var s = curPauseStart.Value;
                var e = endForOpenIntervals;
                if (e > s) pauses.Add((s, e));
            }
            return pauses;
        }

        public static SlaCalcResult Compute(
            DateTime abertura,
            DateTime fimMedicao,
            TimeSpan workdayStart,
            TimeSpan workdayEnd,
            ISet<DateTime>? feriados,
            TimeSpan alvoSla,
            IReadOnlyList<StatusChange> timeline)
        {
            var pauses = BuildAguardandoPauses(abertura, fimMedicao, timeline);
            var elapsed = BusinessTime.CalculateWithPauses(
                abertura, fimMedicao, workdayStart, workdayEnd, feriados ?? new HashSet<DateTime>(), pauses);

            var percent = alvoSla.TotalSeconds <= 0 ? 0
                : (int)Math.Min(100, Math.Round(elapsed.TotalSeconds / alvoSla.TotalSeconds * 100.0));

            var statusAtual = timeline[^1].Status;
            var paused = IsAguardando(statusAtual);

            return new SlaCalcResult
            {
                ElapsedUseful = elapsed,
                Target = alvoSla,
                Percent = percent,
                Paused = paused,
                Label = $"{BusinessTime.ToFriendly(elapsed)} de {BusinessTime.ToFriendly(alvoSla)}"
            };
        }

        private static bool IsAguardando(string? s)
            => string.Equals(s ?? "", "Aguardando", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s ?? "", "Aguardando retorno", StringComparison.OrdinalIgnoreCase);
    }
}
