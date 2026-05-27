using Microsoft.EntityFrameworkCore;
using PortalHelpdeskTI.Models;
using PortalHelpdeskTI.Models.Relatorios;

namespace PortalHelpdeskTI.Services.Relatorios
{
    public class RelatorioTempoService
    {
        private readonly AppDbContext _db;
        public RelatorioTempoService(AppDbContext db) => _db = db;

        // Gera relatório entre datas (filtra pela conclusão dentro do intervalo)
        public async Task<List<TempoAtendimentoDto>> GerarAsync(DateTime? de, DateTime? ate)
        {
            // Normaliza range com guardas contra overflow
            DateTime start;
            DateTime end;

            if (de.HasValue && ate.HasValue && de.Value.Date > ate.Value.Date)
            {
                // se o usuário inverteu as datas, corrige
                var tmp = de.Value;
                de = ate;
                ate = tmp;
            }

            start = de?.Date ?? DateTime.MinValue.Date;

            if (ate.HasValue)
            {
                // fim do dia "ate": 23:59:59.9999999 (sem overflow)
                // AddDays(1) aqui é seguro porque ate.HasValue e não é MaxValue
                var ateDate = ate.Value.Date;
                end = ateDate.AddDays(1).AddTicks(-1);
            }
            else
            {
                // sem data final: usa MaxValue diretamente (sem AddDays)
                end = DateTime.MaxValue;
            }

            // Busca chamados concluídos no intervalo (inclusivo)
            var chamados = await _db.Chamados
                .Include(c => c.Usuario)
                .Include(c => c.Tecnico)
                .Where(c => c.DataConclusao != null &&
                            c.DataConclusao >= start &&
                            c.DataConclusao <= end)
                .OrderBy(c => c.DataConclusao)
                .ToListAsync();

            var itens = new List<TempoAtendimentoDto>();

            foreach (var c in chamados)
            {
                var inicio = c.DataAbertura;
                var fim = c.DataConclusao!.Value;

                // feriados (fixos + móveis) para todos anos no intervalo
                var holidays = new HashSet<DateTime>();
                for (int y = inicio.Year; y <= fim.Year; y++)
                {
                    foreach (var d in FeriadosFixosBrasil(y)) holidays.Add(d.Date);
                    foreach (var d in FeriadosMoveisBrasil(y)) holidays.Add(d.Date);
                }

                // pausas (Aguardando retorno) dentro do recorte
                var pausas = await MontarPausasAsync(c.Id, inicio, fim);

                var jornadaIni = new TimeSpan(9, 0, 0);
                var jornadaFim = new TimeSpan(17, 0, 0);

                var tempoBruto = fim - inicio;
                var tempoUtil = CalcularTempoUtil(inicio, fim, jornadaIni, jornadaFim, holidays);
                var tempoPausado = TimeSpan.Zero;

                foreach (var (ps, pe) in pausas)
                {
                    var (s, e) = Clamp(ps, pe, inicio, fim);
                    if (e > s)
                        tempoPausado += CalcularTempoUtil(s, e, jornadaIni, jornadaFim, holidays);
                }

                tempoUtil -= tempoPausado;
                if (tempoUtil < TimeSpan.Zero) tempoUtil = TimeSpan.Zero;

                // alvo de SLA por prioridade (mesma regra do painel)
                TimeSpan AlvoPorPrioridade(string? p) => p switch
                {
                    "Urgente" => TimeSpan.FromHours(1),
                    "Alta" => TimeSpan.FromHours(2),
                    _ => TimeSpan.FromHours(4),
                };
                var alvoSla = AlvoPorPrioridade(c.Prioridade);

                // percent e “dentro do prazo”
                var percent = alvoSla.TotalSeconds > 0
                    ? (int)Math.Min(100, Math.Round(tempoUtil.TotalSeconds / alvoSla.TotalSeconds * 100.0))
                    : 0;

                // como este relatório lista concluídos, “dentro do prazo” = útil <= alvo
                var dentroPrazo = tempoUtil <= alvoSla;

                // resumo amigável
                string ToFriendly(TimeSpan ts)
                {
                    if (ts.TotalMinutes < 1) return $"{ts.Seconds}s";
                    if (ts.TotalHours < 1) return $"{(int)ts.TotalMinutes}m";
                    var h = (int)ts.TotalHours;
                    var m = ts.Minutes;
                    return m > 0 ? $"{h}h {m}m" : $"{h}h";
                }
                var resumo = $"{ToFriendly(tempoUtil)} de {ToFriendly(alvoSla)}";

                itens.Add(new TempoAtendimentoDto
                {
                    ChamadoId = c.Id,
                    Titulo = c.Titulo,
                    Solicitante = c.Usuario?.Nome,
                    Tecnico = c.Tecnico?.Nome,
                    DataAbertura = c.DataAbertura,
                    DataConclusao = c.DataConclusao,
                    TempoBruto = tempoBruto,
                    TempoUtil = tempoUtil,
                    TempoPausado = tempoPausado,
                    SlaPercent = percent,
                    SlaDentroPrazo = dentroPrazo,
                    SlaResumo = resumo
                });
            }

            return itens;
        }


        // ===== helpers =====

        /*private async Task<List<(DateTime Ini, DateTime Fim)>> MontarPausasAsync(int chamadoId, DateTime start, DateTime end)
        {
            var logs = await _db.ChamadoStatusLogs
                .Where(l => l.ChamadoId == chamadoId && l.DataHora <= end)
                .OrderBy(l => l.DataHora)
                .ToListAsync();

            var anterior = await _db.ChamadoStatusLogs
                .Where(l => l.ChamadoId == chamadoId && l.DataHora < start)
                .OrderByDescending(l => l.DataHora)
                .FirstOrDefaultAsync();

            string statusAtual = anterior?.Status ?? "Aberto";
            var pausas = new List<(DateTime Ini, DateTime Fim)>();
            DateTime? pausaAberta = null;

            if (string.Equals(statusAtual, "Aguardando retorno", StringComparison.OrdinalIgnoreCase))
                pausaAberta = start;

            foreach (var log in logs)
            {
                var novo = log.Status ?? "";
                if (!string.Equals(statusAtual, novo, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(statusAtual, "Aguardando retorno", StringComparison.OrdinalIgnoreCase) && pausaAberta.HasValue)
                    {
                        var (s, e) = Clamp(pausaAberta.Value, log.DataHora, start, end);
                        if (e > s) pausas.Add((s, e));
                        pausaAberta = null;
                    }
                    if (string.Equals(novo, "Aguardando retorno", StringComparison.OrdinalIgnoreCase))
                    {
                        pausaAberta = log.DataHora < start ? start : log.DataHora;
                    }
                    statusAtual = novo;
                }
            }
            if (string.Equals(statusAtual, "Aguardando retorno", StringComparison.OrdinalIgnoreCase) && pausaAberta.HasValue)
            {
                var (s, e) = Clamp(pausaAberta.Value, end, start, end);
                if (e > s) pausas.Add((s, e));
            }
            return pausas;
        }*/
        private static bool IsAguardando(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;

            // normalize
            s = s.Trim();

            // trate as variantes que você usa
            // (se quiser ser mais permissivo, pode usar Contains("Aguardando", ...))
            return
                s.Equals("Aguardando", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("Aguardando retorno", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("Aguardando Resposta", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("Aguardando Resposta do Usuário", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("Aguardando Resposta do Usuario", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<List<(DateTime Ini, DateTime Fim)>> MontarPausasAsync(
            int chamadoId, DateTime start, DateTime end)
        {
            var logs = await _db.ChamadoStatusLogs
                .Where(l => l.ChamadoId == chamadoId && l.DataHora <= end)
                .OrderBy(l => l.DataHora)
                .ToListAsync();

            // status imediatamente antes do recorte
            var anterior = await _db.ChamadoStatusLogs
                .Where(l => l.ChamadoId == chamadoId && l.DataHora < start)
                .OrderByDescending(l => l.DataHora)
                .FirstOrDefaultAsync();

            string statusAtual = anterior?.Status ?? "Aberto";

            var pausas = new List<(DateTime Ini, DateTime Fim)>();
            DateTime? pausaAberta = null;

            // se já entra no recorte em "Aguardando..." abre a pausa no 'start'
            if (IsAguardando(statusAtual))
                pausaAberta = start;

            foreach (var log in logs)
            {
                var novo = log.Status ?? "";

                if (!string.Equals(statusAtual, novo, StringComparison.OrdinalIgnoreCase))
                {
                    // se estava aguardando e saiu desse estado → fecha a pausa
                    if (IsAguardando(statusAtual) && pausaAberta.HasValue)
                    {
                        var s = pausaAberta.Value;
                        var e = log.DataHora;
                        // clamp no recorte
                        if (e > s) pausas.Add((s < start ? start : s, e > end ? end : e));
                        pausaAberta = null;
                    }

                    // se entrou em "Aguardando..." → abre pausa
                    if (IsAguardando(novo))
                    {
                        pausaAberta = log.DataHora < start ? start : log.DataHora;
                    }

                    statusAtual = novo;
                }
            }

            // se terminou o recorte ainda em "Aguardando..." → fecha em 'end'
            if (IsAguardando(statusAtual) && pausaAberta.HasValue)
            {
                var s = pausaAberta.Value;
                var e = end;
                if (e > s) pausas.Add((s < start ? start : s, e));
            }

            return pausas;
        }

        private static (DateTime S, DateTime E) Clamp(DateTime s, DateTime e, DateTime min, DateTime max)
        {
            if (s < min) s = min;
            if (e > max) e = max;
            return (s, e);
        }

        private static bool EhDiaUtil(DateTime date, ISet<DateTime> holidays)
        {
            var dow = date.DayOfWeek;
            if (dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday) return false;
            if (holidays.Contains(date.Date)) return false;
            return true;
        }

        private static TimeSpan CalcularTempoUtil(DateTime start, DateTime end, TimeSpan workStart, TimeSpan workEnd, ISet<DateTime> holidays)
        {
            if (end <= start) return TimeSpan.Zero;
            if (workEnd <= workStart) throw new ArgumentException("jornadaFim deve ser maior que jornadaInicio");
            TimeSpan total = TimeSpan.Zero;
            DateTime d = start.Date;
            DateTime last = end.Date;

            while (d <= last)
            {
                if (EhDiaUtil(d, holidays))
                {
                    DateTime dayStart = new DateTime(d.Year, d.Month, d.Day, workStart.Hours, workStart.Minutes, workStart.Seconds);
                    DateTime dayEnd = new DateTime(d.Year, d.Month, d.Day, workEnd.Hours, workEnd.Minutes, workEnd.Seconds);

                    DateTime chunkStart = (start > dayStart) ? start : dayStart;
                    DateTime chunkEnd = (end < dayEnd) ? end : dayEnd;

                    if (chunkEnd > chunkStart)
                        total += (chunkEnd - chunkStart);
                }
                d = d.AddDays(1);
            }
            return total;
        }

        // Feriados fixos e móveis (mesmo que você já usa)
        private static IEnumerable<DateTime> FeriadosFixosBrasil(int year)
        {
            yield return new DateTime(year, 1, 1);
            yield return new DateTime(year, 4, 21);
            yield return new DateTime(year, 5, 1);
            yield return new DateTime(year, 9, 7);
            yield return new DateTime(year, 10, 12);
            yield return new DateTime(year, 11, 2);
            yield return new DateTime(year, 11, 15);
            yield return new DateTime(year, 12, 25);
        }
        private static IEnumerable<DateTime> FeriadosMoveisBrasil(int year)
        {
            var pascoa = DataPascoa(year);
            yield return pascoa.AddDays(-47).Date; // terça de carnaval
            yield return pascoa.AddDays(-2).Date;  // sexta-feira santa
            yield return pascoa.AddDays(60).Date;  // Corpus Christi
        }
        private static DateTime DataPascoa(int year)
        {
            int a = year % 19, b = year / 100, c = year % 100, d = b / 4, e = b % 4;
            int f = (b + 8) / 25, g = (b - f + 1) / 3, h = (19 * a + b - d - g + 15) % 30;
            int i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = (a + 11 * h + 22 * l) / 451;
            int month = (h + l - 7 * m + 114) / 31;
            int day = ((h + l - 7 * m + 114) % 31) + 1;
            return new DateTime(year, month, day);
        }
        public async Task<double> CalcularHorasUteisConsumidasAsync(int chamadoId, DateTime inicio, DateTime fim,
        TimeSpan jornadaIni, TimeSpan jornadaFim)
        {
            if (fim < inicio) return 0;

            // feriados para todos os anos tocados
            var holidays = BuildHolidays(inicio.Year, fim.Year);

            // tempo útil bruto
            var util = CalcularTempoUtil(inicio, fim, jornadaIni, jornadaFim, holidays);

            // desconta pausas "Aguardando retorno" dentro do recorte (mesma regra do relatório)
            var pausas = await MontarPausasAsync(chamadoId, inicio, fim);
            foreach (var (ps, pe) in pausas)
            {
                var (s, e) = Clamp(ps, pe, inicio, fim);
                if (e > s)
                    util -= CalcularTempoUtil(s, e, jornadaIni, jornadaFim, holidays);
            }

            if (util < TimeSpan.Zero) util = TimeSpan.Zero;
            return util.TotalHours;
        }

        // === NOVO: adicionar N horas úteis a uma data (retorna o "prazo útil") ===
        public DateTime AdicionarHorasUteis(DateTime inicio, double horasUteis,
            TimeSpan jornadaIni, TimeSpan jornadaFim)
        {
            if (horasUteis <= 0) return inicio;

            // vamos construindo feriados on-the-fly por ano
            var holidays = BuildHolidays(inicio.Year, inicio.Year);
            DateTime cur = inicio;

            // normaliza início para dentro da janela/jornada e dia útil
            cur = NormalizarParaInicioUtil(cur, jornadaIni, jornadaFim, ref holidays);

            var restante = TimeSpan.FromHours(horasUteis);
            while (restante > TimeSpan.Zero)
            {
                // se mudar de ano, garante feriados daquele ano
                if (!holidays.Contains(new DateTime(cur.Year, 1, 1)))
                {
                    foreach (var d in FeriadosFixosBrasil(cur.Year)) holidays.Add(d.Date);
                    foreach (var d in FeriadosMoveisBrasil(cur.Year)) holidays.Add(d.Date);
                }

                if (!EhDiaUtil(cur.Date, holidays))
                {
                    cur = ProximoDiaUtil(cur.Date.AddDays(1), jornadaIni, jornadaFim, ref holidays);
                    continue;
                }

                var fimDoDia = new DateTime(cur.Year, cur.Month, cur.Day, jornadaFim.Hours, jornadaFim.Minutes, jornadaFim.Seconds);
                var slot = fimDoDia - cur;

                if (slot >= restante)
                    return cur + restante;

                restante -= slot;
                // vai para o próximo dia útil, início da jornada
                cur = ProximoDiaUtil(cur.Date.AddDays(1), jornadaIni, jornadaFim, ref holidays);
            }
            return cur;
        }

        // ===== helpers privados de apoio =====
        private static HashSet<DateTime> BuildHolidays(int startYear, int endYear)
        {
            var h = new HashSet<DateTime>();
            for (int y = startYear; y <= endYear; y++)
            {
                foreach (var d in FeriadosFixosBrasil(y)) h.Add(d.Date);
                foreach (var d in FeriadosMoveisBrasil(y)) h.Add(d.Date);
            }
            return h;
        }
        private static DateTime ProximoDiaUtil(DateTime d, TimeSpan ini, TimeSpan fim, ref HashSet<DateTime> holidays)
        {
            while (!EhDiaUtil(d.Date, holidays))
            {
                // garante feriados do novo ano se necessário
                if (!holidays.Contains(new DateTime(d.Year, 1, 1)))
                {
                    foreach (var fx in FeriadosFixosBrasil(d.Year)) holidays.Add(fx.Date);
                    foreach (var mv in FeriadosMoveisBrasil(d.Year)) holidays.Add(mv.Date);
                }
                d = d.AddDays(1);
            }
            return new DateTime(d.Year, d.Month, d.Day, ini.Hours, ini.Minutes, ini.Seconds);
        }
        private static DateTime NormalizarParaInicioUtil(DateTime dt, TimeSpan ini, TimeSpan fim, ref HashSet<DateTime> holidays)
        {
            // antes da jornada -> leva para ini; depois da jornada -> próximo dia útil ini
            if (dt.TimeOfDay < ini) dt = new DateTime(dt.Year, dt.Month, dt.Day, ini.Hours, ini.Minutes, ini.Seconds);
            if (dt.TimeOfDay >= fim) dt = dt.Date.AddDays(1);

            return ProximoDiaUtil(dt, ini, fim, ref holidays);
        }
    }
}
