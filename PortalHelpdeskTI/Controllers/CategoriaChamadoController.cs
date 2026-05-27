namespace PortalHelpdeskTI.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.EntityFrameworkCore;
    using PortalHelpdeskTI.Models;

    public class CategoriaChamadoController : Controller
    {
        private readonly AppDbContext _context;

        public CategoriaChamadoController(AppDbContext context)
        {
            _context = context;
        }

        // GET: /CategoriaChamado/ListarCategorias
        [HttpGet]
        public IActionResult ListarCategorias()
        {
            var tipos = _context.TipoChamado
                .Include(t => t.Categorias)
                    .ThenInclude(c => c.Subcategorias)
                .OrderBy(t => t.Nome)
                .ToList();

            return View(tipos);
        }
        public class CategoriaEdicaoDTO
        {
            public int Id { get; set; }
            public string TipoNome { get; set; }
            public string CategoriaNome { get; set; }
        }

    }

}
