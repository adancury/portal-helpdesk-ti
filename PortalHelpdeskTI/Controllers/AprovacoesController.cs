using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using PortalHelpdeskTI.Models;
using PortalHelpdeskTI.Services.Aprovacoes;
using PortalHelpdeskTI.Services.SAP;
using System.Globalization;
using System.Text;
using System.Text.Json;
using static PortalHelpdeskTI.Services.SAP.ServiceLayerClient;

namespace PortalHelpdeskTI.Controllers;

[Authorize]
public class AprovacoesController : Controller
{
    private readonly ServiceLayerClient _sl;
    private readonly IApprovalService _approval;
    private readonly IMemoryCache _cache;

    public AprovacoesController(ServiceLayerClient sl, IApprovalService approval, IMemoryCache cache)
    {
        _sl = sl;
        _approval = approval;
        _cache = cache;
    }

    [HttpGet]
    public IActionResult Index()
    {
        ViewBag.SLHasSession = _sl.HasActiveSession();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoginSL(string sapUser, string sapPassword)
    {
        if (string.IsNullOrWhiteSpace(sapUser) || string.IsNullOrWhiteSpace(sapPassword))
        {
            TempData["MensagemErro"] = "Informe usuário e senha do SAP.";
            return RedirectToAction(nameof(Index));
        }
        var sessionKey = $"{HttpContext.Session.Id}:{sapUser}";
        _sl.SetSessionKey(sessionKey);

        var (ok, error) = await _sl.LoginAsync(sapUser, sapPassword);
        if (!ok)
        {
            TempData["MensagemErro"] = error ?? "Falha no login do Service Layer. Verifique credenciais.";
            return RedirectToAction(nameof(Index));
        }
        ClearPendenciasCacheForCurrentUser();

        HttpContext.Session.SetString("SAP_USERCODE", sapUser);

        var userId = await _sl.ResolveUserIdByCodeAsync(sapUser);
        if (userId.HasValue)
            HttpContext.Session.SetInt32("SAP_USERID", userId.Value);

        System.Diagnostics.Debug.WriteLine($"[LOGIN SL] Salvo UserCode={sapUser}, UserID={(userId ?? -1)}");
        TempData["MensagemSucesso"] = $"Login no Service Layer como {sapUser}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult LogoutSL()
    {
        _sl.Logout();
        ClearPendenciasCacheForCurrentUser();
        HttpContext.Session.Remove("SAP_USERCODE");
        HttpContext.Session.Remove("SAP_USERID");

        TempData["MensagemSucesso"] = "Sessão do Service Layer encerrada.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Painel(int page = 1, int pageSize = 10)
    {
        if (page < 1) page = 1;
        if (pageSize <= 0) pageSize = 10;

        // Aqui você pode aumentar esse "top" se for pouco:
        var listaCompleta = await _sl.ListarPendentesSomenteDoUsuario(top: 1000);

        var total = listaCompleta.Count;
        var itens = listaCompleta
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var vm = new AprovacoesPainelVM
        {
            Itens = itens,
            Page = page,
            PageSize = pageSize,
            Total = total
        };

        return View(vm);
    }

    public async Task<IActionResult> BaixarAnexo(int entry, int line)
    {
        // 1) Busca as linhas de anexo desse AttachmentEntry no Service Layer
        var lines = await _sl.GetAttachmentsAsync(entry);
        var ln = lines.FirstOrDefault(l => l.LineNum == line);

        if (ln == null)
            return Content($"Linha de anexo não encontrada. AttachmentEntry={entry}, LineNum={line}");

        // 2) Monta o caminho físico do arquivo
        // ln.SourcePath costuma ser a pasta; FileName + FileExtension = nome do arquivo
        var directory = ln.SourcePath ?? string.Empty;
        var fileName = $"{ln.FileName}.{ln.FileExtension}";

        // 🔧 OPCIONAL: se o SourcePath do SAP não for acessível diretamente pelo servidor do portal,
        // você pode configurar um path base no appsettings e fazer um replace aqui.
        //
        // Exemplo:
        // var sapBase = @"\\SERVIDOR\SAP_Attachments"; // lido do appsettings
        // directory = directory.Replace("C:\\B1_SHR\\Attachments", sapBase);

        var fullPath = Path.Combine(directory, fileName);

        // 🔁 Mapeia o servidor pelo IP
        // ATENÇÃO: precisa ser string literal com @ e as barras duplicadas certinho.
        if (fullPath.StartsWith(@"\\saphabrw\", StringComparison.OrdinalIgnoreCase))
        {
            fullPath = fullPath.Replace(@"\\saphabrw\", @"\\10.123.46.7\");
        }

        if (!System.IO.File.Exists(fullPath))
        {
            return Content($"Arquivo físico não encontrado em '{fullPath}'. " +
                           "Verifique se o servidor do portal tem acesso à pasta de anexos do SAP.");
        }

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        // Se quiser, podemos melhorar o contentType depois (por extensão)
        var contentType = "application/octet-stream";

        return File(stream, contentType, fileName);
    }

    [HttpGet]
    public async Task<IActionResult> Grid(int page = 1, int pageSize = 10, string? filtro = null)
    {
        if (!_sl.HasActiveSession())
            return Content(
                "<div data-requires-sl-login=\"1\" class=\"p-3 text-muted\">Entre no Service Layer para listar pendências.</div>",
                "text/html"
            );

        var myUserId = HttpContext.Session.GetInt32("SAP_USERID");
        if (!myUserId.HasValue)
            return Content("<div class=\"p-3 text-muted\">Refaça o login no Service Layer.</div>", "text/html");

        if (page < 1) page = 1;
        if (pageSize <= 0) pageSize = 10;

        List<ServiceLayerClient.ApprovalRequestDto> pendencias;
        try
        {
            pendencias = await _sl.ListarPendentesSomenteDoUsuario(top: 1000);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Service Layer", StringComparison.OrdinalIgnoreCase))
        {
            ClearPendenciasCacheForCurrentUser();
            HttpContext.Session.Remove("SAP_USERCODE");
            HttpContext.Session.Remove("SAP_USERID");

            return Content(
                $"<div data-requires-sl-login=\"1\" class=\"p-3 text-muted\">{System.Net.WebUtility.HtmlEncode(ex.Message)}</div>",
                "text/html"
            );
        }

        pendencias = pendencias
            .Where(p => string.Equals(p.Status, "arsPending", StringComparison.OrdinalIgnoreCase))
            .ToList();

        IEnumerable<ServiceLayerClient.ApprovalRequestDto> query = pendencias;

        if (!string.IsNullOrWhiteSpace(filtro))
        {
            var termo = filtro.Trim();
            var termoUpper = termo.ToUpperInvariant();

            query = query.Where(p =>
            {
                // Nº do documento
                var docNumText = p.DraftDocNum.HasValue && p.DraftDocNum.Value > 0
                    ? p.DraftDocNum.Value.ToString()
                    : string.Empty;
                var docUpper = docNumText.ToUpperInvariant();

                // Criador
                var creator = (p.OriginatorNameInitCap ?? string.Empty).ToUpperInvariant();

                // Label do tipo de doc (usa SUA GetTipoLabel já existente)
                var tipoLabel = GetTipoLabel(p).ToUpperInvariant();

                // Código da aprovação (opcional no filtro)
                var codeText = p.Code.ToString().ToUpperInvariant();

                return (!string.IsNullOrEmpty(docUpper) && docUpper.Contains(termoUpper))
                    || (!string.IsNullOrEmpty(creator) && creator.Contains(termoUpper))
                    || (!string.IsNullOrEmpty(tipoLabel) && tipoLabel.Contains(termoUpper))
                    || (!string.IsNullOrEmpty(codeText) && codeText.Contains(termoUpper));
            });
        }

        var listaFiltrada = query.ToList();
        var total = listaFiltrada.Count;

        var itens = listaFiltrada
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var vm = new AprovacoesPainelVM
        {
            Itens = itens,
            Page = page,
            PageSize = pageSize,
            Total = total
        };

        return PartialView("_GridAprovacoes", vm);
    }
    
    // Mesma lógica de label que você já usa na view (_GridAprovacoes)
    private static string GetTipoLabel(ServiceLayerClient.ApprovalRequestDto p)
    {
        if (string.Equals(p.IsDraft, "Y", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(p.DraftType, "PRQ", StringComparison.OrdinalIgnoreCase))
                return "Solicitação";
            if (string.Equals(p.DraftType, "PO", StringComparison.OrdinalIgnoreCase))
                return "Pedido";
        }

        return p.ObjectType switch
        {
            "1470000113" => "Solicitação",
            "22" => "Pedido",
            _ => p.ObjectType ?? "—"
        };
    }

    [HttpGet]
    public async Task<IActionResult> DebugApproval(int code)
    {
        var txt = await _sl.DebugApprovalActionAsync(code);
        return Content(txt, "text/plain; charset=utf-8");
    }

    [HttpGet]
    public async Task<IActionResult> WhoMatches()
    {
        if (!_sl.HasActiveSession()) return Content("Sem sessão SL.");

        var uid = HttpContext.Session.GetInt32("SAP_USERID") ?? -1;

        var all = await _sl.ListarPendenciasGerais(50);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"UID sessão: {uid}");
        foreach (var r in all)
        {
            var lines = (r.ApprovalRequestLines ?? new())
                        .Where(l => l.Status == "ardPending")
                        .Select(l => l.UserID?.ToString() ?? "?")
                        .ToList();

            var hasMine = (r.ApprovalRequestLines ?? new())
                          .Any(l => l.Status == "ardPending" && l.UserID == uid);

            sb.AppendLine($"Req {r.Code} | pendentes p/ UserIDs: {string.Join(",", lines)} | meu? {hasMine}");
        }
        return Content(sb.ToString(), "text/plain; charset=utf-8");
    }

    [HttpGet]
    public async Task<IActionResult> TestaMetodoGrid()
    {
        var uid = HttpContext.Session.GetInt32("SAP_USERID") ?? -1;
        //var list = await _sl.ListarPendenciasUsuarioHLGAsync(uid);
        var list = await _sl.ListarPendentesSomenteDoUsuario();
        var ids = string.Join(",", list.Select(x => x.Code));
        return Content($"UID={uid} | itens={list.Count} | codes=[{ids}]", "text/plain; charset=utf-8");
    }

    [HttpGet]
    public async Task<IActionResult> Diagnostico()
    {
        if (!_sl.HasActiveSession()) return Content("Sem sessão SL.");

        var sapUser = HttpContext.Session.GetString("SAP_USERCODE") ?? "(null)";
        var uidSess = HttpContext.Session.GetInt32("SAP_USERID");
        var uidApi = await _sl.TryResolveUserIdAsync(sapUser);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"SAP_USERCODE: {sapUser}");
        sb.AppendLine($"SAP_USERID (sessão): {uidSess?.ToString() ?? "(null)"}");
        sb.AppendLine($"SAP_USERID (resolvido API): {uidApi?.ToString() ?? "(null)"}");
        sb.AppendLine();

        var all = await _sl.ListarPendenciasGerais(20);
        foreach (var r in all)
        {
            var users = (r.ApprovalRequestLines ?? new())
                        .Where(l => l.Status == "ardPending")
                        .Select(l => l.UserID?.ToString() ?? "?");
            sb.AppendLine($"Req {r.Code} - pendentes p/ UserIDs: {string.Join(",", users)} (CurrentStage: {r.CurrentStage})");
        }

        return Content(sb.ToString(), "text/plain; charset=utf-8");
    }

    // ======= DETALHES (rota ÚNICA para o modal) =======
    [HttpGet("/Aprovacoes/Detalhes/{code:int}")]
    public async Task<IActionResult> Detalhes(int code, int? draftDocNum)
    {
        var userId = HttpContext.Session.GetInt32("SAP_USERID");
        var detalhe = await _sl.GetApprovalRequestDetailsAsync(code, userId);

        // passa o draft pra view
        ViewBag.DraftDocNum = draftDocNum;

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            return PartialView(
                "_DetalhesAprovacao",
                detalhe ?? new ServiceLayerClient.ApprovalRequestDetailsDto
                {
                    ApprovalRequestID = code,
                    Status = "(indisponível)"
                }
            );
        }

        return View("DetalhesAprovacao", detalhe);
    }

    [HttpGet]
    public IActionResult LoginPartial()
    {
        return PartialView("/Views/Aprovacoes/_LoginSL.cshtml");
    }

    private async Task<(int? Key, string? Code)> GetUserByIdAsync(int internalKey)
    {
        using var client = _sl.CreateClientWithCookies();
        var resp = await client.GetAsync($"Users({internalKey})?$select=InternalKey,UserCode");
        var raw = await resp.Content.ReadAsStringAsync();
        System.Diagnostics.Debug.WriteLine($"[SL][Users({internalKey})] {resp.StatusCode} {raw}");
        if (!resp.IsSuccessStatusCode) return (null, null);
        using var doc = JsonDocument.Parse(raw);
        var key = doc.RootElement.GetProperty("InternalKey").GetInt32();
        var code = doc.RootElement.GetProperty("UserCode").GetString();
        return (key, code);
    }

    [HttpPost("Aprovar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Aprovar([FromForm] int id, [FromForm] string? obs)
    {
        var (ok, err, updated) = await _sl.PatchApprovalDecisionAsync(id, "ardApproved", obs);

        if (!ok)
        {
            if (WantsJson(Request))
                return BadRequest(new { message = err ?? "Falha ao aprovar no Service Layer." });

            TempData["ToastTipo"] = "error";
            TempData["ToastMsg"] = err ?? "Falha ao aprovar no Service Layer.";
            return RedirectToAction("Index", "Aprovacoes");
        }

        // >>> AQUI: invalida o cache de pendências do usuário atual <<<
        ClearPendenciasCacheForCurrentUser();

        if (WantsJson(Request))
        {
            return Ok(new
            {
                message = "Aprovado com sucesso.",
                approvalRequestId = id,
                status = updated?.Status,
                currentStage = updated?.CurrentStage
            });
        }

        TempData["ToastTipo"] = "success";
        TempData["ToastMsg"] = "Solicitação aprovada com sucesso.";
        return RedirectToAction("Index", "Aprovacoes");
    }


    [HttpPost("Rejeitar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rejeitar([FromForm] int id, [FromForm] string? obs)
    {
        var (ok, err, updated) = await _sl.PatchApprovalDecisionAsync(id, "ardNotApproved", obs);

        if (!ok)
        {
            if (WantsJson(Request))
                return BadRequest(new { message = err ?? "Falha ao rejeitar no Service Layer." });

            TempData["ToastTipo"] = "error";
            TempData["ToastMsg"] = err ?? "Falha ao rejeitar no Service Layer.";
            return RedirectToAction("Index", "Aprovacoes");
        }

        // >>> AQUI: invalida o cache de pendências do usuário atual <<<
        ClearPendenciasCacheForCurrentUser();

        if (WantsJson(Request))
        {
            return Ok(new
            {
                message = "Rejeitado com sucesso.",
                approvalRequestId = id,
                status = updated?.Status,
                currentStage = updated?.CurrentStage
            });
        }

        TempData["ToastTipo"] = "success";
        TempData["ToastMsg"] = "Solicitação rejeitada com sucesso.";
        return RedirectToAction("Index", "Aprovacoes");
    }


    // Helper para detectar se é chamada AJAX / JSON
    private static bool WantsJson(HttpRequest request)
    {
        var accept = request.Headers["Accept"].ToString();
        var xrw = request.Headers["X-Requested-With"].ToString();

        return (accept?.Contains("application/json") == true) ||
               (xrw == "XMLHttpRequest");
    }
    private void ClearPendenciasCacheForCurrentUser()
    {
        var myUserId = HttpContext.Session.GetInt32("SAP_USERID");
        if (!myUserId.HasValue) return;

        var cacheKey = $"aprovacoes_pendencias_{myUserId.Value}";
        _cache.Remove(cacheKey);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reprovar([FromForm] int id, [FromForm] string? obs)
    {
        var (ok, err) = await _sl.DecideUsingCurrentStageAsync(id, approve: false, remarks: obs);
        if (!ok) return BadRequest(new { message = err ?? "Falha no update de reprovação." });
        return Ok(new { message = "Decisão enviada com sucesso." });
    }

    [HttpGet]
    public async Task<IActionResult> DebugSmoke(int id)
    {
        var ok = await _sl.DebugSmokeAsync(id);
        return Content(ok ? "Smoke OK" : "Smoke FAIL");
    }

    [HttpGet]
    public async Task<IActionResult> DebugHLG(int? id = null)
    {
        var sapUser = HttpContext.Session.GetString("SAP_USERCODE");
        var txt = await _sl.DebugHLGAsync(sapUser, id);
        return Content(txt, "text/plain; charset=utf-8");
    }

    // ======= POST de decisão no modal =======
    [HttpPost("/approvals/{code:int}/decide")]
    public async Task<IActionResult> Decide(int code, int stageCode, string decision, string status, string? remarks)
    {
        var userId = HttpContext.Session.GetInt32("SAP_USERID") ?? 0;
        if (userId == 0) return BadRequest("Usuário SAP não encontrado.");

        bool ok = decision == "approve"
            ? await _sl.ApproveAsync(code, stageCode, userId, status, remarks)
            : await _sl.RejectAsync(code, stageCode, userId, remarks);

        if (!ok) return BadRequest("Falha ao enviar decisão ao Service Layer.");
        return Ok();
    }
}
