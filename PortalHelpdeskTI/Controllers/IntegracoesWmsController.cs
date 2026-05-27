using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using PortalHelpdeskTI.Services.Integracoes;
using PortalHelpdeskTI.ViewModels.IntegracoesWms;
using System.Data;

namespace PortalHelpdeskTI.Controllers
{
    public sealed class IntegracoesWmsController : Controller
    {
        private readonly IntegracoesWmsService _svc;

        public IntegracoesWmsController(IntegracoesWmsService svc)
        {
            _svc = svc;
        }

        [HttpGet]
        public IActionResult Index([FromQuery] IntegracoesWmsFiltroVm f)
        {
            if (string.IsNullOrWhiteSpace(f.Aba)) f.Aba = "envios";
            if (f.Page < 1) f.Page = 1;
            if (f.PageSize <= 0) f.PageSize = 50;

            ViewBag.MetodosEnvio = IntegracoesWmsService.MetodosEnvio;
            return View(f);
        }

        [HttpGet]
        public async Task<IActionResult> Grid([FromQuery] IntegracoesWmsFiltroVm f, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(f.Aba)) f.Aba = "envios";
            if (f.Page < 1) f.Page = 1;
            if (f.PageSize <= 0) f.PageSize = 50;

            (System.Data.DataTable rows, int total) result =
                f.Aba == "retornos"
                ? await _svc.BuscarRetornosAsync(f, ct)
                : await _svc.BuscarEnviosAsync(f, ct);

            ViewBag.Total = result.total;
            ViewBag.Page = f.Page;
            ViewBag.PageSize = f.PageSize;
            ViewBag.Aba = f.Aba;

            return PartialView("_GridIntegracoesWms", result.rows);
        }

        [HttpGet]
        public Task<IActionResult> ExportarExcel(
            DateTime dataIni,
            DateTime dataFim,
            string? metodo,
            string? status,
            string? texto,
            int page = 1,
            int pageSize = 50,
            string aba = "envios",
            CancellationToken ct = default)
        {
            return Exportar(dataIni, dataFim, metodo, status, texto, page, pageSize, aba, ct);
        }

        [HttpGet]
        public async Task<IActionResult> Exportar(
            DateTime dataIni,
            DateTime dataFim,
            string? metodo,
            string? status,
            string? texto,
            int page = 1,
            int pageSize = 50,
            string aba = "envios",
            CancellationToken ct = default)
        {
            var f = new IntegracoesWmsFiltroVm
            {
                DataIni = dataIni,
                DataFim = dataFim,
                Metodo = metodo,
                Status = status,
                Texto = texto,
                Page = page,
                PageSize = pageSize
            };

            var dt = await _svc.BuscarLogsAsync(f, aba, ct);

            using var package = new OfficeOpenXml.ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Logs");

            // Cabeçalho
            for (int c = 0; c < dt.Columns.Count; c++)
            {
                ws.Cells[1, c + 1].Value = dt.Columns[c].ColumnName;
                ws.Cells[1, c + 1].Style.Font.Bold = true;
            }

            // Linhas
            for (int r = 0; r < dt.Rows.Count; r++)
            {
                for (int c = 0; c < dt.Columns.Count; c++)
                    ws.Cells[r + 2, c + 1].Value = dt.Rows[r][c]?.ToString();
            }

            ws.Cells.AutoFitColumns();

            var bytes = package.GetAsByteArray();
            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Logs_WMS_{aba}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }

        [HttpGet]
        public async Task<IActionResult> ExportarTudo(
            DateTime dataIni,
            DateTime dataFim,
            string? metodo,
            string? status,
            string? texto,
            string aba = "envios",
            CancellationToken ct = default)
        {
            var f = new IntegracoesWmsFiltroVm
            {
                DataIni = dataIni,
                DataFim = dataFim,
                Metodo = metodo,
                Status = status,
                Texto = texto,

                // Page/PageSize não importam aqui
                Page = 1,
                PageSize = 50
            };

            // Ajuste aqui conforme sua realidade
            const int batchSize = 5000;
            const int maxRows = 200000; // 0 = sem limite (não recomendo)

            var dt = await _svc.BuscarLogsParaExportacaoAsync(f, aba, batchSize, maxRows, ct);

            using var package = new OfficeOpenXml.ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Logs");

            // Cabeçalho
            for (int c = 0; c < dt.Columns.Count; c++)
            {
                ws.Cells[1, c + 1].Value = dt.Columns[c].ColumnName;
                ws.Cells[1, c + 1].Style.Font.Bold = true;
            }

            // Dados
            for (int r = 0; r < dt.Rows.Count; r++)
            {
                for (int c = 0; c < dt.Columns.Count; c++)
                    ws.Cells[r + 2, c + 1].Value = dt.Rows[r][c]?.ToString();
            }

            ws.Cells.AutoFitColumns();

            var bytes = package.GetAsByteArray();

            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Logs_WMS_{aba}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }

        [HttpGet]
        public async Task<IActionResult> Resumo(
        [FromQuery] IntegracoesWmsFiltroVm f,
        CancellationToken ct)
        {
            var resumo = await _svc.BuscarResumoAsync(f, f.Aba, ct);
            return Json(resumo);
        }
    }
}
