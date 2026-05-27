using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PortalHelpdeskTI.Services.Permissoes;

namespace PortalHelpdeskTI.Filters
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
    public class RelatorioAuthorizeAttribute : Attribute, IAsyncActionFilter
    {
        private readonly string _key;

        public RelatorioAuthorizeAttribute(string relatorioKey)
        {
            _key = (relatorioKey ?? "").Trim().ToUpperInvariant();
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var http = context.HttpContext;

            var usuarioId = http.Session.GetInt32("UsuarioId");
            if (!usuarioId.HasValue)
            {
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }

            var svc = http.RequestServices.GetRequiredService<IRelatorioPermissaoService>();
            var podeVer = await svc.PodeVerAsync(usuarioId.Value, _key, http.RequestAborted);

            if (!podeVer)
            {
                // Quando estiver embutido (embed) ou via AJAX, é melhor retornar 403
                // para evitar redirecionamento dentro de iframe/container.
                var isAjax = http.Request.Headers["X-Requested-With"] == "XMLHttpRequest";
                var embed = string.Equals(http.Request.Query["embed"], "1");

                if (isAjax || embed)
                {
                    context.Result = new StatusCodeResult(403);
                    return;
                }

                context.Result = new RedirectToActionResult("AcessoNegado", "Account", null);
                return;
            }

            await next();
        }
    }
}
