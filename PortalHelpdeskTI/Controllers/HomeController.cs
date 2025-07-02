using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

public class HomeController : Controller
{
    private readonly AppDbContext _context;

    public HomeController(AppDbContext context)
    {
        _context = context;
    }

    public IActionResult Index(string statusFiltro = null)
    {
        var usuarioId = HttpContext.Session.GetInt32("UsuarioId");
        if (usuarioId == null)
        {
            return RedirectToAction("Login", "Account");
        }

        var query = _context.Chamados
            .Include(c => c.Usuario)
            .Where(c => c.UsuarioId == usuarioId.Value);

        if (!string.IsNullOrEmpty(statusFiltro))
        {
            query = query.Where(c => c.Status == statusFiltro);
        }

        var chamados = query.ToList();

        ViewBag.Nome = _context.Usuarios.FirstOrDefault(u => u.Id == usuarioId.Value)?.Nome ?? "Usuário";
        ViewBag.StatusFiltro = statusFiltro;

        return View(chamados);
    }

}
