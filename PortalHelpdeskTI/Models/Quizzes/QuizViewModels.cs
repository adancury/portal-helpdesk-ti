// Caminho: ViewModels/Quizzes/QuizViewModels.cs
using System;
using System.Collections.Generic;
using PortalHelpdeskTI.Models.Quizzes;

namespace PortalHelpdeskTI.ViewModels.Quizzes
{
    public class QuizListItemVM
    {
        public int Id { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public string? Categoria { get; set; }
        public bool Publicado { get; set; }
        public int TotalQuestoes { get; set; }

        // >>> novos campos para controle de disponibilidade
        public int TentativasUsadas { get; set; }
        public bool Aprovado { get; set; }
        public bool Disponivel { get; set; }
    }

    public class QuizTakeVM
    {
        public int QuizId { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public string? Descricao { get; set; }
        public int? TempoLimiteSegundos { get; set; }
        public List<QuizTakeQuestaoVM> Questoes { get; set; } = new();
    }

    public class QuizTakeQuestaoVM
    {
        public int QuestaoId { get; set; }
        public string Enunciado { get; set; } = string.Empty;
        public TipoQuestao Tipo { get; set; }
        public List<QuizTakeOpcaoVM> Opcoes { get; set; } = new();
    }

    public class QuizTakeOpcaoVM
    {
        public int OpcaoId { get; set; }
        public string Texto { get; set; } = string.Empty;
    }

    public class QuizSubmitVM
    {
        public int QuizId { get; set; }
        public Dictionary<int, List<int>> Respostas { get; set; } = new();
    }

    public class QuizResultadoVM
    {
        public int QuizId { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public int PontosObtidos { get; set; }
        public int PontosMaximos { get; set; }
        public decimal NotaPercentual { get; set; }
        public TimeSpan TempoGasto { get; set; }
    }
}
