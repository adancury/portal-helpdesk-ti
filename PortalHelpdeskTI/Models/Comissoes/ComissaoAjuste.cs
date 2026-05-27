using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PortalHelpdeskTI.Models.Comissoes
{
    [Table("ComissaoAjuste")]
    public class ComissaoAjuste
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int SlpCode { get; set; }

        [Column(TypeName = "date")]
        public DateTime DataIni { get; set; }

        [Column(TypeName = "date")]
        public DateTime DataFim { get; set; }

        /// <summary>
        /// "IR" ou "DESCONTO_CONDICIONADO"
        /// </summary>
        [Required, StringLength(40)]
        public string Tipo { get; set; } = "IR";

        [Column(TypeName = "decimal(19,6)")]
        public decimal Valor { get; set; }

        [StringLength(500)]
        public string? Observacao { get; set; }

        [StringLength(200)]
        public string? NFsRelacionadas { get; set; }
    }
}
