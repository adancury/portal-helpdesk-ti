// Caminho: Services/Quizzes/QuizService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

// >>> AJUSTE este using para o namespace REAL do seu AppDbContext <<<
using PortalHelpdeskTI; // troque se seu AppDbContext estiver em outro namespace

using PortalHelpdeskTI.Models.Quizzes;

namespace PortalHelpdeskTI.Services.Quizzes
{
    public class QuizService : IQuizService
    {
        private readonly AppDbContext _db;
        private readonly Random _rng = new();

        public QuizService(AppDbContext db) => _db = db;

        public PortalHelpdeskTI.ViewModels.Quizzes.QuizTakeVM MontarQuizParaExecucao(int quizId, int? seed = null)
        {
            var quiz = _db.Quizzes
                .Include(q => q.Questoes)
                    .ThenInclude(q => q.Opcoes)
                .FirstOrDefault(q => q.Id == quizId && q.Publicado)
                ?? throw new InvalidOperationException("Quiz não encontrado ou não publicado.");

            var questoes = quiz.Questoes.OrderBy(q => q.Ordem).ToList();
            if (quiz.EmbaralharQuestoes)
            {
                var rng = seed.HasValue ? new Random(seed.Value) : _rng;
                questoes = Shuffle(questoes, rng).ToList();
            }

            return new PortalHelpdeskTI.ViewModels.Quizzes.QuizTakeVM
            {
                QuizId = quiz.Id,
                Titulo = quiz.Titulo,
                Descricao = quiz.Descricao,
                TempoLimiteSegundos = quiz.TempoLimiteSegundos,
                Questoes = questoes.Select(q => new PortalHelpdeskTI.ViewModels.Quizzes.QuizTakeQuestaoVM
                {
                    QuestaoId = q.Id,
                    Enunciado = q.Enunciado,
                    Tipo = q.Tipo,
                    Opcoes = q.Opcoes
                        .OrderBy(o => o.Ordem)
                        .Select(o => new PortalHelpdeskTI.ViewModels.Quizzes.QuizTakeOpcaoVM
                        {
                            OpcaoId = o.Id,
                            Texto = o.Texto
                        })
                        .ToList()
                }).ToList()
            };
        }

        public PortalHelpdeskTI.ViewModels.Quizzes.QuizResultadoVM CorrigirESalvar(
            int quizId,
            int usuarioId,
            PortalHelpdeskTI.ViewModels.Quizzes.QuizSubmitVM sub,
            DateTime inicio)
        {
            var quiz = _db.Quizzes
                .Include(q => q.Questoes)
                    .ThenInclude(q => q.Opcoes)
                .FirstOrDefault(q => q.Id == quizId)
                ?? throw new InvalidOperationException("Quiz não encontrado.");

            var tentativa = new QuizTentativa
            {
                QuizId = quiz.Id,
                UsuarioId = usuarioId,
                Inicio = inicio,
                Fim = DateTime.Now
            };

            _db.QuizTentativas.Add(tentativa);
            _db.SaveChanges();

            int pontos = 0, max = 0;

            foreach (var questao in quiz.Questoes)
            {
                max += 1;

                var corretas = questao.Opcoes.Where(o => o.Correta)
                                             .Select(o => o.Id)
                                             .OrderBy(x => x)
                                             .ToList();

                sub.Respostas.TryGetValue(questao.Id, out var marcadas);
                marcadas ??= new List<int>();

                foreach (var idOpcao in marcadas)
                {
                    _db.QuizRespostas.Add(new QuizResposta
                    {
                        QuizTentativaId = tentativa.Id,
                        QuizQuestaoId = questao.Id,
                        QuizOpcaoId = idOpcao
                    });
                }

                if (corretas.SequenceEqual(marcadas.OrderBy(x => x)))
                    pontos += 1;
            }

            tentativa.PontosObtidos = pontos;
            tentativa.PontosMaximos = max;
            tentativa.NotaPercentual = max == 0 ? 0 : Math.Round((decimal)pontos / max * 100m, 2);

            _db.SaveChanges();

            return new PortalHelpdeskTI.ViewModels.Quizzes.QuizResultadoVM
            {
                QuizId = quiz.Id,
                Titulo = quiz.Titulo,
                PontosObtidos = tentativa.PontosObtidos,
                PontosMaximos = tentativa.PontosMaximos,
                NotaPercentual = tentativa.NotaPercentual,
                TempoGasto = (tentativa.Fim ?? DateTime.Now) - tentativa.Inicio
            };
        }

        private static List<T> Shuffle<T>(IList<T> list, Random rng)
        {
            var arr = list.ToList();
            for (int i = arr.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }
            return arr;
        }
    }
}
