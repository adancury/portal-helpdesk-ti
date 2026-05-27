using System;
using System.Collections.Generic;

namespace PortalHelpdeskTI.Helpers
{
    public static class BusinessTime
    {
        /// <summary>
        /// Calcula tempo útil entre start e end, somando apenas seg–sex dentro de [workdayStart, workdayEnd].
        /// Feriados são DateTime com .Date (sem hora).
        /// </summary>
        public static TimeSpan Calculate(
            DateTime start,
            DateTime end,
            TimeSpan workdayStart,
            TimeSpan workdayEnd,
            ISet<DateTime>? holidays = null)
        {
            if (end <= start) return TimeSpan.Zero;
            if (workdayEnd <= workdayStart) throw new ArgumentException("workdayEnd must be > workdayStart");

            TimeSpan total = TimeSpan.Zero;
            DateTime d = start.Date;
            DateTime last = end.Date;

            while (d <= last)
            {
                if (IsBusinessDay(d, holidays))
                {
                    var dayStart = new DateTime(d.Year, d.Month, d.Day, workdayStart.Hours, workdayStart.Minutes, workdayStart.Seconds);
                    var dayEnd = new DateTime(d.Year, d.Month, d.Day, workdayEnd.Hours, workdayEnd.Minutes, workdayEnd.Seconds);

                    var chunkStart = (start > dayStart) ? start : dayStart;
                    var chunkEnd = (end < dayEnd) ? end : dayEnd;

                    if (chunkEnd > chunkStart)
                        total += (chunkEnd - chunkStart);
                }
                d = d.AddDays(1);
            }
            return total;
        }

        public static string ToFriendly(TimeSpan ts)
        {
            if (ts.TotalMinutes < 1) return $"{ts.Seconds}s";
            if (ts.TotalHours < 1) return $"{(int)ts.TotalMinutes}m";
            var h = (int)ts.TotalHours;
            var m = ts.Minutes;
            return m > 0 ? $"{h}h {m}m" : $"{h}h";
        }

        private static bool IsBusinessDay(DateTime date, ISet<DateTime>? holidays)
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
            if (holidays != null && holidays.Contains(date.Date)) return false;
            return true;
        }

        /// <summary>
        /// Calcula o tempo útil total e subtrai as pausas informadas (cada pausa é um intervalo DateTime).
        /// </summary>
        public static TimeSpan CalculateWithPauses(
            DateTime start,
            DateTime end,
            TimeSpan workdayStart,
            TimeSpan workdayEnd,
            ISet<DateTime>? holidays,
            IEnumerable<(DateTime PauseStart, DateTime PauseEnd)> pauses)
        {
            var total = Calculate(start, end, workdayStart, workdayEnd, holidays);
            foreach (var (ps, pe) in pauses)
            {
                var s = Max(start, ps);
                var e = Min(end, pe);
                if (e > s)
                {
                    var overlap = Calculate(s, e, workdayStart, workdayEnd, holidays);
                    total -= overlap;
                }
            }
            return total < TimeSpan.Zero ? TimeSpan.Zero : total;
        }

        public static (DateTime, DateTime) Clamp(DateTime s, DateTime e, DateTime min, DateTime max)
            => (s < min ? min : s, e > max ? max : e);

        private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
        private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
    }
}
