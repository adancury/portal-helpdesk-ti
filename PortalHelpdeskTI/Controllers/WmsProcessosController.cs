using Microsoft.AspNetCore.Mvc;
using PortalHelpdeskTI.Services.Integracoes;
using PortalHelpdeskTI.ViewModels.IntegracoesWms;

namespace PortalHelpdeskTI.Controllers
{
    public class WmsProcessosController : Controller
    {
        private readonly WmsProcessosQueryService _query;
        private readonly WmsProcessosSyncService _sync;

        public WmsProcessosController(WmsProcessosQueryService query, WmsProcessosSyncService sync)
        {
            _query = query;
            _sync = sync;
        }

        [HttpGet]
        public async Task<IActionResult> Index([FromQuery] WmsProcessosFiltroVm filtro, CancellationToken ct)
        {
            var vm = await _query.BuscarAsync(filtro, ct);
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return PartialView("_Grid", vm);

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> Detalhe(int id, CancellationToken ct)
        {
            var vm = await _query.DetalheAsync(id, ct);
            if (vm == null)
                return NotFound();

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Sincronizar(string tipo = "SAIDAS", DateTime? dataIni = null, DateTime? dataFim = null, CancellationToken ct = default)
        {
            var ini = DateOnly.FromDateTime((dataIni ?? DateTime.Today.AddDays(-2)).Date);
            var fim = DateOnly.FromDateTime((dataFim ?? DateTime.Today).Date);
            var exec = await _sync.SincronizarTipoAsync(tipo, ini, fim, ct);

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { ok = exec.Status == "Sucesso", exec.Status, exec.Mensagem });

            TempData[exec.Status == "Sucesso" ? "ToastMsg" : "Erro"] = exec.Mensagem;
            return RedirectToAction(nameof(Index), new { tipo });
        }
    }
}
