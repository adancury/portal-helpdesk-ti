namespace PortalHelpdeskTI.Models
{
    public class SubcategoriaChamado
    {
        public int Id { get; set; }
        public string Nome { get; set; }

        public int CategoriaId { get; set; }
        public CategoriaChamado Categoria { get; set; }
    }
}
