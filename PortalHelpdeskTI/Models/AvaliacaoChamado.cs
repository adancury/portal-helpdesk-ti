using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace PortalHelpdeskTI.Models
{
    [Table("AvaliacoesChamado")] // <- garante o nome da tabela
    public class AvaliacaoChamado
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [BindNever]
        public int Id { get; set; }

        [ForeignKey(nameof(Chamado))]
        public int ChamadoId { get; set; }

        [ForeignKey(nameof(Usuario))]
        public int UsuarioId { get; set; }

        [Required]
        [Range(1, 5)]
        public int Nota { get; set; }

        [MaxLength(1000)]
        public string? Comentario { get; set; }

        public DateTime DataAvaliacao { get; set; }  // você já está setando no Controller

        public Chamado? Chamado { get; set; }
        public Usuario? Usuario { get; set; }
    }
}

