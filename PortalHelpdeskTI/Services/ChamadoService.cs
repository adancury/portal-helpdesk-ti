using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PortalHelpdeskTI.Helpers;
using PortalHelpdeskTI.Models;
using PortalHelpdeskTI.Services; // para o IEmailService
using PortalHelpdeskTI.Services.Relatorios;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Threading.Tasks;


namespace PortalHelpdeskTI.Services
{
    public class ChamadoService
    {
        private readonly AppDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ILogger<ChamadoService> _logger;
        private readonly RelatorioTempoService _tempo;

        public ChamadoService(AppDbContext context, IEmailService emailService,
            ILogger<ChamadoService> logger, RelatorioTempoService tempo)
        {
            _context = context;
            _emailService = emailService;
            _logger = logger;
            _tempo = tempo;
        }
        public List<Chamado> ObterChamadosUsuario(int usuarioId)
        {
            return _context.Chamados
                .Include(c => c.Usuario)
                .Where(c => c.UsuarioId == usuarioId)
                .ToList();
        }
        /*public List<Chamado> ObterChamadosTecnicoPainel(List<string>? statusSelecionados = null)
        {
            var query = _context.Chamados
                .Include(c => c.Usuario)
                .Include(c => c.Tecnico)
                .AsQueryable();

            // Aplicar filtro apenas se houver seleção
            if (statusSelecionados != null && statusSelecionados.Any())
            {
                query = query.Where(c => statusSelecionados.Contains(c.Status));
            }

            return query
                .OrderByDescending(c => c.DataAbertura)
                .ToList();
        }*/
        public async Task<List<Chamado>> ObterChamadosTecnicoPainelAsync(
        List<string>? statusSelecionados = null,
        string? perfil = null,
        int? tipoManutencaoId = null)
        {
            // 1) Carrega chamados (sem tracking p/ performance)
            var baseQuery = _context.Chamados
                .AsNoTracking()
                .Include(c => c.Usuario)
                .Include(c => c.Tecnico)
                .Include(c => c.Categoria)
                .Include(c => c.TipoChamado)
                .AsQueryable();

            // SEGREGAÇÃO POR PERFIL
            if (tipoManutencaoId.HasValue && !string.IsNullOrWhiteSpace(perfil))
            {
                if (perfil.Equals("Manutencao", StringComparison.OrdinalIgnoreCase))
                {
                    // manutenção vê apenas manutenção
                    baseQuery = baseQuery.Where(c => c.TipoChamadoId == tipoManutencaoId.Value);
                }
                else if (perfil.Equals("Tecnico", StringComparison.OrdinalIgnoreCase))
                {
                    // técnico vê tudo menos manutenção
                    baseQuery = baseQuery.Where(c => c.TipoChamadoId != tipoManutencaoId.Value);
                }
            }

            if (statusSelecionados != null && statusSelecionados.Any())
                baseQuery = baseQuery.Where(c => statusSelecionados.Contains(c.Status));

            var chamados = await baseQuery
                .OrderByDescending(c => c.DataAbertura)
                .ToListAsync();

            if (chamados.Count == 0) return chamados;

            // 2) Carrega SLAs 1x (em memória) e indexa por (CategoriaId, SubcategoriaId, Indicador)
            var slas = await _context.SLAConfiguracoes
                .AsNoTracking()
                .ToListAsync();

            static bool GetSlaIndicador(SLAConfiguracao x)
            {
                // A coluna existe no BD, mas o nome no Model pode variar.
                // Tentativas comuns:
                var p =
                    x.GetType().GetProperty("UsarIndicadorTIAlta") ??
                    x.GetType().GetProperty("UsarIndicadorTiAlta") ??
                    x.GetType().GetProperty("IndicadorTIAlta") ??
                    x.GetType().GetProperty("IndicadorTiAlta");

                if (p != null && p.PropertyType == typeof(bool))
                    return (bool)(p.GetValue(x) ?? false);

                if (p != null && p.PropertyType == typeof(bool?))
                    return (bool?)(p.GetValue(x)) ?? false;

                return false;
            }

            var slasPorChave = slas
                .GroupBy(x => new { x.CategoriaId, x.SubcategoriaId, Indicador = GetSlaIndicador(x) })
                .Select(g => g.OrderByDescending(x => x.Id).First())
                .ToDictionary(
                    x => (x.CategoriaId, x.SubcategoriaId, Indicador: GetSlaIndicador(x)),
                    x => x
                );

            SLAConfiguracao? ResolveSla(int categoriaId, int? subcategoriaId, bool usarIndicadorTiAlta)
            {
                // 1) tenta SLA específico da subcategoria
                if (subcategoriaId.HasValue &&
                    slasPorChave.TryGetValue((categoriaId, subcategoriaId.Value, usarIndicadorTiAlta), out var sla1))
                    return sla1;

                // 2) fallback: SLA padrão da categoria (SubcategoriaId = NULL)
                if (slasPorChave.TryGetValue((categoriaId, (int?)null, usarIndicadorTiAlta), out var sla2))
                    return sla2;

                // 3) fallback extra: tenta indicador false
                if (subcategoriaId.HasValue &&
                    slasPorChave.TryGetValue((categoriaId, subcategoriaId.Value, false), out var sla3))
                    return sla3;

                if (slasPorChave.TryGetValue((categoriaId, (int?)null, false), out var sla4))
                    return sla4;

                return null;
            }

            // 3) Carrega todos os logs de status relevantes 1x
            var ids = chamados.Select(c => c.Id).ToList();
            var agora = DateTime.Now;
            var ranges = chamados.Select(c => new { c.Id, Ini = c.DataAbertura, Fim = c.DataConclusao ?? agora }).ToList();
            var maxEnd = ranges.Max(r => r.Fim);

            var logs = await _context.ChamadoStatusLogs
        .AsNoTracking()
        .Where(l => ids.Contains(l.ChamadoId) && l.DataHora <= maxEnd)
        .Select(l => new StatusLogItem
        {
            ChamadoId = l.ChamadoId,
            DataHora = l.DataHora,
            Status = l.Status
        })
        .OrderBy(l => l.ChamadoId).ThenBy(l => l.DataHora)
        .ToListAsync();

            var logsByChamado = logs
                .GroupBy(x => x.ChamadoId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // 4) Feríados (iguais ao relatório)
            int minYear = ranges.Min(r => r.Ini.Year);
            int maxYear = ranges.Max(r => r.Fim.Year);
            var holidays = BuildHolidays(minYear, maxYear);

            // 5) Jornada (iguais ao relatório)
            var jornadaIni = new TimeSpan(9, 0, 0);
            var jornadaFim = new TimeSpan(17, 0, 0);

            // 6) Calcula SLA para cada chamado (em memória)
            foreach (var c in chamados)
            {
                var categoriaId = c.CategoriaId ?? 0;

                // SubcategoriaId no Chamado pode ter nomes diferentes dependendo do seu Model
                int? subcategoriaId = null;
                {
                    var pSub =
                        c.GetType().GetProperty("SubcategoriaId") ??
                        c.GetType().GetProperty("SubCategoriaId") ??
                        c.GetType().GetProperty("SubcategoriaChamadoId") ??
                        c.GetType().GetProperty("SubCategoriaChamadoId");

                    if (pSub != null)
                    {
                        var v = pSub.GetValue(c);
                        if (v is int i) subcategoriaId = i;
                        else if (v is int ni) subcategoriaId = ni;
                    }
                }

                // Indicador TI Alta no Chamado (ajuste para o nome real se existir)
                bool usarIndicadorTiAlta = false;
                {
                    var pInd =
                        c.GetType().GetProperty("UsarIndicadorTIAlta") ??
                        c.GetType().GetProperty("UsarIndicadorTiAlta") ??
                        c.GetType().GetProperty("IndicadorTIAlta") ??
                        c.GetType().GetProperty("IndicadorTiAlta") ??
                        c.GetType().GetProperty("TIAlta") ??
                        c.GetType().GetProperty("TiAlta");

                    if (pInd != null)
                    {
                        if (pInd.PropertyType == typeof(bool))
                            usarIndicadorTiAlta = (bool)(pInd.GetValue(c) ?? false);
                        else if (pInd.PropertyType == typeof(bool?))
                            usarIndicadorTiAlta = (bool?)(pInd.GetValue(c)) ?? false;
                    }
                }

                var sla = (c.CategoriaId.HasValue && categoriaId > 0)
                    ? ResolveSla(categoriaId, subcategoriaId, usarIndicadorTiAlta)
                    : null;

                var totalSlaHoras = (sla != null && sla.TempoResolucaoHoras > 0)
                    ? sla.TempoResolucaoHoras
                    : 24;

                c.SlaHorasTotal = totalSlaHoras;

                var inicio = c.DataAbertura;
                var fim = c.DataConclusao ?? agora;

                // prazo útil (somando N horas úteis)
                c.PrazoFinalUtil = AddBusinessHours(inicio, totalSlaHoras, jornadaIni, jornadaFim, holidays);

                // Pausas "Aguardando retorno" a partir dos logs (em memória)
                var pausas = MontarPausasEmMemoria(c.Id, inicio, fim, logsByChamado);

                // Tempo útil bruto
                var util = CalcularTempoUtil(inicio, fim, jornadaIni, jornadaFim, holidays);

                // Desconta pausas (só parte útil dentro do recorte)
                var pausado = TimeSpan.Zero;
                foreach (var (ps, pe) in pausas)
                {
                    var (s, e) = Clamp(ps, pe, inicio, fim);
                    if (e > s)
                        pausado += CalcularTempoUtil(s, e, jornadaIni, jornadaFim, holidays);
                }

                var consumidas = util - pausado;
                if (consumidas < TimeSpan.Zero) consumidas = TimeSpan.Zero;
                c.SlaHorasConsumidas = consumidas.TotalHours;

                if (c.DataConclusao.HasValue)
                {
                    c.StatusSLA = (c.SlaHorasConsumidas <= totalSlaHoras) ? "Dentro do Prazo" : "Fora do Prazo";
                    c.PercentualProgressoSLA = 100;
                }
                else
                {
                    var progresso = Math.Min(c.SlaHorasConsumidas / Math.Max(1, totalSlaHoras) * 100.0, 100.0);
                    c.PercentualProgressoSLA = Math.Round(progresso, 1);
                    c.StatusSLA = (c.SlaHorasConsumidas > totalSlaHoras) ? "Fora do Prazo" : "Em andamento";
                }
            }

            return ChamadoOrderingHelper.OrdenarPorPrioridadeESla(chamados).ToList();

            // ===== Helpers (mesma lógica do relatório) =====

            static (DateTime S, DateTime E) Clamp(DateTime s, DateTime e, DateTime min, DateTime max)
            {
                if (s < min) s = min;
                if (e > max) e = max;
                return (s, e);
            }

            static HashSet<DateTime> BuildHolidays(int startYear, int endYear)
            {
                var h = new HashSet<DateTime>();
                for (int y = startYear; y <= endYear; y++)
                {
                    foreach (var d in FeriadosFixosBrasil(y)) h.Add(d.Date);
                    foreach (var d in FeriadosMoveisBrasil(y)) h.Add(d.Date);
                }
                return h;
            }
            static IEnumerable<DateTime> FeriadosFixosBrasil(int year)
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
            static IEnumerable<DateTime> FeriadosMoveisBrasil(int year)
            {
                var pascoa = DataPascoa(year);
                yield return pascoa.AddDays(-47).Date; // terça de carnaval
                yield return pascoa.AddDays(-2).Date;  // sexta-feira santa
                yield return pascoa.AddDays(60).Date;  // Corpus Christi
            }
            static DateTime DataPascoa(int year)
            {
                int a = year % 19, b = year / 100, c = year % 100, d = b / 4, e = b % 4;
                int f = (b + 8) / 25, g = (b - f + 1) / 3, h = (19 * a + b - d - g + 15) % 30;
                int i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = (a + 11 * h + 22 * l) / 451;
                int month = (h + l - 7 * m + 114) / 31;
                int day = ((h + l - 7 * m + 114) % 31) + 1;
                return new DateTime(year, month, day);
            }
            static bool EhDiaUtil(DateTime date, ISet<DateTime> holidays)
                => date.DayOfWeek != DayOfWeek.Saturday
                   && date.DayOfWeek != DayOfWeek.Sunday
                   && !holidays.Contains(date.Date);

            static TimeSpan CalcularTempoUtil(DateTime start, DateTime end, TimeSpan workStart, TimeSpan workEnd, ISet<DateTime> holidays)
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
                        DateTime dayStart = d.Date + workStart;
                        DateTime dayEnd = d.Date + workEnd;

                        DateTime chunkStart = (start > dayStart) ? start : dayStart;
                        DateTime chunkEnd = (end < dayEnd) ? end : dayEnd;

                        if (chunkEnd > chunkStart)
                            total += (chunkEnd - chunkStart);
                    }
                    d = d.AddDays(1);
                }
                return total;
            }

            static DateTime AddBusinessHours(DateTime inicio, double horasUteis, TimeSpan workStart, TimeSpan workEnd, HashSet<DateTime> holidays)
            {
                if (horasUteis <= 0) return inicio;

                DateTime cur = inicio;
                // normaliza início dentro da janela
                if (cur.TimeOfDay < workStart) cur = cur.Date + workStart;
                if (cur.TimeOfDay >= workEnd) cur = cur.Date.AddDays(1) + workStart;
                while (!EhDiaUtil(cur.Date, holidays)) cur = cur.Date.AddDays(1) + workStart;

                var restante = TimeSpan.FromHours(horasUteis);

                while (restante > TimeSpan.Zero)
                {
                    if (!EhDiaUtil(cur.Date, holidays))
                    {
                        cur = cur.Date.AddDays(1) + workStart;
                        continue;
                    }

                    var endOfDay = cur.Date + workEnd;
                    var slot = endOfDay - cur;

                    if (slot >= restante) return cur + restante;

                    restante -= slot;
                    cur = cur.Date.AddDays(1) + workStart;

                    while (!EhDiaUtil(cur.Date, holidays)) cur = cur.Date.AddDays(1) + workStart;
                }
                return cur;
            }

            static List<(DateTime Ini, DateTime Fim)> MontarPausasEmMemoria(
                int chamadoId, DateTime start, DateTime end,
                Dictionary<int, List<StatusLogItem>> logsByChamado)

            {
                var pausas = new List<(DateTime, DateTime)>();
                if (!logsByChamado.TryGetValue(chamadoId, out var lst) || lst.Count == 0)
                    return pausas;

                // status antes do início
                string statusAtual = "Aberto";
                var prev = lst.LastOrDefault(l => l.DataHora < start);
                if (prev != null && prev!.Status != null) statusAtual = prev.Status;

                bool EmAguardando(string? s)
                    => string.Equals(s ?? "", "Aguardando retorno", StringComparison.OrdinalIgnoreCase);

                DateTime? pausaAberta = EmAguardando(statusAtual) ? start : null;

                foreach (var log in lst)
                {
                    if (log.DataHora < start) continue;
                    if (log.DataHora > end) break;

                    var novo = log.Status ?? "";
                    if (!string.Equals(novo, statusAtual, StringComparison.OrdinalIgnoreCase))
                    {
                        // Fechando pausa
                        if (EmAguardando(statusAtual) && pausaAberta.HasValue)
                        {
                            pausas.Add((pausaAberta.Value, log.DataHora));
                            pausaAberta = null;
                        }
                        // Abrindo pausa
                        if (EmAguardando(novo))
                        {
                            pausaAberta = log.DataHora < start ? start : log.DataHora;
                        }
                        statusAtual = novo;
                    }
                }

                if (EmAguardando(statusAtual) && pausaAberta.HasValue)
                    pausas.Add((pausaAberta.Value, end));

                return pausas;
            }
        }
        public Chamado ObterPorId(int id)
        {
            return _context.Chamados
                .Include(c => c.Usuario)
                .Include(c => c.Tecnico)
                .Include(c => c.Interacoes)
                    .ThenInclude(i => i.Usuario)
                .Include(c => c.Interacoes)
                    .ThenInclude(i => i.Anexos)
                .FirstOrDefault(c => c.Id == id);
        }
        public void AbrirChamado(Chamado chamado, int usuarioId)
        {
            chamado.DataAbertura = DateTime.Now;
            chamado.Status = "Aberto";
            chamado.UsuarioId = usuarioId;

            _context.Chamados.Add(chamado);
            _context.SaveChanges();

            // log inicial
            _context.ChamadoStatusLogs.Add(new ChamadoStatusLog
            {
                ChamadoId = chamado.Id,
                Status = "Aberto",
                DataHora = chamado.DataAbertura,
                UsuarioId = usuarioId
            });
            _context.SaveChanges();
        }
        /*public async Task<Interacao> AtualizarChamadoAsync(Chamado chamado, string novoStatus, int? tecnicoId, string interacaoMensagem, int usuarioId)
        {
            var statusAntes = chamado.Status; // captura antes

            var usuario = _context.Usuarios.FirstOrDefault(u => u.Id == usuarioId);

            // 🔹 Se quem está interagindo é o USUÁRIO FINAL, e há mensagem,
            //     então o status passa a "Resposta do Usuário"
            if (usuario?.Perfil == "Usuario" && !string.IsNullOrWhiteSpace(interacaoMensagem))
            {
                novoStatus = "Resposta do Usuário";
            }

            if (!string.IsNullOrEmpty(novoStatus))
                chamado.Status = novoStatus;

            if (tecnicoId.HasValue)
                chamado.TecnicoId = tecnicoId;

            Interacao interacao = null;

            if (!string.IsNullOrWhiteSpace(interacaoMensagem))
            {
                interacao = new Interacao
                {
                    ChamadoId = chamado.Id,
                    UsuarioId = usuarioId,
                    Data = DateTime.Now,
                    Mensagem = interacaoMensagem
                };

                _context.Interacoes.Add(interacao);

                if (chamado.Status == "Concluído")
                {
                    chamado.Solucao = interacaoMensagem;
                    chamado.DataConclusao = DateTime.Now;
                }
            }

            // flags de visualização
            if (usuario != null)
            {
                if (usuario.Perfil == "Usuario")
                {
                    chamado.VisualizadoPeloTecnico = false;
                    chamado.VisualizadoPeloSolicitante = true;
                }
                else if (usuario.Perfil == "Técnico" || usuario.Perfil == "Tecnico")
                {
                    chamado.VisualizadoPeloSolicitante = false;
                    chamado.VisualizadoPeloTecnico = true;
                }
            }

            await _context.SaveChangesAsync();

            // 🔹 Loga mudança de status, se houve
            if (!string.Equals(statusAntes, chamado.Status, StringComparison.OrdinalIgnoreCase))
            {
                await AddStatusLogAsync(chamado.Id, chamado.Status, usuarioId);
            }

            await EnviarEmailAtualizacao(chamado, interacaoMensagem, usuario?.Perfil ?? "");

            return interacao;
        }*/
        public async Task<Interacao?> AtualizarChamadoAsync(
    Chamado chamado,
    string novoStatus,
    int? tecnicoId,
    string interacaoMensagem,
    int usuarioId,
    string? prioridade = null) // ⬅️ NOVO (opcional)
        {
            var statusAntes = chamado.Status; // captura antes

            var usuario = _context.Usuarios.FirstOrDefault(u => u.Id == usuarioId);

            // Se quem está interagindo é o USUÁRIO FINAL e há mensagem -> vira "Resposta do Usuário"
            if (usuario?.Perfil == "Usuario" && !string.IsNullOrWhiteSpace(interacaoMensagem))
                novoStatus = "Resposta do Usuário";

            // Atualiza status
            if (!string.IsNullOrWhiteSpace(novoStatus))
                chamado.Status = novoStatus;

            // Atualiza técnico
            if (tecnicoId.HasValue)
                chamado.TecnicoId = tecnicoId;

            // ✅ Atualiza prioridade (string), se veio no POST
            if (!string.IsNullOrWhiteSpace(prioridade))
                chamado.Prioridade = prioridade.Trim();

            // Cria interação (se veio texto)
            Interacao? interacao = null;
            if (!string.IsNullOrWhiteSpace(interacaoMensagem))
            {
                interacao = new Interacao
                {
                    ChamadoId = chamado.Id,
                    UsuarioId = usuarioId,
                    Data = DateTime.Now,
                    Mensagem = interacaoMensagem
                };
                _context.Interacoes.Add(interacao);
            }

            // ✅ Trava/limpa DataConclusao com base na transição real de status
            var mudouStatus = !string.Equals(statusAntes, chamado.Status, StringComparison.OrdinalIgnoreCase);
            if (mudouStatus)
            {
                if (string.Equals(chamado.Status, "Concluído", StringComparison.OrdinalIgnoreCase))
                {
                    // Se concluiu agora, garante data conclusão
                    chamado.DataConclusao = DateTime.Now;

                    // Se a interação foi a solução, preenche Solucao
                    if (!string.IsNullOrWhiteSpace(interacaoMensagem))
                        chamado.Solucao = interacaoMensagem;
                }
                else
                {
                    // Se saiu de concluído, limpa a data de conclusão (reabertura/continuidade)
                    if (string.Equals(statusAntes, "Concluído", StringComparison.OrdinalIgnoreCase))
                        chamado.DataConclusao = null;
                }
            }

            // Flags de visualização
            if (usuario != null)
            {
                if (usuario.Perfil == "Usuario")
                {
                    chamado.VisualizadoPeloTecnico = false;
                    chamado.VisualizadoPeloSolicitante = true;
                }
                else if (usuario.Perfil == "Técnico" || usuario.Perfil == "Tecnico")
                {
                    chamado.VisualizadoPeloSolicitante = false;
                    chamado.VisualizadoPeloTecnico = true;
                }
            }

            await _context.SaveChangesAsync();

            // Log de mudança de status (se houve)
            if (mudouStatus)
                await AddStatusLogAsync(chamado.Id, chamado.Status, usuarioId);

            await EnviarEmailAtualizacao(chamado, interacaoMensagem, usuario?.Perfil ?? "");

            return interacao;
        }
        private async Task EnviarEmailAtualizacao(Chamado chamado, string interacaoMensagem, string perfil)
        {
            string corpo = null;
            //tem quye fazer um if para saber o perfil e qual template de email enviar:
            if (chamado.Usuario == null)
            {
                chamado.Usuario = _context.Usuarios.FirstOrDefault(u => u.Id == chamado.UsuarioId);
            }
            try
            {
                var ultimaInteracao = chamado.Interacoes.OrderByDescending(i => i.Data).FirstOrDefault();
                string assunto = $"Atualização do chamado #{chamado.Id}";
                string mensagem = interacaoMensagem;

                Console.WriteLine($"➡ Enviando email para: {chamado.Usuario.Email}");

                if (perfil == "Usuario")
                {
                    corpo = MontarCorpoEmail("InteracaoUsuario", chamado.Usuario.Nome, mensagem, chamado.Id);
                    corpo = corpo.Replace("{chamado.Id}", chamado.Id.ToString())
                                 .Replace("{chamado.Titulo}", chamado.Titulo)
                                 .Replace("{interacaoMensagem}", mensagem)
                                 .Replace("{chamado.Usuario.Nome}", chamado.Usuario.Nome)
                                 .Replace("{tecnico.Nome}", chamado.Tecnico?.Nome ?? ""); // cuidado com técnico nulo

                    //await _emailService.EnviarEmailAsync(chamado.Tecnico.Email, assunto, corpo);
                    if (chamado.TecnicoId.HasValue && chamado.Tecnico != null && !string.IsNullOrWhiteSpace(chamado.Tecnico.Email))
                    {
                        await _emailService.EnviarEmailAsync(chamado.Tecnico.Email, assunto, corpo);
                    }
                }
                else
                {
                    corpo = MontarCorpoEmail("InteracaoTecnico", chamado.Usuario.Nome, mensagem, chamado.Id);
                    corpo = corpo.Replace("{chamado.Id}", chamado.Id.ToString())
                                 .Replace("{chamado.Titulo}", chamado.Titulo)
                                 .Replace("{interacaoMensagem}", mensagem)
                                 .Replace("{chamado.Usuario.Nome}", chamado.Usuario.Nome);

                    //await _emailService.EnviarEmailAsync(chamado.Usuario.Email, assunto, corpo);
                    if (chamado.Usuario != null && !string.IsNullOrWhiteSpace(chamado.Usuario.Email) &&
                        chamado.Usuario.Email.Contains("@") && chamado.Usuario.Email.Contains("."))
                    {
                        await _emailService.EnviarEmailAsync(chamado.Usuario.Email, assunto, corpo);
                    }
                }


            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao enviar e-mail: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                // Aqui você poderia lançar ou logar se quiser
            }
        }
        public Interacao AdicionarInteracao(Chamado chamado, int usuarioId, string mensagem)
        {
            var interacao = new Interacao
            {
                ChamadoId = chamado.Id,
                UsuarioId = usuarioId,
                Data = DateTime.Now,
                Mensagem = mensagem
            };

            _context.Interacoes.Add(interacao);

            var usuario = _context.Usuarios.FirstOrDefault(u => u.Id == usuarioId);

            if (usuario != null)
            {
                if (usuario.Perfil == "Usuario")
                {
                    // 🔹 Força "Resposta do Usuário"
                    var statusAntes = chamado.Status;
                    chamado.Status = "Resposta do Usuário";

                    chamado.VisualizadoPeloTecnico = false;
                    chamado.VisualizadoPeloSolicitante = true;

                    _context.Chamados.Update(chamado);
                    _context.SaveChanges();

                    // log se mudou
                    if (!string.Equals(statusAntes, chamado.Status, StringComparison.OrdinalIgnoreCase))
                    {
                        _context.ChamadoStatusLogs.Add(new ChamadoStatusLog
                        {
                            ChamadoId = chamado.Id,
                            Status = chamado.Status,
                            DataHora = DateTime.Now,
                            UsuarioId = usuarioId
                        });
                        _context.SaveChanges();
                    }
                }
                else if (usuario.Perfil == "Tecnico" || usuario.Perfil == "Técnico")
                {
                    chamado.VisualizadoPeloSolicitante = false;
                    _context.Chamados.Update(chamado);
                    _context.SaveChanges();
                }
            }
            else
            {
                _context.SaveChanges();
            }

            return interacao;
        }

        public void ReabrirChamado(Chamado chamado, int usuarioId, string motivo)
        {
            chamado.Status = "Aberto";
            chamado.DataConclusao = null;

            var interacao = new Interacao
            {
                ChamadoId = chamado.Id,
                UsuarioId = usuarioId,
                Data = DateTime.Now,
                Mensagem = $"Chamado reaberto. Motivo: {motivo}"
            };

            _context.Interacoes.Add(interacao);
            _context.SaveChanges();
            // log de status
            _context.ChamadoStatusLogs.Add(new ChamadoStatusLog
            {
                ChamadoId = chamado.Id,
                Status = "Aberto",
                DataHora = DateTime.Now,
                UsuarioId = usuarioId
            });
            _context.SaveChanges();
        }
        private string MontarCorpoEmail(string tipo, string nomeDestinatario, string mensagem, int idChamado)
        {
            var template = _context.TemplatesEmail.FirstOrDefault(t => t.Tipo == tipo);


            if (template == null)
                return $"Olá, {nomeDestinatario}. Uma nova interação ocorreu: {mensagem}.";

            return template.CorpoHtml;
        }
        public object ObterResumoTecnico(int? tecnicoId)
        {
            // base: todos os chamados do técnico logado
            var qry = _context.Chamados.AsQueryable();
            /*if (tecnicoId.HasValue)
                qry = qry.Where(c => c.TecnicoId == tecnicoId);*/

            // ajuste os status conforme seu domínio ("Aberto", "Em Andamento", etc.)
            var total = qry.Count();
            var andamento = qry.Count(c => c.Status != "Concluído"); // se usar "Aberto", ajuste aqui
            var resolvidos = qry.Count(c => c.Status == "Concluído");
            //var urgentes = qry.Count(c => (c.Prioridade == "Urgente" && c.Prioridade == "Alta" && c.Prioridade == "Crítica" && c.Status != "Concluído"));
            var urgentes = qry.Count(c => (c.Prioridade == "Urgente" && c.Status != "Concluído"
                                        || c.Prioridade == "Crítica" && c.Status != "Concluído"
                                        || c.Prioridade == "Alta" && c.Status != "Concluído"));
            //var urgentes = qry.Count(c => (c.Prioridade == "Crítica" && c.Status != "Concluído"));

            return new { total, andamento, resolvidos, urgentes };
        }
        private static string GetLogoUrl() => "https://drive.google.com/thumbnail?id=1L6etGFMCV4832TInat_BT2ceLe4m7uSb";
        private static Dictionary<string, string> BuildTokens(Chamado c, string linkAvaliacao)
        {
            return new()
            {
                ["UsuarioNome"] = c.Usuario?.Nome ?? "",
                ["ChamadoId"] = c.Id.ToString(),
                ["ChamadoTitulo"] = c.Titulo ?? "",
                ["LinkAvaliacao"] = linkAvaliacao,
                ["LogoUrl"] = GetLogoUrl()
            };
        }
        private static string RenderTokens(string html, IReadOnlyDictionary<string, string> tokens)
        {
            if (string.IsNullOrEmpty(html) || tokens == null || tokens.Count == 0)
                return html ?? "";

            // tokens que devem entrar "crus" (sem HtmlEncode)
            var rawKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "LinkAvaliacao", "LogoUrl" };

            foreach (var kv in tokens)
            {
                var token = "{{" + kv.Key + "}}";
                var value = rawKeys.Contains(kv.Key)
                    ? kv.Value
                    : System.Net.WebUtility.HtmlEncode(kv.Value);

                html = html.Replace(token, value);
            }

            return html;
        }
        private static string FallbackTemplate(string usuarioNome, int chamadoId, string chamadoTitulo, string link, string logoUrl)
        {
            return $@"
            <table style=""width: 100%; max-width: 600px; border-collapse: collapse; font-family: Arial, sans-serif; color: #333; margin: 0 auto;"">
              <tr>
                <td style=""background-color: #004A80; color: white; padding: 12px 8px; text-align: center;"">
                  <img src=""{logoUrl}"" alt=""Logo BRW"" style=""max-width: 110px; margin-bottom: 6px;"">
                  <h2 style=""margin: 6px 0 0 0; font-size: 18px; font-weight: 700;"">Portal Helpdesk BRW</h2>
                </td>
              </tr>
              <tr>
                <td style=""padding: 20px;"">
                  <p style=""margin: 0 0 10px 0;"">Olá <strong>{System.Net.WebUtility.HtmlEncode(usuarioNome)}</strong>,</p>
                  <p style=""margin: 0 0 14px 0;"">
                    Seu chamado <strong>#{chamadoId} - {System.Net.WebUtility.HtmlEncode(chamadoTitulo ?? "")}</strong> foi concluído.
                  </p>
                  <p style=""margin: 0 0 18px 0;"">Para nos ajudar a melhorar, por favor, avalie o atendimento. Leva menos de 1 minuto.</p>
                  <p style=""text-align: center; margin: 26px 0;"">
                    <a href=""{link}""
                       style=""background-color: #004A80; color: white; text-decoration: none; padding: 12px 22px; border-radius: 6px; display: inline-block; font-weight: bold;"">
                       Avaliar Atendimento
                    </a>
                  </p>
                  <p style=""font-size: 12px; color: #555; margin: 0;"">
                    Se o botão não funcionar, acesse este link:<br>
                    <a href=""{link}"" style=""color: #004A80; word-break: break-all;"">{System.Net.WebUtility.HtmlEncode(link)}</a>
                  </p>
                  <p style=""margin-top: 28px;"">Atenciosamente,<br>Equipe de Suporte BRW</p>
                </td>
              </tr>
              <tr>
                <td style=""background-color: #004A80; color: #dbe7f3; font-size: 12px; padding: 10px; text-align: center;"">
                  Este e-mail foi enviado automaticamente. Não responda.
                </td>
              </tr>
            </table>";
        }
        /// <summary>
        /// Envia o e-mail de lembrete de avaliação usando TemplatesEmail.
        /// Marca AvaliacaoLembreteEnviado = true ao concluir.
        /// </summary>
        public async Task<bool> EnviarLembreteAvaliacaoAsync(Chamado chamado, string linkAvaliacao, bool reenvio = false)
        {
            // sanity checks
            if (chamado == null || chamado.Usuario == null || string.IsNullOrWhiteSpace(chamado.Usuario.Email))
                return false;

            // tipo do template
            var tipo = reenvio ? "LembreteAvaliacao_Reenvio" : "LembreteAvaliacao";

            // busca template
            var tpl = await _context.TemplatesEmail
                .AsNoTracking()
                .Where(t => t.Tipo == tipo)
                .Select(t => new { t.Assunto, t.CorpoHtml })
                .FirstOrDefaultAsync();

            // monta tokens
            var tokens = BuildTokens(chamado, linkAvaliacao);

            // assunto e corpo (com fallback)
            var assuntoBase = $"Por favor, avalie o atendimento do chamado #{chamado.Id}";
            var assunto = tpl?.Assunto ?? assuntoBase;
            var corpoHtml = tpl?.CorpoHtml ?? FallbackTemplate(
                chamado.Usuario?.Nome ?? "",
                chamado.Id,
                chamado.Titulo ?? "",
                linkAvaliacao,
                GetLogoUrl()
            );

            // renderiza tokens {{...}}
            assunto = RenderTokens(assunto, tokens);
            corpoHtml = RenderTokens(corpoHtml, tokens);

            // envia
            await _emailService.EnviarEmailAsync(chamado.Usuario.Email, assunto, corpoHtml);

            // marca como enviado (1 por chamado)
            chamado.AvaliacaoLembreteEnviado = true;
            await _context.SaveChangesAsync();

            return true;
        }
        private async Task AddStatusLogAsync(int chamadoId, string novoStatus, int? usuarioId = null)
        {
            // evita duplicar se o último registro já for o mesmo status
            var ultimo = await _context.ChamadoStatusLogs
                .Where(l => l.ChamadoId == chamadoId)
                .OrderByDescending(l => l.DataHora)
                .FirstOrDefaultAsync();

            if (ultimo != null && string.Equals(ultimo.Status, novoStatus, StringComparison.OrdinalIgnoreCase))
                return;

            _context.ChamadoStatusLogs.Add(new ChamadoStatusLog
            {
                ChamadoId = chamadoId,
                Status = novoStatus,
                DataHora = DateTime.Now,
                UsuarioId = usuarioId
            });
            await _context.SaveChangesAsync();
        }
        private sealed class StatusLogItem
        {
            public int ChamadoId { get; set; }
            public DateTime DataHora { get; set; }
            public string? Status { get; set; }
        }
    }
}
