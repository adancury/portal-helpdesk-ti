using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PortalHelpdeskTI.Models;
using System;
using System.Threading.Tasks;

namespace PortalHelpdeskTI.Controllers
{
    [Authorize]
    public class InicioController : Controller
    {
        private readonly AppDbContext _context;

        public InicioController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var perfil = HttpContext.Session.GetString("Perfil"); // "Tecnico" | "Usuario"
            var usuarioId = HttpContext.Session.GetInt32("UsuarioId");

            // Banner de avaliação só faz sentido para usuário final
            if (!string.Equals(perfil, "Tecnico", StringComparison.OrdinalIgnoreCase) && usuarioId.HasValue)
            {
                var uid = usuarioId.Value;

                var qtdPendentes = await _context.Chamados
                    .AsNoTracking()
                    .Where(c => c.UsuarioId == uid && c.Status == "Concluído")
                    .Where(c => !_context.AvaliacoesChamado.Any(a => a.ChamadoId == c.Id && a.UsuarioId == uid))
                    .CountAsync();

                ViewBag.QtdPendentesAvaliacao = qtdPendentes;
            }
            else
            {
                ViewBag.QtdPendentesAvaliacao = 0;
            }

            return View();
        }
    }
}
