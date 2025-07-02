using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

public class AccountController : Controller
{
    private readonly AppDbContext _context;

    public AccountController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public IActionResult Login()
    {
        return View();
    }

    [HttpPost]
    public IActionResult Login(string email, string senha)
    {
        var user = _context.Usuarios.FirstOrDefault(u => u.Email == email && u.SenhaHash == senha);
        if (user != null)
        {
            HttpContext.Session.SetInt32("UsuarioId", user.Id);

            if (!string.IsNullOrEmpty(user.Perfil) &&
                (user.Perfil.Equals("Técnico", StringComparison.OrdinalIgnoreCase) ||
                 user.Perfil.Equals("Tecnico", StringComparison.OrdinalIgnoreCase)))
            {
                return RedirectToAction("TecnicoPainel", "Chamados");
            }
            else
            {
                return RedirectToAction("Index", "Chamados");
            }
        }

        ViewBag.Erro = "Login inválido.";
        return View();
    }

    public IActionResult Logoff()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Login", "Account");
    }
}
