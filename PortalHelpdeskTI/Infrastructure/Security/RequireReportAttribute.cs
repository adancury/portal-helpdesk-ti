using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PortalHelpdeskTI.Services.Permissoes;

namespace PortalHelpdeskTI.Infrastructure.Security
{
    public class RequireReportAttribute : Attribute, IAsyncActionFilter
    {
        private readonly string _key;

        public RequireReportAttribute(string key)
        {
            _key = (key ?? "").Trim().ToUpperInvariant();
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var http = context.HttpContext;
            var usuarioId = http.Session.GetInt32("UsuarioId");

            // 1) Sem sessão => manda para login (experiência padrão do portal)
            if (!usuarioId.HasValue || usuarioId.Value <= 0)
            {
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }

            // 2) Permissão do relatório
            var perms = http.RequestServices.GetRequiredService<IRelatorioPermissaoService>();
            var ok = await perms.PodeVerAsync(usuarioId.Value, _key, http.RequestAborted);

            if (!ok)
            {
                // Para embed/AJAX, evitar redirect dentro de iframe/container
                var isAjax = string.Equals(http.Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
                var isEmbed = string.Equals(http.Request.Query["embed"], "1", StringComparison.OrdinalIgnoreCase);

                if (isAjax || isEmbed)
                {
                    context.Result = new StatusCodeResult(403);
                    return;
                }

                // Navegação normal
                context.Result = new RedirectToActionResult("AcessoNegado", "Account", null);
                return;
            }

            await next();
        }
    }
}
