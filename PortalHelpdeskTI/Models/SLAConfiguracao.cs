using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PortalHelpdeskTI.Models
{
    public class SLAConfiguracao
    {
        [Key]
        public int Id { get; set; }

        // FK para CategoriaChamado (ajuste o nome se sua categoria for outra)
        public int CategoriaId { get; set; }

        public int TempoRespostaHoras { get; set; }  // opcional (se for usar)
        public int TempoResolucaoHoras { get; set; } // base do SLA (usado no cálculo)

        [ForeignKey(nameof(CategoriaId))]
        public CategoriaChamado? Categoria { get; set; }
        public int? SubcategoriaId { get; set; }
        public SubcategoriaChamado? Subcategoria { get; set; }

    }
}
