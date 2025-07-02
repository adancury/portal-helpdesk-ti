using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PortalHelpdeskTI.Models;

public class ChamadosController : Controller
{
    private readonly AppDbContext _context;

    public ChamadosController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public IActionResult Novo()
    {
        ViewBag.Tipos = _context.TipoChamado.OrderBy(t => t.Nome).ToList();
        ViewBag.Categorias = _context.CategoriaChamado.OrderBy(c => c.Nome).ToList();
        ViewBag.Subcategorias = _context.SubcategoriaChamado.ToList();
        return View();
    }

    [HttpPost]
    public IActionResult Novo(Chamado chamado)
    {
        if (ModelState.IsValid)
        {
            chamado.DataAbertura = DateTime.Now;
            chamado.Status = "Aberto";

            var usuarioId = HttpContext.Session.GetInt32("UsuarioId");
            if (usuarioId == null)
            {
                ModelState.AddModelError("", "Usuário não autenticado.");
                ViewBag.Tipos = _context.TipoChamado.ToList();
                ViewBag.Categorias = _context.CategoriaChamado.ToList();
                ViewBag.Subcategorias = _context.SubcategoriaChamado.ToList();
                return View(chamado);
            }

            chamado.UsuarioId = usuarioId.Value;

            _context.Chamados.Add(chamado);
            _context.SaveChanges();

            return RedirectToAction("TecnicoPainel"); // Ou a action desejada após sucesso
        }

        // Se inválido, repopula os drop-downs
        ViewBag.Tipos = _context.TipoChamado.ToList();
        ViewBag.Categorias = _context.CategoriaChamado.ToList();
        ViewBag.Subcategorias = _context.SubcategoriaChamado.ToList();

        return View(chamado);
    }

    // Exemplo para popular subcategorias via Ajax
    public IActionResult SubcategoriasPorCategoria(int categoriaId)
    {
        var subcategorias = _context.SubcategoriaChamado
            .Where(s => s.CategoriaId == categoriaId)
            .Select(s => new { s.Id, s.Nome })
            .ToList();
        return Json(subcategorias);
    }
    public IActionResult TecnicoPainel()
    {
        var chamados = _context.Chamados
    .Include(c => c.Usuario)
    .Include(c => c.Tecnico)
    .Where(c => c.Status != "Concluído")
    .ToList();

        return View(chamados);
    }
    public IActionResult Index()
    {
        var chamados = _context.Chamados
            .Include(c => c.Usuario)
            .ToList();

        return View(chamados);
    }

}
