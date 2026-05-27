// Caminho: Models/Quizzes/QuizModels.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace PortalHelpdeskTI.Models.Quizzes
{
    public enum TipoQuestao
    {
        UnicaEscolha = 1,
        MultiplaEscolha = 2,
        VerdadeiroFalso = 3
    }

    public class Quiz
    {
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Titulo { get; set; } = string.Empty;

        public string? Descricao { get; set; }

        public bool Publicado { get; set; }

        [MaxLength(100)]
        public string? Categoria { get; set; }

        /// <summary>Tempo limite opcional em segundos (null = sem limite).</summary>
        public int? TempoLimiteSegundos { get; set; }

        /// <summary>Se verdadeiro, embaralha a ordem das questões.</summary>
        public bool EmbaralharQuestoes { get; set; } = true;

        /// <summary>Nota mínima (0..100). Se atingida, bloqueia novas tentativas.</summary>
        public decimal? NotaCortePercentual { get; set; }

        /// <summary>Lista de questões do quiz.</summary>
        [ValidateNever]
        public ICollection<QuizQuestao> Questoes { get; set; } = new List<QuizQuestao>();
    }

    public class QuizQuestao
    {
        public int Id { get; set; }

        public int QuizId { get; set; }

        [ValidateNever]
        public Quiz Quiz { get; set; } = null!;

        [Required]
        public string Enunciado { get; set; } = string.Empty;

        public TipoQuestao Tipo { get; set; }

        public int Ordem { get; set; }

        [ValidateNever]
        public ICollection<QuizOpcao> Opcoes { get; set; } = new List<QuizOpcao>();
    }

    public class QuizOpcao
    {
        public int Id { get; set; }

        public int QuizQuestaoId { get; set; }

        [ValidateNever]
        public QuizQuestao Questao { get; set; } = null!;

        [Required]
        public string Texto { get; set; } = string.Empty;

        public bool Correta { get; set; }

        public int Ordem { get; set; }
    }

    public class QuizTentativa
    {
        public int Id { get; set; }

        public int QuizId { get; set; }

        [ValidateNever]
        public Quiz Quiz { get; set; } = null!;

        public int UsuarioId { get; set; }

        public DateTime Inicio { get; set; }
        public DateTime? Fim { get; set; }

        public int PontosObtidos { get; set; }
        public int PontosMaximos { get; set; }
        public decimal NotaPercentual { get; set; }
    }

    public class QuizResposta
    {
        public int Id { get; set; }

        public int QuizTentativaId { get; set; }

        [ValidateNever]
        public QuizTentativa Tentativa { get; set; } = null!;

        public int QuizQuestaoId { get; set; }

        public int? QuizOpcaoId { get; set; }
    }
}
