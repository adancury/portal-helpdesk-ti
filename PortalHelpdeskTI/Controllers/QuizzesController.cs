using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PortalHelpdeskTI.Services.Quizzes;
using PortalHelpdeskTI.ViewModels.Quizzes;

[Route("Quizzes")]
public class QuizzesController : Controller
{
    private readonly IQuizService _service;
    private readonly AppDbContext _db;

    public QuizzesController(IQuizService service, AppDbContext db)
    { _service = service; _db = db; }

    // GET /Quizzes
    [HttpGet("", Name = "Quizzes_Index")]
    public IActionResult Index(string? categoria)
    {
        // se não estiver logado, mostramos a lista sem contadores (Take já exige login)
        int usuarioId = HttpContext.Session.GetInt32("UsuarioId") ?? 0;

        // pré-carrega tentativas do usuário (um group-by só)
        var stats = _db.QuizTentativas
            .Where(t => t.UsuarioId == usuarioId)
            .GroupBy(t => t.QuizId)
            .Select(g => new { QuizId = g.Key, Count = g.Count(), MaxNota = g.Max(x => x.NotaPercentual) })
            .ToList();

        var quizzes = _db.Quizzes
            .Include(q => q.Questoes)
            .Where(q => q.Publicado && (categoria == null || q.Categoria == categoria))
            .OrderBy(q => q.Titulo)
            .AsEnumerable() // para poder usar lógica local com 'stats'
            .Select(q =>
            {
                var s = stats.FirstOrDefault(x => x.QuizId == q.Id);
                int tentativas = s?.Count ?? 0;
                bool aprovado = q.NotaCortePercentual.HasValue && s != null && s.MaxNota >= q.NotaCortePercentual.Value;
                bool disponivel = usuarioId == 0 ? true : (tentativas < 3 && !aprovado);

                return new QuizListItemVM
                {
                    Id = q.Id,
                    Titulo = q.Titulo,
                    Categoria = q.Categoria,
                    Publicado = q.Publicado,
                    TotalQuestoes = q.Questoes.Count,
                    TentativasUsadas = tentativas,
                    Aprovado = aprovado,
                    Disponivel = disponivel
                };
            })
            .ToList();

        return View(quizzes);
    }

    // GET /Quizzes/{id}
    [HttpGet("{id:int}", Name = "Quizzes_Take")]
    public IActionResult Take(int id)
    {
        int usuarioId = HttpContext.Session.GetInt32("UsuarioId") ?? 0;
        if (usuarioId == 0) return RedirectToAction("Login", "Account");

        var quiz = _db.Quizzes.FirstOrDefault(q => q.Id == id && q.Publicado);
        if (quiz == null) return NotFound();

        // ---- Regra de bloqueio: máx 3 tentativas OU já aprovado (nota >= corte) ----
        var tentativas = _db.QuizTentativas
            .Where(t => t.QuizId == id && t.UsuarioId == usuarioId)
            .Select(t => new { t.NotaPercentual })
            .ToList();
        int usadas = tentativas.Count;
        bool aprovado = quiz.NotaCortePercentual.HasValue && tentativas.Any(t => t.NotaPercentual >= quiz.NotaCortePercentual.Value);

        if (usadas >= 3 || aprovado)
        {
            TempData["toast"] = usadas >= 3
                ? "Você atingiu o limite de 3 tentativas para este quiz."
                : "Você já atingiu a nota mínima deste quiz. Novas tentativas não estão disponíveis.";
            return RedirectToRoute("Quizzes_Index");
        }
        // ---------------------------------------------------------------------------

        var vm = _service.MontarQuizParaExecucao(id);
        ViewBag.InicioIso = DateTime.Now.ToString("o");
        return View(vm);
    }

    // POST /Quizzes/{id}
    [HttpPost("{id:int}", Name = "Quizzes_Submit")]
    [ValidateAntiForgeryToken]
    public IActionResult Submit(int id, QuizSubmitVM model, string inicioIso)
    {
        int usuarioId = HttpContext.Session.GetInt32("UsuarioId") ?? 0;
        if (usuarioId == 0) return RedirectToAction("Login", "Account");
        if (id != model.QuizId) return BadRequest();

        // Re-valida bloqueio (caso vários tabs/envios)
        var quiz = _db.Quizzes.FirstOrDefault(q => q.Id == id);
        if (quiz == null) return NotFound();

        var tentativas = _db.QuizTentativas
            .Where(t => t.QuizId == id && t.UsuarioId == usuarioId)
            .Select(t => new { t.NotaPercentual })
            .ToList();
        int usadas = tentativas.Count;
        bool aprovado = quiz.NotaCortePercentual.HasValue && tentativas.Any(t => t.NotaPercentual >= quiz.NotaCortePercentual.Value);
        if (usadas >= 3 || aprovado)
        {
            TempData["toast"] = "Este quiz não está mais disponível para você.";
            return RedirectToRoute("Quizzes_Index");
        }

        var inicio = DateTime.Parse(inicioIso, null, System.Globalization.DateTimeStyles.RoundtripKind);
        var resultado = _service.CorrigirESalvar(id, usuarioId, model, inicio);
        TempData["toast"] = $"Quiz concluído! Nota: {resultado.NotaPercentual}%";

        var tentativaId = _db.QuizTentativas
            .Where(t => t.QuizId == id && t.UsuarioId == usuarioId)
            .OrderByDescending(t => t.Id)
            .Select(t => t.Id)
            .FirstOrDefault();

        return RedirectToAction("Resultado", new { tentativaId });
    }

    // GET /Quizzes/resultado?tentativaId=...
    [HttpGet("resultado", Name = "Quizzes_Resultado")]
    public IActionResult Resultado(int tentativaId)
    {
        var t = _db.QuizTentativas.Include(x => x.Quiz).FirstOrDefault(x => x.Id == tentativaId);
        if (t == null) return NotFound();

        var vm = new QuizResultadoVM
        {
            QuizId = t.QuizId,
            Titulo = t.Quiz.Titulo,
            PontosObtidos = t.PontosObtidos,
            PontosMaximos = t.PontosMaximos,
            NotaPercentual = t.NotaPercentual,
            TempoGasto = (t.Fim ?? DateTime.Now) - t.Inicio
        };
        return View(vm);
    }
}
