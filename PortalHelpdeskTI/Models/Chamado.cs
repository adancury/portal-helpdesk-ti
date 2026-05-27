using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PortalHelpdeskTI.Models
{
    public class Chamado
    {
        public int Id { get; set; }

        [Required]
        public string Titulo { get; set; }

        [Required]
        public string Descricao { get; set; }

        [Required]
        public string Prioridade { get; set; }

        public DateTime DataAbertura { get; set; }

        public string Status { get; set; }

        [Required]
        public int UsuarioId { get; set; }

        [ForeignKey("UsuarioId")]
        public Usuario Usuario { get; set; }

        public int? TecnicoId { get; set; }

        [ForeignKey("TecnicoId")]
        public Usuario Tecnico { get; set; }

        [Required(ErrorMessage = "Tipo de chamado é obrigatório.")]
        public int? TipoChamadoId { get; set; }

        [ForeignKey("TipoChamadoId")]
        public TipoChamado TipoChamado { get; set; }

        [Required(ErrorMessage = "Categoria é obrigatória.")]
        public int? CategoriaId { get; set; }

        [ForeignKey("CategoriaId")]
        public CategoriaChamado Categoria { get; set; }

        [Required(ErrorMessage = "Subcategoria é obrigatória.")]
        public int? SubcategoriaId { get; set; }

        [ForeignKey("SubcategoriaId")]
        public SubcategoriaChamado Subcategoria { get; set; }

        public bool VisualizadoPeloTecnico { get; set; } = false;

        public bool VisualizadoPeloSolicitante { get; set; }

        public string? Solucao { get; set; }

        public ICollection<Interacao> Interacoes { get; set; } = new List<Interacao>();
        public DateTime? DataConclusao { get; set; }

        public ICollection<Anexo> Anexos { get; set; } = new List<Anexo>();
        public bool AvaliacaoLembreteEnviado { get; set; } = false;
        // --- Cálculo de SLA ---
        [NotMapped]
        public string StatusSLA { get; set; } // “Dentro do Prazo”, “Fora do Prazo”, “Em andamento”
        [NotMapped]
        public double PercentualProgressoSLA { get; set; } // 0–100%
                                                           // Models/Chamado.cs
        [NotMapped]
        public int SlaHorasTotal { get; set; } // total de horas do SLA de resolução
        [NotMapped] public double SlaHorasConsumidas { get; set; } // horas úteis consumidas
        [NotMapped] public DateTime? PrazoFinalUtil { get; set; }  // prazo em horas úteis

    }
}
