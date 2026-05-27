using iText.Commons.Actions.Contexts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PortalHelpdeskTI.Models;
using PortalHelpdeskTI.Helpers;
using PortalHelpdeskTI.Services;
using PortalHelpdeskTI.Services.AvaliacaoChamado;
using PortalHelpdeskTI.Services.Calendario;
using PortalHelpdeskTI.Services.Relatorios;
using PortalHelpdeskTI.Services.PainelTecnico;

public class ChamadosController : Controller
{
    private readonly ChamadoService _chamadoService;
    private readonly AnexoService _anexoService;
    private readonly AppDbContext _context;
    private readonly AvaliacaoService _avaliacaoService;
    private readonly ILogger<ChamadosController> _logger;
    private readonly IEmailService _emailService;
    private readonly IHolidayProvider _holidayProvider;
    private readonly RelatorioTempoService _relatorioTempoService;
    private static readonly TimeSpan WorkdayStart = new(8, 0, 0);
    private static readonly TimeSpan WorkdayEnd = new(18, 0, 0);
    private readonly PrevisaoRupturaService _previsaoRupturaService;
    private readonly IPainelTecnicoIndicadoresService _painelIndicadores;


    public ChamadosController(ChamadoService chamadoService,
        AnexoService anexoService, AppDbContext context,
        AvaliacaoService avaliacaoService, ILogger<ChamadosController> logger,
        IEmailService emailService, IHolidayProvider holidayProvider, 
        RelatorioTempoService relatorioTempoService, PrevisaoRupturaService previsaoRupturaService, 
        IPainelTecnicoIndicadoresService painelIndicadores)
    {
        _chamadoService = chamadoService;
        _anexoService = anexoService;
        _context = context;
        _avaliacaoService = avaliacaoService;
        _logger = logger;
        _emailService = emailService;
        _holidayProvider = holidayProvider;
        _relatorioTempoService = relatorioTempoService;
        _previsaoRupturaService = previsaoRupturaService;
        _painelIndicadores = painelIndicadores;
    }
    // --- SLA: config de jornada ---
    //private static readonly TimeSpan WorkdayStart = new(8, 0, 0);
    //private static readonly TimeSpan WorkdayEnd = new(18, 0, 0);
    private static DateTime GetUltimaDataInteracaoOuAbertura(Chamado c)
    {
        var max = c.Interacoes?
            .OrderByDescending(i => i.Data)
            .Select(i => (DateTime?)i.Data)
            .FirstOrDefault();
        return max ?? c.DataAbertura;
    }

    // --- SLA: alvo por prioridade ---
    private static TimeSpan AlvoSlaPorPrioridade(string? prioridade) => prioridade switch
    {
        "Urgente" => TimeSpan.FromHours(1),
        "Alta" => TimeSpan.FromHours(2),
        _ => TimeSpan.FromHours(4),
    };

    // --- monta a timeline do chamado para o SLA ---
    // precisa no topo:
    // using System.Linq;
    // using PortalHelpdeskTI.Helpers;
    // usando a VM: using PortalHelpdeskTI.Models;

    private GridTecnicoItemVM BuildGridItemVm(
        Chamado c,
        IEnumerable<ChamadoStatusLog> logsOrdenados,  // <- logs deste chamado
        ISet<DateTime> feriados,
        DateTime agora)
    {
        // sua timeline versão B
        var timeline = BuildTimelineFromLogs(c, logsOrdenados);

        var fim = string.Equals(c.Status, "Concluído", StringComparison.OrdinalIgnoreCase)
            ? (c.DataConclusao ?? GetUltimaDataInteracaoOuAbertura(c))
            : agora;

        var alvo = AlvoSlaPorPrioridade(c.Prioridade);

        var r = SlaCalculator.Compute(
            c.DataAbertura,
            fim,
            WorkdayStart,
            WorkdayEnd,
            feriados,
            alvo,
            timeline
        );

        var css = r.Percent >= 100 ? "bg-danger"
                : r.Percent >= 75 ? "bg-warning"
                : "bg-success";

        return new GridTecnicoItemVM
        {
            Chamado = c,
            SlaPercent = r.Percent,
            SlaCss = css,
            SlaLabel = r.Label + (r.Paused ? " • pausado" : ""),
            SlaPaused = r.Paused
        };
    }

    [HttpGet]
    public IActionResult Avaliar(int id)
    {
        var usuarioId = HttpContext.Session.GetInt32("UsuarioId") ?? 0;

        // Se já avaliou, volta para detalhes
        bool jaAvaliado = _context.AvaliacoesChamado
            .Any(a => a.ChamadoId == id && a.UsuarioId == usuarioId);

        if (jaAvaliado)
        {
            TempData["MensagemSucesso"] = "Você já avaliou este chamado. Obrigado!";
            return RedirectToAction("Detalhes", new { id });
        }

        // Carrega dados do chamado
        var chamado = _context.Chamados
            .Include(c => c.Categoria)
            .Include(c => c.Tecnico)
            .FirstOrDefault(c => c.Id == id);

        if (chamado == null)
            return NotFound();

        // Considerando que são DateTime? (nullable)
        DateTime? inicio = chamado.DataAbertura;
        DateTime? fim = chamado.DataConclusao;

        // Jornada útil
        var jornadaInicio = new TimeSpan(9, 0, 0);  // 09:00
        var jornadaFim = new TimeSpan(17, 0, 0); // 17:00

        string tempoResolucaoFmt = "—";

        if (inicio.HasValue && fim.HasValue && fim.Value > inicio.Value)
        {
            var start = inicio.Value;
            var end = fim.Value;

            // Feriados (fixos + móveis) por todos os anos do intervalo
            var feriados = new HashSet<DateTime>();
            for (int y = start.Year; y <= end.Year; y++)
            {
                foreach (var d in FeriadosFixosBrasil(y)) feriados.Add(d.Date);
                foreach (var d in FeriadosMoveisBrasil(y)) feriados.Add(d.Date);
            }

            // ====================== PAUSAS POR "AGUARDANDO RETORNO" ======================
            // 1) carrega logs até o fim do intervalo
            var logs = _context.ChamadoStatusLogs
                .Where(l => l.ChamadoId == chamado.Id && l.DataHora <= end)
                .OrderBy(l => l.DataHora)
                .ToList();

            // 2) status imediatamente antes do início
            var anterior = _context.ChamadoStatusLogs
                .Where(l => l.ChamadoId == chamado.Id && l.DataHora < start)
                .OrderByDescending(l => l.DataHora)
                .FirstOrDefault();

            string statusAtual = anterior?.Status ?? chamado.Status ?? "Aberto"; // ajuste se não tiver 'Status' no Chamado
            var pausas = new List<(DateTime Ini, DateTime Fim)>();
            DateTime? pausaAberta = null;

            // Se já iniciou em "Aguardando retorno", abre pausa no 'start'
            if (string.Equals(statusAtual, "Aguardando retorno", StringComparison.OrdinalIgnoreCase))
                pausaAberta = start;

            // 3) percorre logs para montar intervalos
            foreach (var log in logs)
            {
                var novoStatus = log.Status ?? "";

                if (!string.Equals(statusAtual, novoStatus, StringComparison.OrdinalIgnoreCase))
                {
                    // se estava aguardando e saiu, fecha pausa
                    if (string.Equals(statusAtual, "Aguardando retorno", StringComparison.OrdinalIgnoreCase) && pausaAberta.HasValue)
                    {
                        var (s, e) = ClampIntervalo(pausaAberta.Value, log.DataHora, start, end);
                        if (e > s) pausas.Add((s, e));
                        pausaAberta = null;
                    }

                    // se entrou em "Aguardando retorno", abre pausa
                    if (string.Equals(novoStatus, "Aguardando retorno", StringComparison.OrdinalIgnoreCase))
                    {
                        pausaAberta = log.DataHora < start ? start : log.DataHora;
                    }

                    statusAtual = novoStatus;
                }
            }

            // se terminou ainda aguardando, fecha em 'end'
            if (string.Equals(statusAtual, "Aguardando retorno", StringComparison.OrdinalIgnoreCase) && pausaAberta.HasValue)
            {
                var (s, e) = ClampIntervalo(pausaAberta.Value, end, start, end);
                if (e > s) pausas.Add((s, e));
            }
            // ====================== FIM PAUSAS ======================

            // Total útil bruto
            var totalUtil = CalcularTempoUtil(start, end, jornadaInicio, jornadaFim, feriados);

            // Subtrai o útil das pausas (calcula útil dentro de cada intervalo de pausa)
            TimeSpan totalPausas = TimeSpan.Zero;
            foreach (var p in pausas)
            {
                var (ps, pe) = ClampIntervalo(p.Ini, p.Fim, start, end);
                if (pe > ps)
                    totalPausas += CalcularTempoUtil(ps, pe, jornadaInicio, jornadaFim, feriados);
            }

            var ts = totalUtil - totalPausas;
            if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;

            tempoResolucaoFmt = ToFriendly(ts);
        }

        ViewBag.Codigo = $"TI-{chamado.Id:D6}";
        ViewBag.DataResolvido = chamado.DataConclusao;
        ViewBag.Tecnico = chamado.Tecnico?.Nome;
        ViewBag.Categoria = chamado.Categoria?.Nome;
        ViewBag.Descricao = chamado.Titulo ?? chamado.Descricao;
        ViewBag.TempoResolucao = tempoResolucaoFmt;

        return View(new PortalHelpdeskTI.Models.AvaliacaoChamado { ChamadoId = id });

        // ===== Helpers locais (SEM DateOnly/TimeOnly) =====

        static (DateTime S, DateTime E) ClampIntervalo(DateTime s, DateTime e, DateTime min, DateTime max)
        {
            if (s < min) s = min;
            if (e > max) e = max;
            return (s, e);
        }

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

        static bool EhDiaUtil(DateTime date, ISet<DateTime> holidays)
        {
            var dow = date.DayOfWeek;
            if (dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday) return false;
            if (holidays.Contains(date.Date)) return false;
            return true;
        }

        // Feriados fixos nacionais
        static IEnumerable<DateTime> FeriadosFixosBrasil(int year)
        {
            yield return new DateTime(year, 1, 1);   // Confraternização Universal
            yield return new DateTime(year, 4, 21);  // Tiradentes
            yield return new DateTime(year, 5, 1);   // Dia do Trabalho
            yield return new DateTime(year, 9, 7);   // Independência
            yield return new DateTime(year, 10, 12); // Nossa Senhora Aparecida
            yield return new DateTime(year, 11, 2);  // Finados
            yield return new DateTime(year, 11, 15); // Proclamação da República
            yield return new DateTime(year, 12, 25); // Natal
        }

        // Feriados móveis (Páscoa → carnaval, sexta santa, corpus christi)
        static IEnumerable<DateTime> FeriadosMoveisBrasil(int year)
        {
            DateTime pascoa = DataPascoa(year);
            yield return pascoa.AddDays(-47).Date; // terça de carnaval
            yield return pascoa.AddDays(-2).Date;  // sexta-feira santa
            yield return pascoa.AddDays(60).Date;  // Corpus Christi
        }

        // Algoritmo de Butcher (gregoriano)
        static DateTime DataPascoa(int year)
        {
            int a = year % 19, b = year / 100, c = year % 100, d = b / 4, e = b % 4;
            int f = (b + 8) / 25, g = (b - f + 1) / 3, h = (19 * a + b - d - g + 15) % 30;
            int i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = (a + 11 * h + 22 * l) / 451;
            int month = (h + l - 7 * m + 114) / 31;
            int day = ((h + l - 7 * m + 114) % 31) + 1;
            return new DateTime(year, month, day);
        }

        static string ToFriendly(TimeSpan ts)
        {
            if (ts.TotalMinutes < 1) return $"{ts.Seconds}s";
            if (ts.TotalHours < 1) return $"{(int)ts.TotalMinutes}m";
            var h = (int)ts.TotalHours;
            var m = ts.Minutes;
            return m > 0 ? $"{h}h {m}m" : $"{h}h";
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Avaliar(PortalHelpdeskTI.Models.AvaliacaoChamado model)
    {
        var usuarioId = HttpContext.Session.GetInt32("UsuarioId") ?? 0;
        _logger.LogInformation("POST Avaliar: ChamadoId={ChamadoId}, UsuarioId={UsuarioId}, Nota={Nota}",
                               model.ChamadoId, usuarioId, model.Nota);

        if (usuarioId <= 0) return Unauthorized();
        if (model.ChamadoId <= 0) return BadRequest();
        if (model.Nota < 1 || model.Nota > 5)
        {
            ModelState.AddModelError(nameof(model.Nota), "Selecione uma nota.");
            return View(model);
        }

        // Defesa extra: garante que não vamos inserir Id explícito
        model.Id = 0;
        model.UsuarioId = usuarioId;
        model.DataAvaliacao = DateTime.Now;

        try
        {
            _context.AvaliacoesChamado.Add(model);
            await _context.SaveChangesAsync();
            TempData["MensagemSucesso"] = "Obrigado por sua avaliação!";
        }
        catch (DbUpdateException ex) when (
            (ex.InnerException?.Message?.Contains("UX_AvaliacoesChamado_ChaUsu") ?? false) ||
            (ex.InnerException?.Message?.Contains("UNIQUE") ?? false))
        {
            // Violação de índice único (já avaliou)
            TempData["MensagemSucesso"] = "Você já avaliou este chamado. Obrigado!";
        }

        //return RedirectToAction("Detalhes", new { id = model.ChamadoId });
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> IndicadoresPainelTecnico(DateTime? de, DateTime? ate)
    {
        try
        {
            var tecnicoId = HttpContext.Session.GetInt32("UsuarioId") ?? 0;
            if (tecnicoId <= 0)
                return Unauthorized(new { message = "Sessão expirada ou usuário não autenticado." });

            // padrão: mês atual
            var hoje = DateTime.Today;
            var dataDe = de?.Date ?? new DateTime(hoje.Year, hoje.Month, 1);
            var dataAte = ate?.Date ?? dataDe.AddMonths(1).AddDays(-1);

            var vm = await _painelIndicadores.CalcularAsync(tecnicoId, dataDe, dataAte);

            // Regra "fechado" consistente com o service
            var closed = new[] { "FECHADO", "CONCLUIDO", "CONCLUÍDO" };

            // Pendentes de avaliação (time-wide):
            // Chamados fechados sem avaliação do usuário do chamado e sem lembrete enviado.
            // (Evita CountAsync com StringComparison e simplifica a tradução)
            var pendentes = await _context.Chamados
                .AsNoTracking()
                .Where(c =>
                    closed.Contains((c.Status ?? "").ToUpper()) &&
                    !c.AvaliacaoLembreteEnviado &&
                    !_context.AvaliacoesChamado.Any(a =>
                        a.ChamadoId == c.Id &&
                        a.UsuarioId == c.UsuarioId
                    )
                )
                .CountAsync();

            vm.PendentesAvaliacao = pendentes;

            return Json(vm);
        }
        catch (Exception ex)
        {
            // Retornar JSON em caso de erro ajuda o seu JS a depurar.
            Response.StatusCode = 500;
            return Json(new
            {
                message = "Erro ao calcular indicadores do painel técnico.",
                detail = ex.Message
            });
        }
    }

    [HttpGet]
    public IActionResult Novo()
    {
        Console.WriteLine("Chamado GET Novo foi chamado");
        ViewBag.Tipos = _context.TipoChamado.OrderBy(t => t.Nome).ToList();

        // Criar um novo chamado com a lista de anexos inicializada
        var chamado = new Chamado
        {
            Anexos = new List<Anexo>() // Inicializando a lista de anexos
        };

        return View("Novo", chamado); // Passa o modelo para a view
    }
    [HttpGet]
    public IActionResult ResumoTecnico()
    {
        var tecnicoId = HttpContext.Session.GetInt32("UsuarioId");
        var resumo = _chamadoService.ObterResumoTecnico(tecnicoId);
        return Json(resumo);
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Novo(Chamado chamado, List<IFormFile>? anexos)
    {
        ValidarAberturaChamado(chamado);
        ValidarAnexosNovoChamado(anexos);

        if (!ModelState.IsValid)
        {
            ViewBag.Tipos = _context.TipoChamado.OrderBy(t => t.Nome).ToList();
            chamado.Anexos ??= new List<Anexo>();
            return View("Novo", chamado);
        }

        int usuarioId = HttpContext.Session.GetInt32("UsuarioId") ?? 1;
        _chamadoService.AbrirChamado(chamado, usuarioId); // Grava o chamado e define o ID

        if (anexos != null)
        {
            foreach (var anexo in anexos.Where(a => a != null && a.Length > 0))
            {
                _anexoService.SalvarAnexo(chamado.Id, anexo);
            }
        }

        TempData["MensagemSucesso"] = $"Chamado '{chamado.Titulo}' aberto com sucesso!";

        var perfil = HttpContext.Session.GetString("Perfil");
        return perfil != null && (perfil.Equals("Técnico", StringComparison.OrdinalIgnoreCase) || perfil.Equals("Tecnico", StringComparison.OrdinalIgnoreCase))
            ? RedirectToAction("TecnicoPainel")
            : RedirectToAction("Index");
    }

    private void ValidarAberturaChamado(Chamado chamado)
    {
        ModelState.Remove(nameof(Chamado.Id));
        ModelState.Remove(nameof(Chamado.Status));
        ModelState.Remove(nameof(Chamado.Usuario));
        ModelState.Remove(nameof(Chamado.UsuarioId));
        ModelState.Remove(nameof(Chamado.Tecnico));
        ModelState.Remove(nameof(Chamado.TipoChamado));
        ModelState.Remove(nameof(Chamado.Categoria));
        ModelState.Remove(nameof(Chamado.Subcategoria));
        ModelState.Remove(nameof(Chamado.Interacoes));
        ModelState.Remove(nameof(Chamado.Anexos));
        ModelState.Remove(nameof(Chamado.StatusSLA));
        ModelState.Remove(nameof(Chamado.PercentualProgressoSLA));
        ModelState.Remove(nameof(Chamado.SlaHorasTotal));
        ModelState.Remove(nameof(Chamado.SlaHorasConsumidas));
        ModelState.Remove(nameof(Chamado.PrazoFinalUtil));

        if (string.IsNullOrWhiteSpace(chamado.Titulo))
            ModelState.AddModelError(nameof(Chamado.Titulo), "Informe o título do chamado.");

        if (string.IsNullOrWhiteSpace(chamado.Descricao))
            ModelState.AddModelError(nameof(Chamado.Descricao), "Informe a descrição do chamado.");

        var prioridadesValidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Baixa",
            "Média",
            "Media",
            "Alta",
            "Crítica",
            "Critica"
        };

        if (string.IsNullOrWhiteSpace(chamado.Prioridade) || !prioridadesValidas.Contains(chamado.Prioridade.Trim()))
            ModelState.AddModelError(nameof(Chamado.Prioridade), "Selecione a prioridade.");

        if (!chamado.TipoChamadoId.HasValue || chamado.TipoChamadoId.Value <= 0)
            ModelState.AddModelError(nameof(Chamado.TipoChamadoId), "Selecione o tipo do chamado.");

        if (!chamado.CategoriaId.HasValue || chamado.CategoriaId.Value <= 0)
            ModelState.AddModelError(nameof(Chamado.CategoriaId), "Selecione a categoria.");

        if (!chamado.SubcategoriaId.HasValue || chamado.SubcategoriaId.Value <= 0)
            ModelState.AddModelError(nameof(Chamado.SubcategoriaId), "Selecione a subcategoria.");

        if (chamado.TipoChamadoId.HasValue &&
            !_context.TipoChamado.Any(t => t.Id == chamado.TipoChamadoId.Value))
        {
            ModelState.AddModelError(nameof(Chamado.TipoChamadoId), "Tipo de chamado inválido.");
        }

        if (chamado.TipoChamadoId.HasValue &&
            chamado.CategoriaId.HasValue &&
            !_context.CategoriaChamado.Any(c =>
                c.Id == chamado.CategoriaId.Value &&
                c.TipoChamadoId == chamado.TipoChamadoId.Value))
        {
            ModelState.AddModelError(nameof(Chamado.CategoriaId), "Categoria inválida para o tipo selecionado.");
        }

        if (chamado.CategoriaId.HasValue &&
            chamado.SubcategoriaId.HasValue &&
            !_context.SubcategoriaChamado.Any(s =>
                s.Id == chamado.SubcategoriaId.Value &&
                s.CategoriaId == chamado.CategoriaId.Value))
        {
            ModelState.AddModelError(nameof(Chamado.SubcategoriaId), "Subcategoria inválida para a categoria selecionada.");
        }
    }

    private void ValidarAnexosNovoChamado(List<IFormFile>? anexos)
    {
        const long tamanhoMaximoBytes = 15 * 1024 * 1024;

        if (anexos == null || anexos.Count == 0)
            return;

        foreach (var anexo in anexos.Where(a => a != null && a.Length > 0))
        {
            if (anexo.Length > tamanhoMaximoBytes)
            {
                ModelState.AddModelError("", $"O anexo '{anexo.FileName}' excede 15 MB.");
            }
        }
    }
    [HttpGet]
    public IActionResult Detalhes(int id)
    {
        var usuarioId = HttpContext.Session.GetInt32("UsuarioId") ?? 0;
        var chamado = _context.Chamados
            .Include(c => c.Interacoes).ThenInclude(i => i.Anexos)
            .Include(c => c.Anexos)
            .FirstOrDefault(c => c.Id == id);

        if (chamado == null) return NotFound();

        var solicitanteJaAvaliado = _context.AvaliacoesChamado
        .Any(a => a.ChamadoId == id && a.UsuarioId == chamado.UsuarioId);

        ViewBag.JaAvaliado = _context.AvaliacoesChamado
            .Any(a => a.ChamadoId == id && a.UsuarioId == usuarioId);

        return View(chamado);
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EnviarLembreteAvaliacao(int id)
    {
        var chamado = await _context.Chamados
            .Include(c => c.Usuario)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (chamado == null) return NotFound();

        // só se concluído, ainda não avaliado e ainda não enviado
        bool jaAvaliado = await _context.AvaliacoesChamado
            .AnyAsync(a => a.ChamadoId == id && a.UsuarioId == chamado.UsuarioId);

        if (!string.Equals(chamado.Status, "Concluído", StringComparison.OrdinalIgnoreCase))
        {
            TempData["MensagemSucesso"] = "O chamado ainda não está concluído.";
            return RedirectToAction("Detalhes", new { id });
        }
        if (jaAvaliado)
        {
            TempData["MensagemSucesso"] = "Este chamado já foi avaliado.";
            return RedirectToAction("Detalhes", new { id });
        }
        if (chamado.AvaliacaoLembreteEnviado)
        {
            TempData["MensagemSucesso"] = "O lembrete deste chamado já foi enviado.";
            return RedirectToAction("Detalhes", new { id });
        }

        var link = Url.Action("Avaliar", "Chamados", new { id }, protocol: Request.Scheme, host: Request.Host.Value);

        // ✅ Centraliza no service (usa template da tabela + seta AvaliacaoLembreteEnviado + SaveChanges)
        await _chamadoService.EnviarLembreteAvaliacaoAsync(chamado, link!);

        TempData["MensagemSucesso"] = "Lembrete enviado.";
        return RedirectToAction("Detalhes", new { id });
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EnviarLembretesAvaliacao()
    {
        try
        {
            // Seleciona somente quem está concluído, ainda não teve lembrete enviado
            // e ainda não possui avaliação registrada pelo mesmo usuário.
            var candidatos = await _context.Chamados
                .Include(c => c.Usuario)
                .Where(c => c.Status == "Concluído"
                            && !c.AvaliacaoLembreteEnviado
                            && !_context.AvaliacoesChamado
                                .Any(a => a.ChamadoId == c.Id && a.UsuarioId == c.UsuarioId))
                .ToListAsync();

            int enviados = 0;

            foreach (var c in candidatos)
            {
                if (string.IsNullOrWhiteSpace(c.Usuario?.Email))
                    continue;

                var link = Url.Action(
                    "Avaliar",
                    "Chamados",
                    new { id = c.Id },
                    protocol: Request.Scheme,
                    host: Request.Host.Value
                );

                var ok = await _chamadoService.EnviarLembreteAvaliacaoAsync(c, link!);

                if (ok)
                {
                    enviados++;
                    // ✅ Marca que o lembrete deste chamado já foi enviado
                    c.AvaliacaoLembreteEnviado = true;

                    // (opcional) se tiver um campo de data/hora, registre:
                    // c.DataLembreteAvaliacaoEnviado = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync();

            // Calcula quantos ainda restaram pendentes após a marcação
            var pendentesRestantes = await _context.Chamados
                .Where(c => c.Status == "Concluído"
                            && !c.AvaliacaoLembreteEnviado
                            && !_context.AvaliacoesChamado
                                .Any(a => a.ChamadoId == c.Id && a.UsuarioId == c.UsuarioId))
                .CountAsync();

            // 🔁 Agora retornamos JSON para o front atualizar o botão/label sem reload
            return Json(new { enviados, pendentes = pendentesRestantes });
        }
        catch (Exception)
        {
            // Logue se tiver logger injetado
            // _logger.LogError(ex, "Erro ao enviar lembretes de avaliação");

            // Retorna erro para o front exibir toast vermelho
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Json(new { error = "Falha ao enviar lembretes de avaliação." });
        }
    }

    public async Task<IActionResult> TecnicoPainel(List<string>? statusSelecionados)
    {
        // Default da tela (o que aparece quando nada é marcado)
        var defaultStatusDisplay = new List<string>
    {
        "Aberto", "Em Atendimento", "Aguardando", "Resposta do Usuário"
    };

        // Se vier vazio/nulo, usa o default de exibição
        if (statusSelecionados == null || statusSelecionados.Count == 0)
            statusSelecionados = defaultStatusDisplay;

        // Expande "Resposta do Usuário" para aceitar registros sem acento também
        var statusParaFiltro = statusSelecionados
            .SelectMany(s => s.Equals("Resposta do Usuário", StringComparison.OrdinalIgnoreCase)
                             ? new[] { "Resposta do Usuário", "Resposta do Usuario" }
                             : new[] { s })
            .ToList();

        var perfil = GetPerfilSessao() ?? "";
        var tipoManutId = await GetTipoManutencaoIdAsync();

        List<Chamado> chamados;
        List<int> novosChamados;
        if (perfil.Equals("Manutencao", StringComparison.OrdinalIgnoreCase) && !tipoManutId.HasValue)
        {
            chamados = new List<Chamado>();
            novosChamados = new List<int>();
        }
        else
        {
            chamados = await _chamadoService.ObterChamadosTecnicoPainelAsync(
                statusParaFiltro,
                perfil,
                tipoManutId
            );

            var qNovaInteracao = _context.Chamados
                .Where(c => statusParaFiltro.Contains(c.Status));

            if (tipoManutId.HasValue)
            {
                if (perfil.Equals("Manutencao", StringComparison.OrdinalIgnoreCase))
                    qNovaInteracao = qNovaInteracao.Where(c => c.TipoChamadoId == tipoManutId.Value);
                else if (perfil.Equals("Tecnico", StringComparison.OrdinalIgnoreCase))
                    qNovaInteracao = qNovaInteracao.Where(c => c.TipoChamadoId != tipoManutId.Value);
            }

            novosChamados = await qNovaInteracao
                .Select(c => new
                {
                    c.Id,
                    UltimaInteracaoPerfil = c.Interacoes
                        .OrderByDescending(i => i.Data)
                        .Select(i => i.Usuario.Perfil)
                        .FirstOrDefault()
                })
                .Where(x => x.UltimaInteracaoPerfil == "Usuario")
                .Select(x => x.Id)
                .ToListAsync();
        }

        var ids = chamados.Select(c => c.Id).ToList();
        var logs = ids.Count == 0
            ? new List<ChamadoStatusLog>()
            : await _context.ChamadoStatusLogs
                .Where(l => ids.Contains(l.ChamadoId))
                .OrderBy(l => l.ChamadoId).ThenBy(l => l.DataHora)
                .ToListAsync();

        var logsPorChamado = logs
            .GroupBy(l => l.ChamadoId)
            .ToDictionary(g => g.Key, g => g.AsEnumerable());

        ISet<DateTime> feriados = new HashSet<DateTime>();
        var agora = DateTime.Now;
        var vms = chamados
            .Select(c => BuildGridItemVm(
                c,
                logsPorChamado.TryGetValue(c.Id, out var listaLogs)
                    ? listaLogs
                    : Enumerable.Empty<ChamadoStatusLog>(),
                feriados,
                agora))
            .ToList();

        ViewBag.NovosChamados = novosChamados;
        ViewBag.StatusSelecionados = statusSelecionados; // mantém a seleção do usuário (com acento)

        //var resumoRuptura = await _previsaoRupturaService.ObterResumoAsync();
        //ViewBag.RupturaResumo = resumoRuptura;

        return View(ChamadoOrderingHelper.OrdenarPorPrioridadeESla(vms).ToList());
    }
    public IActionResult Index(bool? pendenteAvaliacao)
    {
        var usuarioId = HttpContext.Session.GetInt32("UsuarioId");
        if (usuarioId == null)
            return RedirectToAction("Login", "Account");

        var chamados = _chamadoService.ObterChamadosUsuario(usuarioId.Value);

        // Bolinha de nova interação do técnico (mantém)
        ViewBag.NovosChamados = chamados
            .Where(c => !c.VisualizadoPeloSolicitante)
            .Select(c => c.Id)
            .ToList();

        // IDs concluídos do usuário
        var concluidosIds = chamados
            .Where(c => c.Status == "Concluído")
            .Select(c => c.Id)
            .ToList();

        // IDs já avaliados (somente do usuário logado e dos chamados concluídos dele)
        var avaliadosIds = (concluidosIds.Count == 0)
            ? new HashSet<int>()
            : _context.AvaliacoesChamado
                .Where(a => a.UsuarioId == usuarioId.Value && concluidosIds.Contains(a.ChamadoId))
                .Select(a => a.ChamadoId)
                .Distinct()
                .ToHashSet();

        // IDs pendentes = concluídos - avaliados
        var pendentesIds = concluidosIds
            .Where(id => !avaliadosIds.Contains(id))
            .ToHashSet();

        ViewBag.PendentesAvaliacaoIds = pendentesIds;
        ViewBag.FiltroPendentes = pendenteAvaliacao == true;

        if (pendenteAvaliacao == true)
            chamados = chamados.Where(c => pendentesIds.Contains(c.Id)).ToList();

        return View(chamados);
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    [ActionName("Atender")] // garante que o form asp-action="Atender" poste aqui
    public async Task<IActionResult> AtenderAsync(
    int id,
    string novoStatus,
    int? TecnicoId,
    int? TipoChamadoId,
    int? CategoriaId,
    int? SubcategoriaId,
    string interacaoMensagem,
    string? Prioridade,
    IFormFile? Anexo)
    {
        var chamado = _context.Chamados
            .Include(c => c.Interacoes)
            .FirstOrDefault(c => c.Id == id);

        if (chamado == null) return NotFound();

        // ✅ Atualiza classificação (com coerência básica)
        if (TipoChamadoId.HasValue && TipoChamadoId.Value != 0)
        {
            chamado.TipoChamadoId = TipoChamadoId.Value;

            // se trocar o tipo e a categoria atual não pertencer ao novo tipo, limpa cat/sub
            if (chamado.CategoriaId.HasValue &&
                !_context.CategoriaChamado.Any(c => c.Id == chamado.CategoriaId && c.TipoChamadoId == chamado.TipoChamadoId))
            {
                chamado.CategoriaId = null;
                chamado.SubcategoriaId = null;
            }
        }

        if (CategoriaId.HasValue && CategoriaId.Value != 0)
        {
            chamado.CategoriaId = CategoriaId.Value;

            // se trocar a categoria e a sub atual não pertencer à nova categoria, limpa sub
            if (chamado.SubcategoriaId.HasValue &&
                !_context.SubcategoriaChamado.Any(s => s.Id == chamado.SubcategoriaId && s.CategoriaId == chamado.CategoriaId))
            {
                chamado.SubcategoriaId = null;
            }
        }

        if (SubcategoriaId.HasValue && SubcategoriaId.Value != 0)
            chamado.SubcategoriaId = SubcategoriaId.Value;

        // ✅ Atualiza status/técnico/mensagem (como já fazia)
        await _chamadoService.AtualizarChamadoAsync(
            chamado,
            novoStatus,
            TecnicoId,
            interacaoMensagem,
            HttpContext.Session.GetInt32("UsuarioId") ?? 0,
            Prioridade
        );

        // 📎 Anexo (como já fazia)
        if (Anexo != null && Anexo.Length > 0)
        {
            var ultimaInteracao = chamado.Interacoes.OrderByDescending(i => i.Data).FirstOrDefault();
            var resultado = _anexoService.SalvarAnexo(id, Anexo, ultimaInteracao?.Id ?? 0);
            if (!resultado.sucesso) TempData["MensagemErro"] = resultado.mensagem;
        }

        await _context.SaveChangesAsync();
        TempData["MensagemSucesso"] = "Chamado atualizado com sucesso!";
        return RedirectToAction("TecnicoPainel", "Chamados");
    }
    public async Task<IActionResult> AdicionarInteracaoAsync(int chamadoId, string mensagem, IFormFile? Anexo)
    {
        var chamado = _chamadoService.ObterPorId(chamadoId);
        if (chamado == null)
            return NotFound();

        var usuarioId = HttpContext.Session.GetInt32("UsuarioId") ?? 0;

        // ✅ cria a interação corretamente e dispara e-mail
        var interacao = await _chamadoService.AtualizarChamadoAsync(
            chamado,
            chamado.Status,          // mantém o status atual
            chamado.TecnicoId,       // mantém técnico atual
            mensagem,
            usuarioId
        );

        // 📎 Se houver anexo, vincula à interação recém-criada
        if (Anexo != null && Anexo.Length > 0)
        {
            var resultado = _anexoService.SalvarAnexo(chamadoId, Anexo, interacao.Id);
            TempData[resultado.sucesso ? "MensagemSucesso" : "MensagemErro"] = resultado.mensagem;
        }
        else
        {
            TempData["MensagemSucesso"] = "Interação adicionada com sucesso!";
        }

        return RedirectToAction("Index", "Chamados");
    }
    public IActionResult CategoriasPorTipo(int tipoId)
    {
        var categorias = _context.CategoriaChamado
            .Where(c => c.TipoChamadoId == tipoId)
            .Select(c => new { c.Id, c.Nome })
            .ToList();

        return Json(categorias);
    }
    public IActionResult SubcategoriasPorCategoria(int categoriaId)
    {
        var subcategorias = _context.SubcategoriaChamado
            .Where(s => s.CategoriaId == categoriaId)
            .Select(s => new { s.Id, s.Nome })
            .ToList();

        return Json(subcategorias);
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult UploadAnexo(int ChamadoId, IFormFile Anexo)
    {
        var resultado = _anexoService.SalvarAnexo(ChamadoId, Anexo);
        TempData[resultado.sucesso ? "MensagemSucesso" : "MensagemErro"] = resultado.mensagem;

        return RedirectToAction("Detalhes", new { id = ChamadoId });
    }
    [HttpGet]
    public IActionResult Atender(int id)
    {
        var chamado = _context.Chamados
            .Include(c => c.Usuario).ThenInclude(u => u.Departamento)
            .Include(c => c.Tecnico)
            .Include(c => c.TipoChamado)
            .Include(c => c.Categoria)
            .Include(c => c.Subcategoria)
            .Include(c => c.Anexos)
            .Include(c => c.Interacoes).ThenInclude(i => i.Usuario)
            .FirstOrDefault(c => c.Id == id);

        if (chamado == null) return NotFound();
        var solicitanteJaAvaliado = _context.AvaliacoesChamado
        .Any(a => a.ChamadoId == id && a.UsuarioId == chamado.UsuarioId);
            ViewBag.JaAvaliado = solicitanteJaAvaliado;
        if (!chamado.VisualizadoPeloTecnico)
        {
            chamado.VisualizadoPeloTecnico = true;
            _context.SaveChanges();
        }

        ViewBag.Tecnicos = _context.Usuarios
            .Where(u => u.Perfil == "Técnico" || u.Perfil == "Tecnico")
            .OrderBy(u => u.Nome)
            .ToList();

        // ✅ listas para os dropdowns
        ViewBag.Tipos = _context.TipoChamado.OrderBy(t => t.Nome).ToList();
        ViewBag.Categorias = (chamado.TipoChamadoId > 0)
            ? _context.CategoriaChamado.Where(c => c.TipoChamadoId == chamado.TipoChamadoId).OrderBy(c => c.Nome).ToList()
            : new List<CategoriaChamado>();
        ViewBag.Subcategorias = (chamado.CategoriaId > 0)
            ? _context.SubcategoriaChamado.Where(s => s.CategoriaId == chamado.CategoriaId).OrderBy(s => s.Nome).ToList()
            : new List<SubcategoriaChamado>();

        return View(chamado);
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ReabrirChamado(int id, string motivo)
    {
        var chamado = _chamadoService.ObterPorId(id);
        if (chamado == null)
            return NotFound();

        if (chamado.DataConclusao == null ||
            (DateTime.Now - chamado.DataConclusao.Value).TotalHours > 24)
        {
            TempData["MensagemErro"] = "O prazo de 24 horas para reabrir o chamado expirou.";
            return RedirectToAction("Detalhes", new { id });
        }
        chamado.AvaliacaoLembreteEnviado = false;
        var usuarioId = HttpContext.Session.GetInt32("UsuarioId") ?? 0;

        chamado.Status = "Aberto";
        chamado.DataConclusao = null; // limpa a data, já que o chamado foi reaberto

        var interacao = new Interacao
        {
            ChamadoId = chamado.Id,
            UsuarioId = usuarioId,
            Data = DateTime.Now,
            Mensagem = $"Chamado reaberto. Motivo: {motivo}"
        };

        _context.Interacoes.Add(interacao);
        chamado.VisualizadoPeloTecnico = false;

        _context.SaveChanges();

        TempData["MensagemSucesso"] = $"O chamado #{chamado.Id} foi reaberto com sucesso!";
        return RedirectToAction("Index");
    }
    [HttpGet]
    public IActionResult VerificarNovosChamados()
    {
        int usuarioId = HttpContext.Session.GetInt32("UsuarioId") ?? 0;

        var chamados = _context.Chamados
            .Where(c => c.TecnicoId == usuarioId && !c.VisualizadoPeloTecnico && c.Status != "Concluído")
            .Select(c => new
            {
                c.Id
            })
            .ToList();

        return Json(chamados);
    }
    // Timeline usando SOMENTE os logs de status (sem Interacao.StatusNovo)
    private static List<StatusChange> BuildTimelineFromLogs(Chamado c, IEnumerable<ChamadoStatusLog> logsOrdenados)
    {
        var timeline = new List<StatusChange> { new() { When = c.DataAbertura, Status = "Aberto" }  };

        foreach (var l in logsOrdenados)
        {
            timeline.Add(new StatusChange
            {
                When = l.DataHora,
                Status = l.Status ?? ""
            });
        }

        timeline = timeline.OrderBy(t => t.When).ToList();

        // Garante status atual no final
        var whenFinal = GetUltimaDataInteracaoOuAbertura(c);
        if (timeline.Count == 0 || !string.Equals(timeline[^1].Status, c.Status, StringComparison.OrdinalIgnoreCase))
        {
            timeline.Add(new StatusChange
            {
                When = whenFinal,
                Status = c.Status
            });
        }

        return timeline;
    }
    [HttpGet]
    public async Task<IActionResult> GridTecnicoParcial(List<string>? statusSelecionados = null)
    {
        var defaultStatusDisplay = new List<string>
    {
        "Aberto", "Em Atendimento", "Aguardando", "Resposta do Usuário"
    };

        if (statusSelecionados == null || statusSelecionados.Count == 0)
            statusSelecionados = defaultStatusDisplay;

        var statusParaFiltro = statusSelecionados
            .SelectMany(s => s.Equals("Resposta do Usuário", StringComparison.OrdinalIgnoreCase)
                             ? new[] { "Resposta do Usuário", "Resposta do Usuario" }
                             : new[] { s })
            .ToList();

        var perfil = GetPerfilSessao() ?? "";
        var tipoManutId = await GetTipoManutencaoIdAsync();

        // ✅ FAIL-SAFE: se é Manutencao e não achou o tipo, NUNCA mostrar TI (retorna grid vazia)
        if (perfil.Equals("Manutencao", StringComparison.OrdinalIgnoreCase) && !tipoManutId.HasValue)
        {
            ViewBag.NovosChamados = new List<int>();
            ViewBag.StatusSelecionados = statusSelecionados;
            return PartialView("_GridTecnico", Enumerable.Empty<GridTecnicoItemVM>());
        }

        // ids com nova interação do usuário final (bolinha)
        var qNovaInteracao = _context.Chamados
            .Where(c => statusParaFiltro.Contains(c.Status));

        // ✅ Segregação por perfil x TipoChamado (Manutencao)
        if (tipoManutId.HasValue)
        {
            if (perfil.Equals("Manutencao", StringComparison.OrdinalIgnoreCase))
                qNovaInteracao = qNovaInteracao.Where(c => c.TipoChamadoId == tipoManutId.Value);
            else if (perfil.Equals("Tecnico", StringComparison.OrdinalIgnoreCase))
                qNovaInteracao = qNovaInteracao.Where(c => c.TipoChamadoId != tipoManutId.Value);
        }

        var idsComNovaInteracao = await qNovaInteracao
            .Select(c => new
            {
                c.Id,
                UltimaInteracaoPerfil = c.Interacoes
                    .OrderByDescending(i => i.Data)
                    .Select(i => i.Usuario.Perfil)
                    .FirstOrDefault()
            })
            .Where(x => x.UltimaInteracaoPerfil == "Usuario")
            .Select(x => x.Id)
            .ToListAsync();

        // lista principal
        var chamados = await _chamadoService.ObterChamadosTecnicoPainelAsync(
            statusParaFiltro,
            perfil,
            tipoManutId
        );

        // carrega logs para TODOS os chamados exibidos
        var ids = chamados.Select(c => c.Id).ToList();

        // ✅ Se não tem nenhum chamado, evita query e já retorna vazio (micro otimização)
        if (ids.Count == 0)
        {
            ViewBag.NovosChamados = idsComNovaInteracao;
            ViewBag.StatusSelecionados = statusSelecionados;
            return PartialView("_GridTecnico", Enumerable.Empty<GridTecnicoItemVM>());
        }

        var logs = await _context.ChamadoStatusLogs
            .Where(l => ids.Contains(l.ChamadoId))
            .OrderBy(l => l.ChamadoId).ThenBy(l => l.DataHora)
            .ToListAsync();

        var logsPorChamado = logs
            .GroupBy(l => l.ChamadoId)
            .ToDictionary(g => g.Key, g => g.AsEnumerable());

        // feriados (placeholder — troque depois pelo provider real)
        ISet<DateTime> feriados = new HashSet<DateTime>();

        var vms = new List<GridTecnicoItemVM>();
        var agora = DateTime.Now;

        foreach (var c in chamados)
        {
            var listaLogs = logsPorChamado.TryGetValue(c.Id, out var gl)
                ? gl
                : Enumerable.Empty<ChamadoStatusLog>();

            var timeline = BuildTimelineFromLogs(c, listaLogs);

            var fim = string.Equals(c.Status, "Concluído", StringComparison.OrdinalIgnoreCase)
                ? (c.DataConclusao ?? GetUltimaDataInteracaoOuAbertura(c))
                : agora;

            var alvo = c.Prioridade switch
            {
                "Urgente" => TimeSpan.FromHours(1),
                "Alta" => TimeSpan.FromHours(2),
                _ => TimeSpan.FromHours(4),
            };

            var r = SlaCalculator.Compute(
                c.DataAbertura, fim,
                WorkdayStart, WorkdayEnd,
                feriados, alvo, timeline
            );

            var css = r.Percent >= 100 ? "bg-danger"
                   : r.Percent >= 75 ? "bg-warning"
                   : "bg-success";

            vms.Add(new GridTecnicoItemVM
            {
                Chamado = c,
                SlaPercent = r.Percent,
                SlaCss = css,
                SlaLabel = r.Label + (r.Paused ? " • pausado" : ""),
                SlaPaused = r.Paused
            });
        }

        ViewBag.NovosChamados = idsComNovaInteracao;
        ViewBag.StatusSelecionados = statusSelecionados;
        return PartialView("_GridTecnico", ChamadoOrderingHelper.OrdenarPorPrioridadeESla(vms).ToList());
    }
    [HttpGet]
    public IActionResult DownloadAnexo(int chamadoId, string nomeArquivo)
    {
        if (string.IsNullOrWhiteSpace(nomeArquivo))
            return BadRequest("Nome do arquivo é obrigatório.");

        nomeArquivo = Path.GetFileName(nomeArquivo); // evita traversal
        var caminhoArquivo = Path.Combine(Directory.GetCurrentDirectory(), "Anexos", chamadoId.ToString(), nomeArquivo);

        if (!System.IO.File.Exists(caminhoArquivo))
            return NotFound("Arquivo não encontrado.");

        var contentType = ObterContentType(nomeArquivo) ?? "application/octet-stream";
        var stream = new FileStream(caminhoArquivo, FileMode.Open, FileAccess.Read, FileShare.Read);

        // Sem Content-Disposition -> o browser decide (PDF e imagens abrem inline)
        return new FileStreamResult(stream, contentType);
    }
    private string ObterContentType(string nomeArquivo)
    {
        var extensao = Path.GetExtension(nomeArquivo).ToLowerInvariant();
        return extensao switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".mp4" => "video/mp4",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
    }
    [HttpGet]
    public IActionResult ChamadosComInteracaoUsuario()
    {
        var chamados = _context.Chamados
            .Include(c => c.Interacoes)
            .ThenInclude(i => i.Usuario)
            .Where(c => c.Status != "Concluído") // não trazer os finalizados
            .ToList()
            .Where(c =>
            {
                var ultimaInteracao = c.Interacoes
                    .OrderByDescending(i => i.Data)
                    .FirstOrDefault();
                return ultimaInteracao?.Usuario?.Perfil == "Usuario" && !c.VisualizadoPeloTecnico;
            })
            .Select(c => c.Id)
            .ToList();

        return Json(chamados);
    }
    [HttpGet]
    public async Task<IActionResult> RelatorioTempo(DateTime? de, DateTime? ate, bool exportCsv = false)
    {
        if (!de.HasValue && !ate.HasValue)
        {
            var now = DateTime.Now;
            var primeiroDia = new DateTime(now.Year, now.Month, 1);
            var ultimoDia = primeiroDia.AddMonths(1).AddDays(-1);

            de = primeiroDia;
            ate = ultimoDia;
        }

        // Passa valores formatados para a view (pré-preencher os inputs)
        ViewBag.De = de?.ToString("yyyy-MM-dd");
        ViewBag.Ate = ate?.ToString("yyyy-MM-dd");

        var itens = await _relatorioTempoService.GerarAsync(de, ate);

        if (exportCsv)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("ChamadoId;Titulo;Solicitante;Tecnico;Abertura;Conclusao;TempoBruto;TempoUtil;TempoPausado;SlaPercent;SlaDentroPrazo;SlaResumo");

            foreach (var i in itens)
            {
                string esc(string? v) => (v ?? "").Replace(";", ",");
                sb.AppendLine(string.Join(";",
                    i.ChamadoId,
                    esc(i.Titulo),
                    esc(i.Solicitante),
                    esc(i.Tecnico),
                    i.DataAbertura?.ToString("dd/MM/yyyy HH:mm"),
                    i.DataConclusao?.ToString("dd/MM/yyyy HH:mm"),
                    i.TempoBrutoFmt,
                    i.TempoUtilFmt,
                    i.TempoPausadoFmt,
                    i.SlaPercent,
                    i.SlaDentroPrazo ? "Sim" : "Não",
                    esc(i.SlaResumo)
                ));
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            var nome = $"relatorio_tempo_{DateTime.Now:yyyyMMdd_HHmm}.csv";
            return File(bytes, "text/csv; charset=utf-8", nome);
        }

        // Renderiza uma View simples de tabela (crie Views/Chamados/RelatorioTempo.cshtml)
        return View(itens);
    }
    /*helper*/
    private async Task<int?> GetTipoManutencaoIdAsync()
    {
        return await _context.TipoChamado
            .Where(t => t.Nome == "Manutencao" || t.Nome == "Manutenção")
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync();
    }

    private string? GetPerfilSessao()
    {
        return HttpContext.Session.GetString("Perfil");
    }

    [HttpGet]
    public IActionResult AvaliarChamado(int id)
    {
        var usuarioId = HttpContext.Session.GetInt32("UsuarioId");
        if (usuarioId == null)
            return RedirectToAction("Login", "Account");

        var chamado = _context.Chamados
            .AsNoTracking()
            .FirstOrDefault(c => c.Id == id && c.UsuarioId == usuarioId.Value);

        if (chamado == null)
            return NotFound();

        if (!string.Equals(chamado.Status, "Concluído", StringComparison.OrdinalIgnoreCase))
        {
            TempData["MensagemSucesso"] = "Você só pode avaliar chamados concluídos.";
            return RedirectToAction("Index");
        }

        var jaAvaliou = _context.AvaliacoesChamado
            .Any(a => a.ChamadoId == id && a.UsuarioId == usuarioId.Value);

        if (jaAvaliou)
        {
            TempData["MensagemSucesso"] = "Este chamado já foi avaliado.";
            return RedirectToAction("Index");
        }

        var model = new AvaliacaoChamado
        {
            ChamadoId = id
        };

        return View("Avaliar", model); // reutiliza sua View existente
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AvaliarChamado(AvaliacaoChamado model)
    {
        var usuarioId = HttpContext.Session.GetInt32("UsuarioId");
        if (usuarioId == null)
            return RedirectToAction("Login", "Account");

        if (model.Nota < 1 || model.Nota > 5)
            ModelState.AddModelError(nameof(model.Nota), "Selecione uma nota válida.");

        var chamado = await _context.Chamados
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == model.ChamadoId && c.UsuarioId == usuarioId.Value);

        if (chamado == null)
            return NotFound();

        var jaAvaliou = await _context.AvaliacoesChamado
            .AnyAsync(a => a.ChamadoId == model.ChamadoId && a.UsuarioId == usuarioId.Value);

        if (jaAvaliou)
            ModelState.AddModelError("", "Este chamado já foi avaliado.");

        if (!ModelState.IsValid)
            return View("Avaliar", model);

        model.UsuarioId = usuarioId.Value;
        model.DataAvaliacao = DateTime.Now;

        _context.AvaliacoesChamado.Add(model);
        await _context.SaveChangesAsync();

        TempData["MensagemSucesso"] = "Obrigado por sua avaliação!";

        return RedirectToAction(nameof(Index), new { pendenteAvaliacao = 1 });
    }

}
