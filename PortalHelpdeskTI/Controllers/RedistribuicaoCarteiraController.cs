using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PortalHelpdeskTI.Infrastructure.Security;
using PortalHelpdeskTI.Services;
using PortalHelpdeskTI.Services.SAP;
using PortalHelpdeskTI.ViewModels.Redistribuicao;
using System.Data.Odbc;
using System.Text.Json;

namespace PortalHelpdeskTI.Controllers;

[RequireReport("REDISTRIBUICAO_CARTEIRA")]
public class RedistribuicaoCarteiraController : Controller
{
    private readonly RedistribuicaoCarteiraService _svc;
    private readonly ServiceLayerClient _sl;

    // Chaves (mantém padrão e evita typo)
    private const string KEY_VER = "REDISTRIBUICAO_CARTEIRA";
    private const string KEY_APLICAR = "REDISTRIBUICAO_CARTEIRA_APLICAR";

    public RedistribuicaoCarteiraController(RedistribuicaoCarteiraService svc, ServiceLayerClient sl)
    {
        _svc = svc;
        _sl = sl;
    }

    // ===============================
    // Helpers de sessão / UX
    // ===============================
    private int? UsuarioId => HttpContext.Session.GetInt32("UsuarioId");

    private IActionResult? GuardarSessao()
    {
        if (!UsuarioId.HasValue || UsuarioId.Value <= 0)
            return RedirectToAction("Login", "Account");
        return null;
    }

    private void SetPodeAplicarNaView()
    {
        // A checagem “real” é feita pelo [RequireReport] na action AplicarRedistribuicao.
        // Aqui é só UX (mostrar/esconder botão).
        // Se você tiver um helper/claim para verificar permissões na view, pode trocar.
        ViewBag.PodeAplicar = true; // default otimista (ajuste na view via endpoint)
    }

    // ===============================
    // Normalização / Validação de filtros
    // - Mantém compatibilidade com UI antiga (MesesInativo)
    // - Regra nova: "inativo" = sem compra há X dias
    // ===============================
    private static RedistribuicaoCarteiraFiltroVm Normalizar(RedistribuicaoCarteiraFiltroVm? f)
    {
        f ??= new RedistribuicaoCarteiraFiltroVm();

        // Defaults defensivos + limites
        f.MesesLead = Math.Clamp(f.MesesLead <= 0 ? 3 : f.MesesLead, 1, 24);
        f.JanelaTkmMeses = Math.Clamp(f.JanelaTkmMeses <= 0 ? 12 : f.JanelaTkmMeses, 1, 24);

        // Se o POST não trouxe os campos (ex.: botão dentro do modal sem inputs),
        // garantimos defaults coerentes (mantendo o comportamento da tela).
        if (f.MesesInativo <= 0) f.MesesInativo = 12;
        if (f.DiasInativo <= 0 && f.MesesInativo <= 0) f.DiasInativo = 90; // fallback extra

        // Compatibilidade: MesesInativo era "meses" no passado, mas você pediu para manter e apenas interpretar.
        // Estratégia:
        // 1) Se DiasInativo vier preenchido, ele manda.
        // 2) Se DiasInativo não vier, e MesesInativo >= 30, interpretamos MesesInativo como dias.
        // 3) Caso contrário, interpretamos MesesInativo como meses e convertemos para dias (x30).
        int dias;
        if (f.DiasInativo > 0)
            dias = f.DiasInativo;
        else if (f.MesesInativo >= 30)
            dias = f.MesesInativo;          // interpretado como dias
        else
            dias = f.MesesInativo * 30;     // interpretado como meses

        // Limites para evitar consultas ruins e regra comercial (mínimo 30 dias)
        dias = Math.Clamp(dias, 30, 3650);

        f.DiasInativo = dias;

        return f;
    }

    private const string SessionKeyFiltros = "RedistribuicaoCarteira:Filtros";

    private void SalvarFiltrosNaSessao(RedistribuicaoCarteiraFiltroVm filtros)
    {
        try
        {
            HttpContext?.Session?.SetString(SessionKeyFiltros, JsonSerializer.Serialize(filtros));
        }
        catch { /* sessão pode não estar habilitada */ }
    }

    private RedistribuicaoCarteiraFiltroVm? CarregarFiltrosDaSessao()
    {
        try
        {
            var json = HttpContext?.Session?.GetString(SessionKeyFiltros);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<RedistribuicaoCarteiraFiltroVm>(json);
        }
        catch { return null; }
    }

    private RedistribuicaoCarteiraFiltroVm GarantirFiltros(RedistribuicaoCarteiraFiltroVm? filtros)
    {
        var f = filtros;
        if (f == null || (f.MesesLead == 0 && f.MesesInativo == 0 && f.JanelaTkmMeses == 0 && f.DiasInativo == 0 && !f.IncluirSomenteAtivos))
            f = CarregarFiltrosDaSessao() ?? filtros ?? new RedistribuicaoCarteiraFiltroVm();

        f = Normalizar(f);
        SalvarFiltrosNaSessao(f);
        return f;
    }

    // ===============================
    // Index
    // ===============================
    [HttpGet]
    public IActionResult Index()
    {
        var gate = GuardarSessao();
        if (gate != null) return gate;

        SetPodeAplicarNaView();

        var vm = new RedistribuicaoCarteiraResultadoVm
        {
            Filtros = GarantirFiltros(new RedistribuicaoCarteiraFiltroVm())
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Buscar(RedistribuicaoCarteiraFiltroVm filtros, CancellationToken ct)
    {
        var gate = GuardarSessao();
        if (gate != null) return gate;

        SetPodeAplicarNaView();

        filtros = GarantirFiltros(filtros);

        try
        {
            var vm = await _svc.BuscarAsync(filtros, ct);
            return View("Index", vm);
        }
        catch (OdbcException ex)
        {
            TempData["Erro"] = $"Não foi possível conectar ao SAP HANA para buscar os dados. Detalhe técnico: {ex.Message}";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            TempData["Erro"] = $"Erro ao buscar dados da redistribuição: {ex.Message}";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Simular(RedistribuicaoCarteiraFiltroVm? filtros, CancellationToken ct)
    {
        var gate = GuardarSessao();
        if (gate != null) return gate;

        SetPodeAplicarNaView();

        filtros = GarantirFiltros(filtros);

        try
        {
            var vm = await _svc.BuscarAsync(filtros!, ct);

            if (vm == null)
                throw new Exception("BuscarAsync retornou vm nulo.");

            vm.Elegiveis ??= new List<ClienteRedistribuicaoVm>();

            vm.Vendedores = await _svc.BuscarVendedoresAsync(ct);

            if (vm.Vendedores == null)
                throw new Exception("BuscarVendedoresAsync retornou nulo.");

            var (sim, resumo) = _svc.SimularRedistribuicao(vm.Elegiveis, vm.Vendedores);

            vm.Simulacao = sim ?? new List<ClienteRedistribuicaoSimuladoVm>();
            vm.Resumo = resumo ?? new List<ResumoVendedorRedistribuicaoVm>();

            HttpContext.Session.SetString(
                "REDIST_RESUMO_JSON",
                JsonSerializer.Serialize(vm.Resumo)
            );

            return View("Index", vm);
        }
        catch (OdbcException ex)
        {
            TempData["Erro"] = $"Não foi possível conectar ao SAP HANA para executar a simulação. Detalhe técnico: {ex}";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            TempData["Erro"] = $"Erro ao executar a simulação: {ex}";
            return RedirectToAction(nameof(Index));
        }
    }

    // ===============================
    // Exportações
    // ===============================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ExportarResumoExcel()
    {
        var gate = GuardarSessao();
        if (gate != null) return gate;

        var json = HttpContext.Session.GetString("REDIST_RESUMO_JSON");
        if (string.IsNullOrWhiteSpace(json))
        {
            TempData["Erro"] = "Resumo não encontrado na sessão. Execute a simulação novamente.";
            return RedirectToAction("Index");
        }

        List<ResumoVendedorRedistribuicaoVm>? resumo;
        try
        {
            resumo = JsonSerializer.Deserialize<List<ResumoVendedorRedistribuicaoVm>>(json);
        }
        catch
        {
            TempData["Erro"] = "Resumo inválido na sessão. Execute a simulação novamente.";
            return RedirectToAction("Index");
        }

        resumo ??= new();

        // filtros são opcionais no excel; se quiser, salve filtros na session e recupere aqui.
        var filtros = CarregarFiltrosDaSessao() ?? new RedistribuicaoCarteiraFiltroVm();
        filtros = Normalizar(filtros);

        var bytes = _svc.GerarExcelResumo(resumo, filtros);

        var nomeArquivo = $"Resumo_Redistribuicao_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            nomeArquivo);
    }

    /*[HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportarCarteiraExcel(RedistribuicaoCarteiraFiltroVm filtros, CancellationToken ct)
    {
        var gate = GuardarSessao();
        if (gate != null) return gate;

        filtros = GarantirFiltros(filtros);

        try
        {
            var vm = await _svc.BuscarAsync(filtros, ct);
            vm.Vendedores = await _svc.BuscarVendedoresAsync(ct);

            var (sim, resumo) = _svc.SimularRedistribuicao(vm.Elegiveis, vm.Vendedores);
            vm.Simulacao = sim;
            vm.Resumo = resumo;

            var bytes = _svc.GerarExcelCarteira(vm, filtros);

            var fileName = $"Redistribuicao_Carteira_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
        catch (Exception ex)
        {
            TempData["Erro"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }*/

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportarCarteiraExcel(RedistribuicaoCarteiraFiltroVm filtros, bool comSimulacao, CancellationToken ct)
    {
        var gate = GuardarSessao();
        if (gate != null) return gate;

        filtros = GarantirFiltros(filtros);

        try
        {
            var vm = await _svc.BuscarAsync(filtros, ct);

            if (comSimulacao)
            {
                vm.Vendedores = await _svc.BuscarVendedoresAsync(ct);

                var (sim, resumo) = _svc.SimularRedistribuicao(vm.Elegiveis, vm.Vendedores);
                vm.Simulacao = sim;
                vm.Resumo = resumo;
            }

            var bytes = _svc.GerarExcelCarteira(vm, filtros);

            var sufixo = comSimulacao ? "ComSimulacao" : "Atual";
            var fileName = $"Redistribuicao_Carteira_{sufixo}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }
        catch (Exception ex)
        {
            TempData["Erro"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    // ===============================
    // Aplicar no SAP
    // ===============================
    public class AplicarRedistribuicaoRequest
    {
        public List<AplicarRedistribuicaoItem> Itens { get; set; } = new();
    }

    public class AplicarRedistribuicaoItem
    {
        public string CodPN { get; set; } = "";
        public int SlpCodeNovo { get; set; }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireReport(KEY_APLICAR)]
    public async Task<IActionResult> AplicarRedistribuicao([FromBody] AplicarRedistribuicaoRequest req, CancellationToken ct)
    {
        var gate = GuardarSessao();
        if (gate != null) return gate;

        if (req?.Itens == null || req.Itens.Count == 0)
            return new JsonResult(new { ok = false, error = "Nenhum item recebido." })
            { StatusCode = StatusCodes.Status400BadRequest };

        var itens = req.Itens
            .Where(x => !string.IsNullOrWhiteSpace(x.CodPN) && x.SlpCodeNovo > 0)
            .GroupBy(x => x.CodPN.Trim().ToUpperInvariant())
            .Select(g => g.Last())
            .ToList();

        var ret = new List<object>(itens.Count);

        foreach (var it in itens)
        {
            var codpn = it.CodPN.Trim().ToUpperInvariant();

            try
            {
                var r = await _sl.PatchBusinessPartnerSegVendedorAutoLoginAsync(codpn, it.SlpCodeNovo, ct);

                ret.Add(new
                {
                    CodPN = codpn,
                    SlpCodeNovo = it.SlpCodeNovo,
                    Ok = r.ok,
                    Erro = r.ok ? null : r.error
                });
            }
            catch (Exception ex)
            {
                ret.Add(new
                {
                    CodPN = codpn,
                    SlpCodeNovo = it.SlpCodeNovo,
                    Ok = false,
                    Erro = ex.Message
                });
            }
        }

        return new JsonResult(ret);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireReport(KEY_APLICAR)]
    public async Task<IActionResult> AplicarRedistribuicaoViaBanco([FromBody] AplicarRedistribuicaoRequest req, CancellationToken ct)
    {
        var gate = GuardarSessao();
        if (gate != null) return gate;

        if (req?.Itens == null || req.Itens.Count == 0)
            return new JsonResult(new { ok = false, error = "Nenhum item recebido." })
            { StatusCode = StatusCodes.Status400BadRequest };

        var itens = req.Itens
            .Where(x => !string.IsNullOrWhiteSpace(x.CodPN) && x.SlpCodeNovo > 0)
            .GroupBy(x => x.CodPN.Trim().ToUpperInvariant())
            .Select(g => g.Last())
            .ToList();

        try
        {
            var resultado = await _svc.AtualizarSegVendedorEmLoteAsync(
                itens.Select(x => (x.CodPN, x.SlpCodeNovo)),
                ct);

            var ret = resultado.Select(r => new
            {
                CodPN = r.CodPN,
                SlpCodeNovo = r.SlpCodeNovo,
                Ok = r.Ok,
                Erro = r.Erro
            });

            return new JsonResult(ret);
        }
        catch (OdbcException ex)
        {
            return new JsonResult(new
            {
                ok = false,
                error = $"Erro ODBC ao atualizar U_SegVendedor em lote: {ex.Message}"
            })
            { StatusCode = StatusCodes.Status500InternalServerError };
        }
        catch (Exception ex)
        {
            return new JsonResult(new
            {
                ok = false,
                error = $"Erro ao atualizar U_SegVendedor em lote: {ex.Message}"
            })
            { StatusCode = StatusCodes.Status500InternalServerError };
        }
    }
}
