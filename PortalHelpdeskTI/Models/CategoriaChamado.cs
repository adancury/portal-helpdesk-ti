namespace PortalHelpdeskTI.Models
{
    public class CategoriaChamado
    {
        public int Id { get; set; }
        public string Nome { get; set; }
        public ICollection<SubcategoriaChamado> Subcategorias { get; set; }
    }

}
