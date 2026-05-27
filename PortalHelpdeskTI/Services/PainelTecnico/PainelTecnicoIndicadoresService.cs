using Microsoft.EntityFrameworkCore;
using PortalHelpdeskTI.Helpers;
using PortalHelpdeskTI.Models;
using PortalHelpdeskTI.Models.PainelTecnico;

namespace PortalHelpdeskTI.Services.PainelTecnico
{
    public interface IPainelTecnicoIndicadoresService
    {
        Task<PainelTecnicoIndicadoresVM> CalcularAsync(int tecnicoId, DateTime de, DateTime ate);
    }

    public class PainelTecnicoIndicadoresService : IPainelTecnicoIndicadoresService
    {
        private readonly AppDbContext _db;

        private static readonly TimeSpan WorkdayStart = new(8, 0, 0);
        private static readonly TimeSpan WorkdayEnd = new(18, 0, 0);

        public PainelTecnicoIndicadoresService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<PainelTecnicoIndicadoresVM> CalcularAsync(int tecnicoId, DateTime de, DateTime ate)
        {
            // “Meu” = técnico logado
            var meu = await CalcularEscopoAsync(de, ate, tecnicoId);

            // “Time” = geral (sem filtro técnico)
            var time = await CalcularEscopoAsync(de, ate, tecnicoId: null);

            return new PainelTecnicoIndicadoresVM
            {
                DataDe = de,
                DataAte = ate,
                Meu = meu,
                Time = time
            };
        }

        private async Task<IndicadoresKpiVM> CalcularEscopoAsync(DateTime de, DateTime ate, int? tecnicoId)
        {
            var deIni = de.Date;
            var ateFim = ate.Date.AddDays(1); // [de, ate+1)

            var hojeIni = DateTime.Today;
            var hojeFim = hojeIni.AddDays(1);

            // Base de chamados
            var qChamados = _db.Chamados.AsNoTracking().AsQueryable();
            if (tecnicoId.HasValue)
                qChamados = qChamados.Where(c => c.TecnicoId == tecnicoId.Value);

            // Regra de "fechado" (traduzível pelo EF)
            var closed = new[] { "FECHADO", "CONCLUIDO", "CONCLUÍDO" };

            // Status de reabertura (normalizado)
            var respostaUsuario = new[] { "RESPOSTA DO USUÁRIO", "RESPOSTA DO USUARIO" };

            // Feriados: placeholder
            ISet<DateTime> feriados = new HashSet<DateTime>();

            // ===================== Operacional (estado atual / snapshot) =====================
            var backlog = await qChamados.CountAsync(c => !closed.Contains((c.Status ?? "").ToUpper()));
            var abertos = await qChamados.CountAsync(c => (c.Status ?? "").ToUpper() == "ABERTO");
            var aguardando = await qChamados.CountAsync(c => (c.Status ?? "").ToUpper() == "AGUARDANDO");

            // ===================== Fechados hoje =====================
            var fechadosHoje = await qChamados.CountAsync(c =>
                closed.Contains((c.Status ?? "").ToUpper()) &&
                c.DataConclusao != null &&
                c.DataConclusao >= hojeIni && c.DataConclusao < hojeFim
            );

            // ===================== SLA (fechados no período) =====================
            var fechadosPeriodo = await qChamados
                .Where(c =>
                    closed.Contains((c.Status ?? "").ToUpper()) &&
                    c.DataConclusao != null &&
                    c.DataConclusao >= deIni &&
                    c.DataConclusao < ateFim)
                .Select(c => new
                {
                    c.Id,
                    c.DataAbertura,
                    DataConclusao = c.DataConclusao!.Value,
                    c.Prioridade,
                    c.Status,
                    UltimaInteracao = c.Interacoes.Max(i => (DateTime?)i.Data)
                })
                .ToListAsync();

            var idsFechados = fechadosPeriodo.Select(x => x.Id).ToList();

            var logsFechados = (idsFechados.Count == 0)
                ? new List<ChamadoStatusLog>()
                : await _db.ChamadoStatusLogs.AsNoTracking()
                    .Where(l => idsFechados.Contains(l.ChamadoId))
                    .OrderBy(l => l.ChamadoId).ThenBy(l => l.DataHora)
                    .ToListAsync();

            var logsFechadosPorChamado = logsFechados
                .GroupBy(l => l.ChamadoId)
                .ToDictionary(g => g.Key, g => g.ToList());

            int dentro = 0, fora = 0;
            long somaTicksSla = 0;
            int qtdSla = 0;

            foreach (var c in fechadosPeriodo)
            {
                var fim = c.DataConclusao;

                var listaLogs = logsFechadosPorChamado.TryGetValue(c.Id, out var gl)
                    ? gl
                    : new List<ChamadoStatusLog>();

                var timeline = BuildTimelineFromLogs(
                    abertura: c.DataAbertura,
                    statusAtual: c.Status,
                    ultimaInteracao: c.UltimaInteracao,
                    logsOrdenados: listaLogs
                );

                var alvo = (c.Prioridade ?? "").Trim() switch
                {
                    "Urgente" => TimeSpan.FromHours(1),
                    "Alta" => TimeSpan.FromHours(2),
                    _ => TimeSpan.FromHours(4),
                };

                var r = SlaCalculator.Compute(
                    c.DataAbertura,
                    fim,
                    WorkdayStart, WorkdayEnd,
                    feriados,
                    alvo,
                    timeline
                );

                // Tempo consumido de SLA (mesma lógica do progressbar), derivado do Percent
                var usedTicks = (long)(alvo.Ticks * (double)(r.Percent / 100m));
                if (usedTicks > 0)
                {
                    somaTicksSla += usedTicks;
                    qtdSla++;
                }

                if (r.Percent <= 100m) dentro++;
                else fora++;
            }

            var totalFechados = dentro + fora;
            var percDentro = totalFechados == 0 ? 0m : Math.Round(dentro * 100m / totalFechados, 2);

            string tempoMedioFmt = "-";
            if (qtdSla > 0)
            {
                var media = TimeSpan.FromTicks(somaTicksSla / qtdSla);
                tempoMedioFmt = media.TotalHours >= 1
                    ? $"{(int)media.TotalHours}h {media.Minutes}m"
                    : $"{media.Minutes}m";
            }

            // ===================== Em aberto que já estourou SLA (backlog atual) =====================
            var abertosBacklog = await qChamados
                .Where(c =>
                    !closed.Contains((c.Status ?? "").ToUpper()) &&
                    c.DataAbertura < ateFim) // inclui abertos de meses anteriores
                .Select(c => new
                {
                    c.Id,
                    c.DataAbertura,
                    c.Prioridade,
                    c.Status,
                    UltimaInteracao = c.Interacoes.Max(i => (DateTime?)i.Data)
                })
                .ToListAsync();

            var idsAbertosBacklog = abertosBacklog.Select(x => x.Id).ToList();

            int emAbertoForaSla = 0;

            if (idsAbertosBacklog.Count > 0)
            {
                var logsAbertosBacklog = await _db.ChamadoStatusLogs.AsNoTracking()
                    .Where(l => idsAbertosBacklog.Contains(l.ChamadoId))
                    .OrderBy(l => l.ChamadoId).ThenBy(l => l.DataHora)
                    .ToListAsync();

                var logsAbertosBacklogPorChamado = logsAbertosBacklog
                    .GroupBy(l => l.ChamadoId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var c in abertosBacklog)
                {
                    var listaLogs = logsAbertosBacklogPorChamado.TryGetValue(c.Id, out var gl)
                        ? gl
                        : new List<ChamadoStatusLog>();

                    var timeline = BuildTimelineFromLogs(
                        abertura: c.DataAbertura,
                        statusAtual: c.Status,
                        ultimaInteracao: c.UltimaInteracao,
                        logsOrdenados: listaLogs
                    );

                    var alvo = (c.Prioridade ?? "").Trim() switch
                    {
                        "Urgente" => TimeSpan.FromHours(1),
                        "Alta" => TimeSpan.FromHours(2),
                        _ => TimeSpan.FromHours(4),
                    };

                    var r = SlaCalculator.Compute(
                        c.DataAbertura,
                        DateTime.Now, // ainda está aberto
                        WorkdayStart, WorkdayEnd,
                        feriados,
                        alvo,
                        timeline
                    );

                    if (r.Percent >= 100m)
                        emAbertoForaSla++;
                }
            }

            // ===================== Reabertos (no período) =====================
            // Regra: "Resposta do Usuário" dentro do período + existe "Concluído/Fechado" ANTES dessa resposta.
            // Observação: aqui validamos o "Concluído" antes mesmo que ele tenha ocorrido fora do período.
            var qLogs = _db.ChamadoStatusLogs.AsNoTracking().AsQueryable();

            // Se escopo for "Meu", restringe pelos chamados do técnico
            if (tecnicoId.HasValue)
            {
                var meusChamadosIds = qChamados.Select(c => c.Id); // subquery
                qLogs = qLogs.Where(l => meusChamadosIds.Contains(l.ChamadoId));
            }

            // Primeiro: identifica a primeira "Resposta do Usuário" no período por chamado
            var reaberturasNoPeriodo = await qLogs
                .Where(l =>
                    l.DataHora >= deIni && l.DataHora < ateFim &&
                    respostaUsuario.Contains(((l.Status ?? "").Trim()).ToUpper()))
                .GroupBy(l => l.ChamadoId)
                .Select(g => new
                {
                    ChamadoId = g.Key,
                    ReabertoEm = g.Min(x => x.DataHora)
                })
                .ToListAsync();

            int reabertos = 0;

            if (reaberturasNoPeriodo.Count > 0)
            {
                var idsReab = reaberturasNoPeriodo.Select(x => x.ChamadoId).ToList();

                // Conta os que têm "Concluído/Fechado" antes do ReabertoEm
                // Fazemos a validação em memória para evitar limitações de tradução e manter previsível.
                // (Volume tende a ser baixo por mês; se quiser otimizar para alto volume eu te passo versão 100% SQL.)
                var logsParaValidar = await qLogs
                    .Where(l => idsReab.Contains(l.ChamadoId))
                    .Select(l => new { l.ChamadoId, l.Status, l.DataHora })
                    .ToListAsync();

                var logsPorChamado = logsParaValidar
                    .GroupBy(x => x.ChamadoId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var r in reaberturasNoPeriodo)
                {
                    if (!logsPorChamado.TryGetValue(r.ChamadoId, out var lista))
                        continue;

                    var temConcluidoAntes = lista.Any(x =>
                        closed.Contains(((x.Status ?? "").Trim()).ToUpper()) &&
                        x.DataHora < r.ReabertoEm
                    );

                    if (temConcluidoAntes)
                        reabertos++;
                }
            }

            // ===================== CSAT =====================
            var qAval = _db.AvaliacoesChamado.AsNoTracking().AsQueryable()
                .Where(a => a.DataAvaliacao >= deIni && a.DataAvaliacao < ateFim);

            if (tecnicoId.HasValue)
                qAval = qAval.Where(a => a.Chamado.TecnicoId == tecnicoId.Value);

            var totalAval = await qAval.CountAsync();
            decimal? mediaAval = totalAval > 0 ? await qAval.AverageAsync(a => (decimal?)a.Nota) : null;
            if (mediaAval.HasValue) mediaAval = Math.Round(mediaAval.Value, 2);

            return new IndicadoresKpiVM
            {
                // SLA (fechados no período)
                FechadosDentroSla = dentro,
                FechadosForaSla = fora,
                TotalFechados = totalFechados,
                PercDentroSla = percDentro,

                // Operacional (snapshot)
                Abertos = abertos,
                EmAtendimento = backlog,
                Aguardando = aguardando,

                // Qualidade
                Reabertos = reabertos,
                SatisfacaoMedia = mediaAval,
                TotalAvaliacoes = totalAval,

                // Produtividade
                FechadosHoje = fechadosHoje,
                TempoMedioResolucaoFmt = tempoMedioFmt,

                // Fora do SLA (TOTAL = fechados fora + em aberto estourados)
                EmAbertoForaSla = emAbertoForaSla,
                ForaSlaTotal = fora + emAbertoForaSla
            };
        }

        // ======= helper (não depende de Interacoes carregadas) =======
        private static List<StatusChange> BuildTimelineFromLogs(
            DateTime abertura,
            string? statusAtual,
            DateTime? ultimaInteracao,
            IEnumerable<ChamadoStatusLog> logsOrdenados)
        {
            var timeline = new List<StatusChange>
            {
                new() { When = abertura, Status = "Aberto" }
            };

            foreach (var l in logsOrdenados)
            {
                timeline.Add(new StatusChange
                {
                    When = l.DataHora,
                    Status = (l.Status ?? "").Trim()
                });
            }

            timeline = timeline.OrderBy(t => t.When).ToList();

            var whenFinal = ultimaInteracao ?? abertura;

            if (timeline.Count == 0 ||
                !string.Equals(timeline[^1].Status, statusAtual, StringComparison.OrdinalIgnoreCase))
            {
                timeline.Add(new StatusChange
                {
                    When = whenFinal,
                    Status = statusAtual ?? ""
                });
            }

            return timeline;
        }
    }
}
