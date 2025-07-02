using Microsoft.AspNetCore.Mvc;
public class ChamadosController : Controller
{
    private readonly AppDbContext _context;
    public ChamadosController(AppDbContext context)
    {
        _context = context;
    }

    public IActionResult Index()
    {
        var chamados = _context.Chamados.ToList();
        return View(chamados);
    }

    public IActionResult Novo() => View();

    [HttpPost]
    public IActionResult Novo(Chamado chamado)
    {
        chamado.DataAbertura = DateTime.Now;
        chamado.Status = "Aberto";
        _context.Chamados.Add(chamado);
        _context.SaveChanges();
        return RedirectToAction("Index");
    }

    public IActionResult Detalhes(int id)
    {
        var chamado = _context.Chamados.FirstOrDefault(c => c.Id == id);
        return View(chamado);
    }
}
