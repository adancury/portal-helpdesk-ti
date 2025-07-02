using Microsoft.AspNetCore.Mvc;
public class AccountController : Controller
{
    private readonly AppDbContext _context;
    public AccountController(AppDbContext context)
    {
        _context = context;
    }

    public IActionResult Login() => View();

    [HttpPost]
    public IActionResult Login(string email, string senha)
    {
        var user = _context.Usuarios.FirstOrDefault(u => u.Email == email && u.SenhaHash == senha);
        if (user != null)
        {
            // Aqui deveria haver autenticação real com Identity
            return RedirectToAction("Index", "Home");
        }
        ViewBag.Erro = "Login inválido.";
        return View();
    }
}
