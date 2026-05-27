using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PortalHelpdeskTI.Models;
using PortalHelpdeskTI.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;

[AllowAnonymous]
public class AccountController : Controller
{
    private readonly AppDbContext _context;
    private readonly IEmailService _emailService;


    public AccountController(AppDbContext context, IEmailService emailService)
    {
        _context = context;
        _emailService = emailService;
    }

    [HttpGet]
    public IActionResult AcessoNegado()
    {
        return View();
    }

    [HttpGet]
    public IActionResult Login()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken] // opcional mas recomendado
    public async Task<IActionResult> Login(string email, string senha)
    {
        var emailNorm = (email ?? "").Trim().ToLowerInvariant();

        // Busca por e-mail normalizado
        var user = _context.Usuarios.FirstOrDefault(u => u.Email.ToLower() == emailNorm);

        // Usuário inexistente ou senha inválida
        if (user == null || !Senhas.Verificar(senha ?? "", user.SenhaHash))
        {
            ViewBag.Erro = "Login inválido.";
            ViewBag.Email = email;
            return View();
        }

        // Bloqueia acesso se ainda não ativou por e-mail (ou usuário está inativo)
        if (!user.Ativo || !user.EmailConfirmado)
        {
            ViewBag.Erro = "Conta não ativada. Verifique seu e-mail ou solicite reenvio da ativação.";
            ViewBag.Email = email;
            ViewBag.ShowAtivacao = true;
            return View();
        }

        // >>>>>>>>>> NOVO: Autenticação por cookies (mantendo sua Session também)
        var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Nome ?? user.Email),
        new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
        new Claim(ClaimTypes.Role, user.Perfil ?? "Usuario")
    };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,                         // “lembrar”
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8),
                AllowRefresh = true
            });

        // (opcional) Mantém sua sessão legada
        HttpContext.Session.SetInt32("UsuarioId", user.Id);
        HttpContext.Session.SetString("Perfil", user.Perfil ?? string.Empty);

        // Redireciona conforme perfil
        if (!string.IsNullOrEmpty(user.Perfil) &&
            (user.Perfil.Equals("Técnico", StringComparison.OrdinalIgnoreCase) ||
             user.Perfil.Equals("Tecnico", StringComparison.OrdinalIgnoreCase)))
        {
            //return RedirectToAction("TecnicoPainel", "Chamados");
            return RedirectToAction("Index", "Inicio");
        }
        else
        {
            //return RedirectToAction("Index", "Chamados");
            return RedirectToAction("Index", "Inicio");
        }
    }

    public async Task<IActionResult> Logoff()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        HttpContext.Session.Clear();
        return RedirectToAction("Login", "Account");
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult EsqueciSenha()
    {
        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> EnviarLinkReset(string email)
    {
        var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Email == email);
        if (usuario == null)
        {
            TempData["Mensagem"] = "E-mail não encontrado.";
            return RedirectToAction("EsqueciSenha");
        }

        var token = Guid.NewGuid().ToString();
        usuario.TokenRedefinicaoSenha = token;
        usuario.TokenExpiraEm = DateTime.Now.AddHours(1);
        _context.Update(usuario);
        await _context.SaveChangesAsync();

        var servidorIp = "http://helpdesk.brw.local"; // altere conforme necessário
        var link = $"{servidorIp}/Account/RedefinirSenha?token={token}";

        string corpo = $"Olá! Clique no link abaixo para redefinir sua senha:<br><a href='{link}'>Redefinir Senha</a>";

        await _emailService.EnviarEmailAsync(usuario.Email, "Redefinição de Senha", corpo);

        TempData["Mensagem"] = "Link de redefinição enviado para seu e-mail.";
        return RedirectToAction("EsqueciSenha");
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult RedefinirSenha(string token)
    {
        var usuario = _context.Usuarios.FirstOrDefault(u => u.TokenRedefinicaoSenha == token && u.TokenExpiraEm > DateTime.Now);
        if (usuario == null)
        {
            ViewBag.Mensagem = "Este link de redefinição de senha é inválido ou já foi utilizado.";
            return View("TokenExpirado");
        }

        return View(new RedefinirSenhaViewModel { Token = token });
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> SalvarNovaSenha(RedefinirSenhaViewModel model)
    {
        var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.TokenRedefinicaoSenha == model.Token && u.TokenExpiraEm > DateTime.Now);
        if (usuario == null)
            return View("TokenExpirado");

        usuario.SenhaHash = model.NovaSenha; // Em produção: aplicar hash
        usuario.TokenRedefinicaoSenha = null;
        usuario.TokenExpiraEm = null;

        _context.Update(usuario);
        await _context.SaveChangesAsync();

        TempData["Mensagem"] = "Senha alterada com sucesso. Faça login.";
        return RedirectToAction("Login");
    }
    [HttpGet, AllowAnonymous]
    public IActionResult Registrar()
    {
        return View(new RegistrarUsuarioViewModel());
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Registrar(RegistrarUsuarioViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var email = vm.Email.Trim().ToLowerInvariant();
        bool existe = await _context.Usuarios.AnyAsync(u => u.Email.ToLower() == email);
        if (existe)
        {
            ModelState.AddModelError(nameof(vm.Email), "Este e-mail já está cadastrado.");
            return View(vm);
        }

        var usuario = new Usuario
        {
            Nome = vm.Nome.Trim(),
            Email = email,
            Ramal = vm.Ramal.Trim(),
            SenhaHash = Senhas.GerarHash(vm.Senha), // se quiser manter puro, troque por vm.Senha
            Perfil = "Usuario",
            Ativo = false, // bloqueado até confirmar
            EmailConfirmado = false,
            TokenAtivacao = Guid.NewGuid().ToString("N"),
            TokenAtivacaoExpiraEm = DateTime.UtcNow.AddDays(2)
        };

        _context.Usuarios.Add(usuario);
        await _context.SaveChangesAsync();

        // Monte o link de ativação
        // -> Preferível usar URL absoluta via Request.Scheme/Host quando possível:
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        // Se quiser fixo (como no EsqueciSenha): var baseUrl = "http://172.16.1.205";
        var linkAtivacao = $"{baseUrl}/Account/ConfirmarEmail?userId={usuario.Id}&token={usuario.TokenAtivacao}";

        string corpo = $@"
        <p>Olá, {System.Net.WebUtility.HtmlEncode(usuario.Nome)}!</p>
        <p>Para ativar seu usuário do Portal, clique no link abaixo:</p>
        <p><a href=""{linkAtivacao}"">Ativar minha conta</a></p>
        <p>Se você não solicitou este cadastro, ignore este e-mail.</p>";

        await _emailService.EnviarEmailAsync(usuario.Email, "Ative seu acesso ao Portal", corpo);

        TempData["Mensagem"] = "Usuário criado! Enviamos um e-mail com o link de ativação.";
        return RedirectToAction("Login");
    }

    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> ConfirmarEmail(int userId, string token)
    {
        var u = await _context.Usuarios.FirstOrDefaultAsync(x => x.Id == userId);
        if (u == null)
        {
            ViewBag.Mensagem = "Usuário não encontrado.";
            return View("ConfirmarEmailResultado");
        }

        if (u.EmailConfirmado)
        {
            ViewBag.Mensagem = "E-mail já confirmado. Você já pode acessar o portal.";
            return View("ConfirmarEmailResultado");
        }

        if (string.IsNullOrWhiteSpace(token) || token != u.TokenAtivacao)
        {
            ViewBag.Mensagem = "Token inválido.";
            return View("ConfirmarEmailResultado");
        }

        if (u.TokenAtivacaoExpiraEm.HasValue && u.TokenAtivacaoExpiraEm.Value < DateTime.UtcNow)
        {
            ViewBag.Mensagem = "Token expirado. Solicite reenviar a ativação.";
            return View("ConfirmarEmailResultado");
        }

        u.EmailConfirmado = true;
        u.Ativo = true;
        u.TokenAtivacao = null;
        u.TokenAtivacaoExpiraEm = null;
        _context.Update(u);
        await _context.SaveChangesAsync();

        ViewBag.Mensagem = "Conta ativada com sucesso! Faça login para continuar.";
        return View("ConfirmarEmailResultado");
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReenviarAtivacao(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            TempData["Mensagem"] = "Informe o e-mail.";
            return RedirectToAction("Login");
        }

        var u = await _context.Usuarios.FirstOrDefaultAsync(x => x.Email.ToLower() == email.Trim().ToLower());
        if (u == null)
        {
            TempData["Mensagem"] = "Se existir cadastro, reenviamos o e-mail.";
            return RedirectToAction("Login");
        }
        if (u.EmailConfirmado)
        {
            TempData["Mensagem"] = "Este e-mail já está confirmado.";
            return RedirectToAction("Login");
        }

        u.TokenAtivacao = Guid.NewGuid().ToString("N");
        u.TokenAtivacaoExpiraEm = DateTime.UtcNow.AddDays(2);
        await _context.SaveChangesAsync();

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var linkAtivacao = $"{baseUrl}/Account/ConfirmarEmail?userId={u.Id}&token={u.TokenAtivacao}";
        string corpo = $@"<p>Olá, {System.Net.WebUtility.HtmlEncode(u.Nome)}!</p>
                      <p>Seu novo link de ativação:</p>
                      <p><a href=""{linkAtivacao}"">Ativar minha conta</a></p>";

        await _emailService.EnviarEmailAsync(u.Email, "Reenvio: Ativação do Portal", corpo);

        TempData["Mensagem"] = "Reenviamos o e-mail de ativação (se o cadastro existir).";
        TempData["ShowLogin"] = true; // força exibir o login ao recarregar
        return RedirectToAction("Login");
    }

}
