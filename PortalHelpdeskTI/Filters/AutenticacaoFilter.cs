using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
using System;

namespace PortalHelpdeskTI.Filters
{
    public class AutenticacaoFilter : IAuthorizationFilter
    {
        /*public void OnAuthorization(AuthorizationFilterContext context)
        
        {
            // Se a ação ou o controller estiver marcado com [AllowAnonymous], não faz nada
            var endpoint = context.HttpContext.GetEndpoint();
            if (endpoint?.Metadata?.GetMetadata<AllowAnonymousAttribute>() != null)
                return;

            var usuarioId = context.HttpContext.Session.GetInt32("UsuarioId");
            if (!usuarioId.HasValue)
            {
                context.Result = new RedirectToActionResult("Login", "Account", null);
            }
        }*/
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var httpContext = context.HttpContext;
            var path = httpContext.Request.Path;

            // 1) LIBERA tudo que for API (seu endpoint de estoque, etc.)
            if (path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // 2) LIBERA páginas públicas:
            //    - Raiz "/" (que aponta para Account/Login pelo route default)
            //    - Qualquer rota de Account (Login, Registrar, EsqueciSenha, etc.)
            //    - Simulador de prazos
            if (path == "/" ||
                path.StartsWithSegments("/Account", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWithSegments("/Simulador", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // 3) Se a action/controller tiver [AllowAnonymous], não força login
            //    (útil se você usar AllowAnonymous em outros lugares)
            var hasAllowAnonymous =
                context.Filters.OfType<IAllowAnonymousFilter>().Any() ||
                context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any();

            if (hasAllowAnonymous)
                return;

            // 4) LÓGICA NORMAL: se não tem usuário em sessão, redireciona para login
            var usuarioId = httpContext.Session.GetInt32("UsuarioId");
            if (!usuarioId.HasValue)
            {
                context.Result = new RedirectToActionResult("Login", "Account", null);
            }
        }
    }
}
