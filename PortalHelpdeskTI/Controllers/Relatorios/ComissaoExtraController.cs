using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PortalHelpdeskTI.Services.Relatorios;

namespace PortalHelpdeskTI.Controllers.Relatorios
{
    [Authorize]
    [Route("Relatorios/ComissaoExtra")]
    public class ComissaoExtraController : Controller
    {
        private readonly ComissaoExtraService _service;

        public ComissaoExtraController(ComissaoExtraService service)
        {
            _service = service;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index(int? anoFiscal, int? ano, int? trimestre, string? tipoVendedor, CancellationToken ct)
        {
            var now = DateTime.Now;
            var aFiscal = anoFiscal ?? ano ?? now.Year;              // ✅ AnoFiscal
            var t = trimestre ?? 1;

            var vm = await _service.ObterAsync(aFiscal, t, tipoVendedor, ct);
            return View("~/Views/Relatorios/ComissaoExtra/Index.cshtml", vm);
        }

        [HttpGet("Grid")]
        public async Task<IActionResult> Grid(int anoFiscal, int trimestre, string? tipoVendedor, CancellationToken ct)
        {
            var vm = await _service.ObterAsync(anoFiscal, trimestre, tipoVendedor, ct);
            return PartialView("~/Views/Relatorios/ComissaoExtra/_Grid.cshtml", vm);
        }

        [HttpPost("SalvarMeta")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SalvarMeta([FromForm] int vendedorId, [FromForm] int anoFiscal, [FromForm] int trimestre, [FromForm] decimal meta, CancellationToken ct)
        {
            await _service.SalvarMetaAsync(vendedorId, anoFiscal, trimestre, meta, ct);
            return Ok(new { ok = true });
        }

        [HttpPost("AtualizarRealizadoSap")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AtualizarRealizadoSap([FromForm] int anoFiscal, [FromForm] int trimestre, CancellationToken ct)
        {
            var atualizados = await _service.AtualizarRealizadoSapAsync(anoFiscal, trimestre, ct);
            return Ok(new { ok = true, atualizados });
        }
    }
}
