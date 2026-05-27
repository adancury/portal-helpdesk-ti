using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PortalHelpdeskTI.Models.Comissoes
{
    [Table("ComissaoVendedor")]
    public class ComissaoVendedor
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int SlpCode { get; set; } // OSLP.SlpCode

        [Required, StringLength(120)]
        public string SlpName { get; set; } = "";

        /// <summary>
        /// Percentual em forma decimal. Ex.: 0.0449 (= 4,49%)
        /// </summary>
        [Column(TypeName = "decimal(9,6)")]
        public decimal Percentual { get; set; }

        public bool Ativo { get; set; } = true;
        public string BaseCalculo { get; set; } = "FATURAMENTO";
        public string TipoVendedor { get; set; } = "REPRESENTANTE";
        public string? Email { get; set; }
        public bool ParticipaRelatorio { get; set; } = true;
        public bool DestacarIR { get; set; } = true;
    }
}
