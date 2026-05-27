namespace PortalHelpdeskTI.Models
{
    public class CategoriaChamado
    {
        public int Id { get; set; }
        public string Nome { get; set; }

        public int TipoChamadoId { get; set; }
        public TipoChamado TipoChamado { get; set; }

        public ICollection<SubcategoriaChamado> Subcategorias { get; set; } = new List<SubcategoriaChamado>();
    }
}
