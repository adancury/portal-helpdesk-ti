using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

public class ChamadosController : Controller
{
    private readonly AppDbContext _context;

    public ChamadosController(AppDbContext context)
    {
        _context = context;
    }

    public IActionResult Index()
    {
        var usuarioId = HttpContext.Session.GetInt32("UsuarioId");
        if (usuarioId == null)
        {
            return RedirectToAction("Login", "Account");
        }

        var chamados = _context.Chamados
            .Include(c => c.Usuario)
            .Where(c => c.UsuarioId == usuarioId.Value)
            .ToList();

        return View(chamados);
    }

    public IActionResult Novo()
    {
        return View();
    }

    [HttpPost]
    public IActionResult Novo(Chamado chamado)
    {
        var usuarioId = HttpContext.Session.GetInt32("UsuarioId");
        if (usuarioId == null)
        {
            return RedirectToAction("Login", "Account");
        }

        chamado.DataAbertura = DateTime.Now;
        chamado.Status = "Aberto";
        chamado.UsuarioId = usuarioId.Value;

        _context.Chamados.Add(chamado);
        _context.SaveChanges();

        return RedirectToAction("Index");
    }

    public IActionResult TecnicoPainel()
    {
        var chamados = _context.Chamados
            .Include(c => c.Usuario)
            .Where(c => c.Status != "Concluído")
            .ToList();

        return View(chamados);
    }

    public IActionResult Atender(int id)
    {
        var chamado = _context.Chamados
            .Include(c => c.Usuario)
            .Include(c => c.Interacoes)
            .ThenInclude(i => i.Usuario)
            .FirstOrDefault(c => c.Id == id);

        if (chamado == null) return NotFound();

        var tecnicos = _context.Usuarios
            .Where(u => u.Perfil == "Tecnico" || u.Perfil == "Técnico")
            .Select(u => new { u.Id, u.Nome })
            .ToList();

        ViewBag.Tecnicos = tecnicos;

        return View(chamado);
    }


    [HttpPost]
    public IActionResult Atender(int id, string novoStatus, string interacaoMensagem, int? tecnicoResponsavelId)
    {
        var chamado = _context.Chamados.FirstOrDefault(c => c.Id == id);
        if (chamado == null) return NotFound();

        var usuarioId = HttpContext.Session.GetInt32("UsuarioId");
        if (usuarioId == null)
        {
            return RedirectToAction("Login", "Account");
        }

        chamado.Status = novoStatus;

        if (tecnicoResponsavelId.HasValue && _context.Usuarios.Any(u => u.Id == tecnicoResponsavelId.Value))
        {
            chamado.TecnicoId = tecnicoResponsavelId.Value;
        }
        else
        {
            chamado.TecnicoId = null; // Evita conflito com FK
        }

        if (!string.IsNullOrWhiteSpace(interacaoMensagem))
        {
            _context.Interacoes.Add(new Interacao
            {
                ChamadoId = id,
                UsuarioId = usuarioId.Value,
                Data = DateTime.Now,
                Mensagem = interacaoMensagem
            });
        }

        _context.SaveChanges();

        return RedirectToAction("Atender", new { id = id });
    }


    [HttpPost]
    public IActionResult Assumir(int id)
    {
        var chamado = _context.Chamados.FirstOrDefault(c => c.Id == id);
        if (chamado == null) return NotFound();

        var usuarioId = HttpContext.Session.GetInt32("UsuarioId");
        if (usuarioId != null)
        {
            chamado.TecnicoId = usuarioId.Value;
            chamado.Status = "Em Atendimento";
            _context.SaveChanges();
        }

        return RedirectToAction("Atender", new { id = id });
    }

    public IActionResult Detalhes(int id)
    {
        var chamado = _context.Chamados
            .Include(c => c.Usuario)
            .Include(c => c.Interacoes)
            .ThenInclude(i => i.Usuario)
            .FirstOrDefault(c => c.Id == id);

        if (chamado == null) return NotFound();

        return View(chamado);
    }
}
