using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PortalHelpdeskTI;
using PortalHelpdeskTI.Models.Quizzes;
using PortalHelpdeskTI.ViewModels.Quizzes;

namespace PortalHelpdeskTI.Controllers
{
    [Route("Admin/Quizzes")]
    public class AdminQuizzesController : Controller
    {
        private readonly AppDbContext _db;
        public AdminQuizzesController(AppDbContext db) => _db = db;

        private bool IsAjax()
        {
            var header = Request.Headers["X-Requested-With"].ToString();
            return !string.IsNullOrEmpty(header) &&
                   header.Equals("XMLHttpRequest", System.StringComparison.OrdinalIgnoreCase);
        }

        private void SetEmbedFlag(bool embed)
        {
            if (embed || IsAjax()) ViewBag.Embed = true;
        }

        // Remove validação/binding de propriedades de navegação (Questoes/Opcoes)
        private void SanitizeModelStateNavProps()
        {
            var keysToRemove = ModelState.Keys
                .Where(k =>
                    k.StartsWith("Questoes", System.StringComparison.OrdinalIgnoreCase) ||
                    k.StartsWith("Opcoes", System.StringComparison.OrdinalIgnoreCase) ||
                    k.Contains(".Questoes") ||
                    k.Contains(".Opcoes"))
                .ToList();

            foreach (var k in keysToRemove)
                ModelState.Remove(k);
        }

        // Mantém apenas chaves do "cabeçalho" do quiz no ModelState
        private static readonly HashSet<string> _headerKeys = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "Id","Titulo","Descricao","Categoria","TempoLimiteSegundos","NotaCortePercentual","Publicado","EmbaralharQuestoes"
        };

        private void RestrictModelStateToHeader()
        {
            var keys = ModelState.Keys.ToList();
            foreach (var k in keys)
            {
                var root = k;
                var dot = root.IndexOf('.');
                if (dot >= 0) root = root.Substring(0, dot);
                var bracket = root.IndexOf('[');
                if (bracket >= 0) root = root.Substring(0, bracket);

                if (!_headerKeys.Contains(root))
                    ModelState.Remove(k);
            }
        }

        // Normaliza numéricos opcionais (aceita vírgula/ponto e vazio)
        private void NormalizeOptionalNumerics(Quiz model)
        {
            // TempoLimiteSegundos (int?)
            var rawTempo = Request.Form[nameof(model.TempoLimiteSegundos)].ToString();
            if (string.IsNullOrWhiteSpace(rawTempo))
            {
                ModelState.Remove(nameof(model.TempoLimiteSegundos));
                model.TempoLimiteSegundos = null;
            }
            else if (ModelState.TryGetValue(nameof(model.TempoLimiteSegundos), out var stTempo) && stTempo.Errors.Count > 0)
            {
                if (int.TryParse(rawTempo, NumberStyles.Integer, CultureInfo.CurrentCulture, out var i) ||
                    int.TryParse(rawTempo, NumberStyles.Integer, CultureInfo.InvariantCulture, out i))
                {
                    ModelState.Remove(nameof(model.TempoLimiteSegundos));
                    model.TempoLimiteSegundos = i;
                }
            }

            // NotaCortePercentual (decimal?)
            var rawNota = Request.Form[nameof(model.NotaCortePercentual)].ToString();
            if (string.IsNullOrWhiteSpace(rawNota))
            {
                ModelState.Remove(nameof(model.NotaCortePercentual));
                model.NotaCortePercentual = null;
            }
            else if (ModelState.TryGetValue(nameof(model.NotaCortePercentual), out var stNota) && stNota.Errors.Count > 0)
            {
                if (decimal.TryParse(rawNota, NumberStyles.Number, CultureInfo.CurrentCulture, out var d) ||
                    decimal.TryParse(rawNota.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out d) ||
                    decimal.TryParse(rawNota.Replace('.', ','), NumberStyles.Number, new CultureInfo("pt-BR"), out d))
                {
                    ModelState.Remove(nameof(model.NotaCortePercentual));
                    model.NotaCortePercentual = d;
                }
            }
        }

        private void PushModelErrorsToViewBag()
        {
            var erros = ModelState
                .Where(kvp => kvp.Value.Errors.Count > 0)
                .Select(kvp => $"{kvp.Key}: {string.Join("; ", kvp.Value.Errors.Select(e => e.ErrorMessage))}")
                .ToList();

            if (erros.Count > 0)
                ViewBag.ModelErrors = erros;
        }

        [HttpGet("")]
        public IActionResult Index(bool embed = false)
        {
            SetEmbedFlag(embed);

            var lista = _db.Quizzes
                .Include(q => q.Questoes)
                .OrderByDescending(q => q.Id)
                .Select(q => new QuizListItemVM
                {
                    Id = q.Id,
                    Titulo = q.Titulo,
                    Categoria = q.Categoria,
                    Publicado = q.Publicado,
                    TotalQuestoes = q.Questoes.Count
                }).ToList();

            return View("Index", lista);
        }

        [HttpGet("novo")]
        public IActionResult Novo(bool embed = false)
        {
            SetEmbedFlag(embed);
            return View("Novo", new Quiz());
        }

        [HttpPost("novo")]
        [ValidateAntiForgeryToken]
        public IActionResult Novo(Quiz model, bool embed = false)
        {
            SanitizeModelStateNavProps();
            RestrictModelStateToHeader();
            NormalizeOptionalNumerics(model);

            if (!ModelState.IsValid)
            {
                SetEmbedFlag(embed);
                PushModelErrorsToViewBag();
                return View("Novo", model);
            }

            _db.Quizzes.Add(model);
            _db.SaveChanges();
            TempData["toast"] = "Quiz criado";

            // Agora volta para a lista
            return RedirectToAction(nameof(Index));
        }


        [HttpGet("editar/{id:int}")]
        public IActionResult Editar(int id, bool embed = false, string step = null)
        {
            SetEmbedFlag(embed);

            var quiz = _db.Quizzes
                .Include(q => q.Questoes)
                    .ThenInclude(q => q.Opcoes)
                .FirstOrDefault(q => q.Id == id);

            if (quiz == null) return NotFound();

            ViewBag.Step = step; // habilita seção de questões na view
            return View("Editar", quiz);
        }

        [HttpPost("editar/{id:int}")]
        [ValidateAntiForgeryToken]
        public IActionResult Editar(
        int id,
        [Bind("Id,Titulo,Descricao,Categoria,TempoLimiteSegundos,NotaCortePercentual,Publicado,EmbaralharQuestoes")]
        Quiz model,
        bool embed = false)
        {
            if (id != model.Id) return BadRequest();

            SanitizeModelStateNavProps();
            RestrictModelStateToHeader();
            NormalizeOptionalNumerics(model);

            if (!ModelState.IsValid)
            {
                SetEmbedFlag(embed);
                PushModelErrorsToViewBag();
                return View("Editar", model);
            }

            var entity = _db.Quizzes.FirstOrDefault(q => q.Id == id);
            if (entity == null) return NotFound();

            entity.Titulo = model.Titulo;
            entity.Descricao = model.Descricao;
            entity.Categoria = model.Categoria;
            entity.TempoLimiteSegundos = model.TempoLimiteSegundos;
            entity.NotaCortePercentual = model.NotaCortePercentual;
            entity.Publicado = model.Publicado;
            entity.EmbaralharQuestoes = model.EmbaralharQuestoes;

            _db.SaveChanges();
            TempData["toast"] = "Quiz salvo";

            // Agora volta para a lista
            return RedirectToAction(nameof(Index));
        }


        [HttpPost("{quizId:int}/adicionar-questao")]
        [ValidateAntiForgeryToken]
        public IActionResult AdicionarQuestao(int quizId, QuizQuestao q, bool embed = false)
        {
            q.QuizId = quizId;
            _db.QuizQuestoes.Add(q);
            _db.SaveChanges();

            return RedirectToAction("Editar", new { id = quizId, embed = embed || IsAjax(), step = "questions" });
        }

        [HttpPost("questao/{questaoId:int}/remover")]
        [ValidateAntiForgeryToken]
        public IActionResult RemoverQuestao(int questaoId, bool embed = false)
        {
            var questao = _db.QuizQuestoes.Find(questaoId);
            if (questao == null) return NotFound();

            int quizId = questao.QuizId;
            _db.QuizQuestoes.Remove(questao);
            _db.SaveChanges();

            return RedirectToAction("Editar", new { id = quizId, embed = embed || IsAjax(), step = "questions" });
        }

        [HttpPost("questao/{questaoId:int}/adicionar-opcao")]
        [ValidateAntiForgeryToken]
        public IActionResult AdicionarOpcao(int questaoId, QuizOpcao o, bool embed = false)
        {
            o.QuizQuestaoId = questaoId;
            _db.QuizOpcoes.Add(o);
            _db.SaveChanges();

            var quizId = _db.QuizQuestoes.Where(q => q.Id == questaoId).Select(q => q.QuizId).First();
            return RedirectToAction("Editar", new { id = quizId, embed = embed || IsAjax(), step = "questions" });
        }

        [HttpPost("opcao/{opcaoId:int}/remover")]
        [ValidateAntiForgeryToken]
        public IActionResult RemoverOpcao(int opcaoId, bool embed = false)
        {
            var opc = _db.QuizOpcoes.Find(opcaoId);
            if (opc == null) return NotFound();

            int quizId = _db.QuizQuestoes.Where(q => q.Id == opc.QuizQuestaoId).Select(q => q.QuizId).First();
            _db.QuizOpcoes.Remove(opc);
            _db.SaveChanges();

            return RedirectToAction("Editar", new { id = quizId, embed = embed || IsAjax(), step = "questions" });
        }
    }
}
