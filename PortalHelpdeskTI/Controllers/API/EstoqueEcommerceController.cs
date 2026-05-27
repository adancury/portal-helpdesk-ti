using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using PortalHelpdeskTI.Services.Integracoes;

namespace PortalHelpdeskTI.Controllers.Api
{
    [ApiController]
    [Route("api/estoque-ecommerce")]
    [AllowAnonymous] // <- importantíssimo para furar o filtro e o esquema de cookies
    public class EstoqueEcommerceController : ControllerBase
    {
        private readonly EstoqueEcommerceService _service;

        public EstoqueEcommerceController(EstoqueEcommerceService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string? whsCode)
        {
            var deposito = string.IsNullOrWhiteSpace(whsCode) ? "21" : whsCode;

            var itens = await _service.ListarItensDivergentesAsync(deposito);

            return Ok(itens);
        }

        [HttpGet("dois-depositos")]
        public async Task<IActionResult> GetDoisDepositos()
        {
            var itens = await _service.ListarItensDivergentesDoisDepositosAsync("31");
            return Ok(itens);
        }

    }
}
