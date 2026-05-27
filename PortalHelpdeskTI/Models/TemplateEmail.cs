using System.ComponentModel.DataAnnotations;

namespace PortalHelpdeskTI.Models
{
    public class TemplateEmail
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Tipo { get; set; }  // Ex: "AtualizacaoChamado"

        [MaxLength(255)]
        public string Assunto { get; set; }

        [Required]
        public string CorpoHtml { get; set; }
    }
}
