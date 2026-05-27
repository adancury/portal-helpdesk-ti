
namespace PortalHelpdeskTI.Models
{
    public class TipoChamado
    {
        public int Id { get; set; }
        public string Nome { get; set; }

        public ICollection<CategoriaChamado> Categorias { get; set; } = new List<CategoriaChamado>();
    }
}

