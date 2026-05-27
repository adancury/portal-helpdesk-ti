using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using PortalHelpdeskTI.Models.Relatorios;
using PortalHelpdeskTI.Services.Relatorios;
using ClosedXML.Excel;
using System;
using System.IO;
using System.Threading.Tasks;

[Route("Relatorios/MudancaCarteira")]
public class MudancaCarteiraController : Controller
{
    private readonly MudancaCarteiraServiceLayerService _slService;
    private readonly IMemoryCache _cache;

    public MudancaCarteiraController(MudancaCarteiraServiceLayerService slService, IMemoryCache cache)
    {
        _slService = slService;
        _cache = cache;
    }

    private const string SessSapUser = "SAP_SL_User";
    private const string SessSapPass = "SAP_SL_Pass";

    private const string ViewLogin = "~/Views/Relatorios/MudancaCarteira/ServiceLayerLogin.cshtml";
    private const string ViewUpload = "~/Views/Relatorios/MudancaCarteira/ServiceLayer.cshtml";
    private const string ViewResultado = "~/Views/Relatorios/MudancaCarteira/Resultado.cshtml";

    private static string CacheKeyProgress(string jobId) => $"MudancaCarteira:Progress:{jobId}";
    private static string CacheKeyResult(string jobId) => $"MudancaCarteira:Result:{jobId}";

    [HttpGet("ServiceLayer")]
    public IActionResult ServiceLayer()
    {
        var u = HttpContext.Session.GetString(SessSapUser);
        var p = HttpContext.Session.GetString(SessSapPass);

        if (string.IsNullOrWhiteSpace(u) || string.IsNullOrWhiteSpace(p))
            return RedirectToAction(nameof(ServiceLayerLogin));

        return View(ViewUpload, new MudancaCarteiraServiceLayerUploadVM());
    }

    [HttpGet("ServiceLayerLogin")]
    public IActionResult ServiceLayerLogin()
    {
        return View(ViewLogin, new SapLoginVM());
    }

    [HttpPost("ServiceLayerLogin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ServiceLayerLogin(SapLoginVM vm)
    {
        if (!ModelState.IsValid)
            return View(ViewLogin, vm);

        try
        {
            var ok = await _slService.TestarLoginAsync(vm.SapUser, vm.SapPassword);

            if (!ok)
            {
                ModelState.AddModelError("", "Credenciais SAP inválidas ou sem permissão no Service Layer.");
                return View(ViewLogin, vm);
            }

            HttpContext.Session.SetString(SessSapUser, vm.SapUser);
            HttpContext.Session.SetString(SessSapPass, vm.SapPassword);

            return RedirectToAction(nameof(ServiceLayer));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", "Não foi possível conectar ao Service Layer: " + ex.Message);
            return View(ViewLogin, vm);
        }
    }

    // NOVO: inicia o processamento e devolve jobId (para progressbar via polling)
    [HttpPost("ServiceLayerStart")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ServiceLayerStart(MudancaCarteiraServiceLayerServiceVM vm)
    {
        var u = HttpContext.Session.GetString(SessSapUser);
        var p = HttpContext.Session.GetString(SessSapPass);

        if (string.IsNullOrWhiteSpace(u) || string.IsNullOrWhiteSpace(p))
            return Unauthorized(new { ok = false, message = "Sessão do Service Layer expirada. Faça login novamente." });

        if (vm.Arquivo == null || vm.Arquivo.Length == 0)
            return BadRequest(new { ok = false, message = "Selecione um arquivo Excel válido." });

        // Persistir upload em temp (evita stream morrer antes do background terminar)
        var jobId = Guid.NewGuid().ToString("N");
        var tempPath = Path.Combine(Path.GetTempPath(), $"MudancaCarteira_{jobId}.xlsx");

        await using (var fs = System.IO.File.Create(tempPath))
        {
            await vm.Arquivo.CopyToAsync(fs);
        }

        // inicializa progresso no cache
        _cache.Set(CacheKeyProgress(jobId), new MudancaCarteiraProgresso
        {
            Total = 0,
            Processados = 0,
            Atualizados = 0,
            Status = "Job iniciado...",
            Concluido = false
        }, TimeSpan.FromHours(2));

        // dispara processamento em background
        _ = Task.Run(async () =>
        {
            try
            {
                await using var stream = System.IO.File.OpenRead(tempPath);

                MudancaCarteiraResultado result = await _slService.ProcessarAsync(
                    stream,
                    vm.AtualizarPrincipal,
                    u,
                    p,
                    onProgress: prog =>
                    {
                        _cache.Set(CacheKeyProgress(jobId), prog, TimeSpan.FromHours(2));
                    });

                _cache.Set(CacheKeyResult(jobId), result, TimeSpan.FromHours(2));

                // marca concluído (caso não tenha vindo do callback por algum motivo)
                var finalProg = _cache.Get<MudancaCarteiraProgresso>(CacheKeyProgress(jobId)) ?? new MudancaCarteiraProgresso();
                finalProg.Concluido = true;
                if (string.IsNullOrWhiteSpace(finalProg.Status)) finalProg.Status = "Concluído.";
                _cache.Set(CacheKeyProgress(jobId), finalProg, TimeSpan.FromHours(2));
            }
            catch (Exception ex)
            {
                _cache.Set(CacheKeyProgress(jobId), new MudancaCarteiraProgresso
                {
                    Total = 0,
                    Processados = 0,
                    Atualizados = 0,
                    Status = $"Erro no processamento: {ex.Message}",
                    Concluido = true
                }, TimeSpan.FromHours(2));
            }
            finally
            {
                try { System.IO.File.Delete(tempPath); } catch { /* ignore */ }
            }
        });

        return Json(new { ok = true, jobId });
    }

    // NOVO: endpoint para o polling do progressbar
    [HttpGet("Progress")]
    public IActionResult Progress(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return BadRequest(new { ok = false, message = "jobId não informado." });

        var prog = _cache.Get<MudancaCarteiraProgresso>(CacheKeyProgress(jobId));
        if (prog == null)
            return NotFound(new { ok = false, message = "Job não encontrado ou expirado." });

        return Json(new { ok = true, progress = prog });
    }

    // NOVO: abre a tela de resultado a partir do jobId
    [HttpGet("ResultadoJob")]
    public IActionResult ResultadoJob(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return RedirectToAction(nameof(ServiceLayer));

        var result = _cache.Get<MudancaCarteiraResultado>(CacheKeyResult(jobId));
        if (result == null)
        {
            TempData["Erro"] = "Resultado não encontrado (job expirado ou ainda em execução).";
            return RedirectToAction(nameof(ServiceLayer));
        }

        return View(ViewResultado, result);
    }

    [HttpPost("ServiceLayerLogout")]
    [ValidateAntiForgeryToken]
    public IActionResult ServiceLayerLogout()
    {
        HttpContext.Session.Remove(SessSapUser);
        HttpContext.Session.Remove(SessSapPass);
        return RedirectToAction(nameof(ServiceLayerLogin));
    }

    // GET: /Relatorios/MudancaCarteira/Template
    [HttpGet("Template")]
    public IActionResult Template()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Modelo");

        ws.Cell(1, 1).Value = "CardCode";
        ws.Cell(1, 2).Value = "SlpCode";

        ws.Range(1, 1, 1, 2).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();

        ws.Cell(2, 1).Value = "C000001";
        ws.Cell(2, 2).Value = 389;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var fileName = $"Modelo_MudancaCarteira_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }
}
