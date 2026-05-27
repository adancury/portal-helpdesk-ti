using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PortalHelpdeskTI.Models;
using PortalHelpdeskTI.Services.Comissoes;
using PortalHelpdeskTI.ViewModels.Comissoes;
using System.Text.Json.Serialization;

namespace PortalHelpdeskTI.Controllers
{
    public class ComissaoVendedorController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IComissaoVendedorSyncService _sync;

        public ComissaoVendedorController(AppDbContext db, IComissaoVendedorSyncService sync)
        {
            _db = db;
            _sync = sync;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? filtro, bool embed = false, CancellationToken ct = default)
        {
            if (!await GarantirAcessoTiAsync(ct))
                return RedirectToAction("AcessoNegado", "Account");

            var q = _db.ComissaoVendedores.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(filtro))
            {
                filtro = filtro.Trim();
                q = q.Where(x => x.SlpName.Contains(filtro) || x.SlpCode.ToString().Contains(filtro));
            }

            var itens = await q
                .OrderBy(x => x.SlpName)
                .Select(x => new ComissaoVendedorManutencaoVm.RowVm
                {
                    Id = x.Id,
                    SlpCode = x.SlpCode,
                    SlpName = x.SlpName,
                    Percentual = x.Percentual,
                    Ativo = x.Ativo,

                    BaseCalculo = x.BaseCalculo ?? "FATURAMENTO",
                    TipoVendedor = x.TipoVendedor ?? "REPRESENTANTE",
                    ParticipaRelatorio = x.ParticipaRelatorio,
                    Email = x.Email,

                    // ✅ IMPORTANTE
                    DestacarIR = x.DestacarIR
                })
                .ToListAsync(ct);

            var vm = new ComissaoVendedorManutencaoVm
            {
                Filtro = filtro,
                Itens = itens
            };

            // embed/ajax
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
            ViewData["Embed"] = embed || isAjax;

            return View("~/Views/ComissaoVendedor/Index.cshtml", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Atualizar(bool embed = false, CancellationToken ct = default)
        {
            if (!await GarantirAcessoTiAsync(ct))
                return RedirectToAction("AcessoNegado", "Account");

            var (ins, upd) = await _sync.SyncHanaToSqlAsync(ct);

            TempData["ToastTipo"] = "success";
            TempData["ToastMsg"] = $"Atualização concluída. Inseridos: {ins} | Atualizados: {upd}";

            if (embed)
                return Ok(new { ok = true, message = TempData["ToastMsg"] });

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Salvar([FromBody] SaveInputDto input, CancellationToken ct = default)
        {
            try
            {
                if (!await GarantirAcessoTiAsync(ct))
                    return Unauthorized(new { ok = false, message = "Sem permissão." });

                if (input?.Itens == null || input.Itens.Count == 0)
                    return BadRequest(new { ok = false, message = "Nada para salvar." });

                static bool BaseOk(string v) => v == "FATURAMENTO" || v == "BOLETO";
                static bool TipoOk(string v) => v == "REPRESENTANTE" || v == "INTERNO" || v == "EXTERNO";

                var slpCodes = input.Itens.Select(x => x.SlpCode).Distinct().ToList();

                var rows = await _db.ComissaoVendedores
                    .Where(x => slpCodes.Contains(x.SlpCode))
                    .ToListAsync(ct);

                var map = rows.ToDictionary(x => x.SlpCode);

                int salvos = 0;
                int emailsSubidosHana = 0;

                foreach (var it in input.Itens)
                {
                    ct.ThrowIfCancellationRequested();

                    if (!map.TryGetValue(it.SlpCode, out var row))
                        continue;

                    // 🔒 Regra: inativo NUNCA participa do relatório (força e ignora alterações)
                    if (!row.Ativo)
                    {
                        if (row.ParticipaRelatorio)
                        {
                            row.ParticipaRelatorio = false;
                            salvos++; // conta como alteração aplicada
                        }
                        continue;
                    }

                    // Normaliza inputs
                    var baseNorm = (it.BaseCalculo ?? "").Trim().ToUpperInvariant();
                    var tipoNorm = (it.TipoVendedor ?? "").Trim().ToUpperInvariant();

                    // ✅ Valida apenas para ativos
                    if (!BaseOk(baseNorm) || !TipoOk(tipoNorm))
                    {
                        return BadRequest(new
                        {
                            ok = false,
                            message = $"Valor inválido em BaseCalculo/TipoVendedor (SlpCode={it.SlpCode})."
                        });
                    }

                    // Aplica alterações
                    row.BaseCalculo = baseNorm;
                    row.TipoVendedor = tipoNorm;
                    row.ParticipaRelatorio = it.ParticipaRelatorio;

                    // ✅ DestacarIR só faz sentido para REPRESENTANTE.
                    // Se não for representante, força true (não atrapalha e evita confusão).
                    if (tipoNorm == "REPRESENTANTE")
                        row.DestacarIR = it.DestacarIR;
                    else
                        row.DestacarIR = true;

                    var novoEmail = string.IsNullOrWhiteSpace(it.Email) ? null : it.Email.Trim();
                    var emailMudou = !string.Equals((row.Email ?? ""), (novoEmail ?? ""), StringComparison.OrdinalIgnoreCase);
                    row.Email = novoEmail;

                    salvos++;

                    // Só sobe para o HANA se:
                    // - email do portal foi preenchido/alterado
                    // - e email do HANA estiver vazio
                    if (emailMudou && !string.IsNullOrWhiteSpace(novoEmail))
                    {
                        var ok = await _sync.TryPushEmailToHanaIfEmptyAsync(it.SlpCode, novoEmail!, ct);
                        if (ok) emailsSubidosHana++;
                    }
                }

                await _db.SaveChangesAsync(ct);

                return Ok(new
                {
                    ok = true,
                    message = $"Salvo: {salvos}. E-mails enviados ao HANA (somente quando vazio): {emailsSubidosHana}."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    message = "Erro interno ao salvar.",
                    detail = ex.Message
                });
            }
        }

        // ✅ DTOs locais para bindar JSON camelCase sem depender de config global
        public class SaveInputDto
        {
            [JsonPropertyName("itens")]
            public List<RowSaveDto> Itens { get; set; } = new();
        }

        public class RowSaveDto
        {
            [JsonPropertyName("id")]
            public int Id { get; set; }

            [JsonPropertyName("slpCode")]
            public int SlpCode { get; set; }

            [JsonPropertyName("baseCalculo")]
            public string? BaseCalculo { get; set; }

            [JsonPropertyName("tipoVendedor")]
            public string? TipoVendedor { get; set; }

            [JsonPropertyName("email")]
            public string? Email { get; set; }

            [JsonPropertyName("participaRelatorio")]
            public bool ParticipaRelatorio { get; set; }

            // aceita "destacarIR" vindo do JS
            [JsonPropertyName("destacarIR")]
            public bool DestacarIR { get; set; }
        }

        // ============
        // Segurança TI (dept 1 ou 8)
        // ============
        private async Task<(int UsuarioId, int DepartamentoId)> GetUsuarioLogadoAsync(CancellationToken ct)
        {
            var logadoId = HttpContext.Session.GetInt32("UsuarioId");
            if (!logadoId.HasValue || logadoId.Value <= 0)
                return (0, 0);

            var deptId = await _db.Usuarios
                .Where(u => u.Id == logadoId.Value)
                .Select(u => (int?)u.DepartamentoId)
                .FirstOrDefaultAsync(ct);

            return (logadoId.Value, deptId ?? 0);
        }

        private static bool IsDepartamentoTi(int departamentoId) => departamentoId == 1 || departamentoId == 8;

        private async Task<bool> GarantirAcessoTiAsync(CancellationToken ct)
        {
            var logadoId = HttpContext.Session.GetInt32("UsuarioId");
            if (!logadoId.HasValue || logadoId.Value <= 0)
                return false;

            var deptId = await _db.Usuarios
                .Where(u => u.Id == logadoId.Value)
                .Select(u => (int?)u.DepartamentoId)
                .FirstOrDefaultAsync(ct);

            if (!deptId.HasValue)
                return false;

            return deptId.Value == 1 || deptId.Value == 8;
        }
    }
}
