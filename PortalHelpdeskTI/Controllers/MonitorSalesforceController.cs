using Microsoft.AspNetCore.Mvc;
using PortalHelpdeskTI.Models.IntegracoesSL;
using PortalHelpdeskTI.Services.Integracoes;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PortalHelpdeskTI.Controllers
{
    public class MonitorSalesforceController : Controller
    {
        private readonly NexxSalesforceMonitorService _svc;

        public MonitorSalesforceController(NexxSalesforceMonitorService svc) => _svc = svc;

        [HttpGet]
        public async Task<IActionResult> Index(
            string aba = "events",
            DateTime? de = null,
            DateTime? ate = null,
            string? statusEvent = null,
            int? statusLog = null,
            string? tipoDoc = null,
            string? q = null,
            int page = 1,
            int pageSize = 50,
            CancellationToken ct = default)
        {
            // Normalizações
            aba = string.IsNullOrWhiteSpace(aba) ? "events" : aba.Trim().ToLowerInvariant();
            if (page < 1) page = 1;
            if (pageSize < 10 || pageSize > 200) pageSize = 50;

            q = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
            tipoDoc = string.IsNullOrWhiteSpace(tipoDoc) ? null : tipoDoc.Trim();
            statusEvent = string.IsNullOrWhiteSpace(statusEvent) ? null : statusEvent.Trim();

            var vm = new MonitorSalesforceVm
            {
                Aba = aba,
                DataIni = de,
                DataFim = ate,
                StatusEvent = statusEvent,
                StatusLog = statusLog,
                TipoDoc = tipoDoc,
                Q = q,
                Page = page,
                PageSize = pageSize
            };

            try
            {
                var skip = (page - 1) * pageSize;

                if (aba == "logs")
                {
                    // KPIs
                    vm.KpiLogs = await _svc.KpisLogsAsync(de, ate, ct);

                    // Lista
                    var (itens, total, statusDisp, tipos) =
                        await _svc.ListLogsAsync(de, ate, statusLog, tipoDoc, q, skip, pageSize, ct);

                    vm.Logs = itens;
                    vm.Total = total;
                    vm.StatusLogsDisponiveis = statusDisp;
                    vm.TiposDocDisponiveis = tipos;

                    // AJAX -> só a grid
                    if (IsAjaxRequest())
                        return PartialView("_GridLogs", vm);

                    return View("Index", vm);
                }
                else
                {
                    // KPIs
                    vm.KpiEvents = await _svc.KpisEventsAsync(de, ate, ct);

                    // Lista
                    var (itens, total, statusDisp) =
                        await _svc.ListEventsAsync(de, ate, statusEvent, q, skip, pageSize, ct);

                    vm.Events = itens;
                    vm.Total = total;
                    vm.StatusEventsDisponiveis = statusDisp;

                    // AJAX -> só a grid
                    if (IsAjaxRequest())
                        return PartialView("_GridEvents", vm);

                    return View("Index", vm);
                }
            }
            catch (Exception ex)
            {
                // Em produção você pode preferir só retornar 500 (sem vazar erro).
                // Durante implantação, isso ajuda muito a identificar a falha.
                return Content("ERRO no MonitorSalesforce/Index: " + ex.Message + "\n\n" + ex.ToString());
            }
        }

        private bool IsAjaxRequest()
        {
            return string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        }
    }
}
