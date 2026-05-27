using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PortalHelpdeskTI.Models.Comissoes;
using PortalHelpdeskTI.Pdf;
using PortalHelpdeskTI.Services;
using PortalHelpdeskTI.Services.Comissoes;
using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using PortalHelpdeskTI.Infrastructure.Security;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;

namespace PortalHelpdeskTI.Controllers
{
    [RequireReport("COMISSOES")]
    public class ComissoesController : Controller
    {
        private readonly IComissoesService _svc;
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly EnvioComissaoJobStore _job;
        private readonly IMemoryCache _cache;
        private static readonly SemaphoreSlim _resumoLock = new(1, 1);

        public ComissoesController(
            IComissoesService svc,
            AppDbContext db,
            IWebHostEnvironment env,
            EnvioComissaoJobStore job,
            IMemoryCache cache)
        {
            _svc = svc;
            _db = db;
            _env = env;
            _job = job;
            _cache = cache;
        }
        private void SetDownloadCookie(string? dlToken)
        {
            if (string.IsNullOrWhiteSpace(dlToken)) return;

            var cookieName = "dl_" + dlToken;
            Response.Cookies.Append(cookieName, "1", new CookieOptions
            {
                Path = "/",
                HttpOnly = false,   // precisa ser visível ao JS
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.Now.AddMinutes(10)
            });
        }


        // =========================================================
        // EXCEL
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> ExportarResumoExcel(string periodo, string dlToken, CancellationToken ct)

        {
            if (string.IsNullOrWhiteSpace(periodo))
            {
                SetDownloadCookie(dlToken);
                return BadRequest("Período não informado.");
            }

            // periodo esperado: "YYYY-MM"
            var parts = periodo.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !int.TryParse(parts[0], out var ano) || !int.TryParse(parts[1], out var mes))
                return BadRequest("Período inválido. Use o formato YYYY-MM.");

            var cacheKey = $"comissao:resumo:{ano:D4}-{mes:D2}";

            if (!_cache.TryGetValue(cacheKey, out ResumoComissaoVm? resumo) || resumo == null)
            {
                resumo = await _svc.GerarResumoAsync(ano, mes, ct);

                _cache.Set(cacheKey, resumo, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(20)
                });
            }

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using var pkg = new ExcelPackage();
            var ws = pkg.Workbook.Worksheets.Add("Resumo");

            // Cabeçalho
            var headers = new[]
            {
            "Vendedor",
            "SlpCode",
            "Tipo",
            "Base",
            "Receita Líquida",
            "Comissão",
            "Descontos",
            "Valor a Receber"
        };

            for (int c = 0; c < headers.Length; c++)
            {
                ws.Cells[1, c + 1].Value = headers[c];
                ws.Cells[1, c + 1].Style.Font.Bold = true;
                ws.Cells[1, c + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                ws.Cells[1, c + 1].Style.Fill.BackgroundColor.SetColor(Color.LightGray);
                ws.Cells[1, c + 1].Style.Border.BorderAround(ExcelBorderStyle.Thin);
            }

            // Linhas
            var row = 2;
            foreach (var l in resumo.Linhas)
            {
                ws.Cells[row, 1].Value = l.SlpName;
                ws.Cells[row, 2].Value = l.SlpCode;
                ws.Cells[row, 3].Value = l.TipoVendedor;
                ws.Cells[row, 4].Value = l.BaseCalculo;

                ws.Cells[row, 5].Value = (double)l.ReceitaLiquida;
                ws.Cells[row, 6].Value = (double)l.ComissaoBruta;
                ws.Cells[row, 7].Value = (double)l.Descontos;
                ws.Cells[row, 8].Value = (double)l.ValorReceber;

                row++;
            }

            // Formatação
            ws.Column(5).Style.Numberformat.Format = "#,##0.00";
            ws.Column(6).Style.Numberformat.Format = "#,##0.00";
            ws.Column(7).Style.Numberformat.Format = "#,##0.00";
            ws.Column(8).Style.Numberformat.Format = "#,##0.00";

            ws.Cells[1, 1, row - 1, headers.Length].AutoFitColumns();

            // Linha de totais (opcional)
            ws.Cells[row + 1, 4].Value = "Totais:";
            ws.Cells[row + 1, 4].Style.Font.Bold = true;

            ws.Cells[row + 1, 5].Formula = $"SUM(E2:E{row - 1})";
            ws.Cells[row + 1, 6].Formula = $"SUM(F2:F{row - 1})";
            ws.Cells[row + 1, 7].Formula = $"SUM(G2:G{row - 1})";
            ws.Cells[row + 1, 8].Formula = $"SUM(H2:H{row - 1})";

            ws.Cells[row + 1, 5, row + 1, 8].Style.Font.Bold = true;
            ws.Cells[row + 1, 5, row + 1, 8].Style.Numberformat.Format = "#,##0.00";

            // Retorno
            var bytes = pkg.GetAsByteArray();
            var fileName = $"Comissoes_Resumo_{ano:D4}-{mes:D2}.xlsx";
            if (!string.IsNullOrWhiteSpace(dlToken))
            {
                var cookieName = "dl_" + dlToken;
                Response.Cookies.Append(cookieName, "1", new CookieOptions
                {
                    Path = "/",
                    HttpOnly = false,   // precisa ser visível ao JS
                    Secure = Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    Expires = DateTimeOffset.Now.AddMinutes(10)
                });
            }
            SetDownloadCookie(dlToken);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        // =========================================================
        // DESCONTOS
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdicionarDesconto(
        int slpCode,
        DateTime ini,
        DateTime fim,
        string tipo,
        string valor,
        string? observacao,
        string? nfsRelacionadas,
        string? periodo,
        string? competencia,
        int parcelas,
        CancellationToken ct)
        {
            ini = ini.Date;
            fim = fim.Date;

            if (fim < ini)
            {
                TempData["Erro"] = "Período inválido.";
                return RedirectToAction("Relatorio", new
                {
                    slpCode,
                    ini = ini.ToString("yyyy-MM-dd"),
                    fim = fim.ToString("yyyy-MM-dd"),
                    periodo
                });
            }

            var tiposPermitidos = new HashSet<string>
    {
        "DESCONTO_REP_DEVOLUCAO",
        "DESCONTO_REP_FRETE",
        "DESCONTO_REP_OUTROS"
    };

            if (!tiposPermitidos.Contains(tipo))
            {
                TempData["Erro"] = "Tipo de desconto inválido.";
                return RedirectToAction("Relatorio", new
                {
                    slpCode,
                    ini = ini.ToString("yyyy-MM-dd"),
                    fim = fim.ToString("yyyy-MM-dd"),
                    periodo
                });
            }

            // Parse pt-BR seguro
            var cultura = new CultureInfo("pt-BR");
            var valorNorm = (valor ?? "").Trim();

            if (string.IsNullOrWhiteSpace(valorNorm))
            {
                TempData["Erro"] = "Informe um valor válido para o desconto.";
                return RedirectToAction("Relatorio", new
                {
                    slpCode,
                    ini = ini.ToString("yyyy-MM-dd"),
                    fim = fim.ToString("yyyy-MM-dd"),
                    periodo
                });
            }

            // Aceita "343.45" e "343,45"
            if (valorNorm.Contains('.') && !valorNorm.Contains(','))
                valorNorm = valorNorm.Replace('.', ',');

            if (!decimal.TryParse(valorNorm, NumberStyles.Number, cultura, out var valorTotal) || valorTotal <= 0)
            {
                TempData["Erro"] = "Valor inválido. Ex.: 343,45";
                return RedirectToAction("Relatorio", new
                {
                    slpCode,
                    ini = ini.ToString("yyyy-MM-dd"),
                    fim = fim.ToString("yyyy-MM-dd"),
                    periodo
                });
            }

            // ✅ Competência inicial: competencia (modal) > periodo (tela) > mês do fim do relatório
            var compStr = (competencia ?? "").Trim();
            if (string.IsNullOrWhiteSpace(compStr))
                compStr = (periodo ?? "").Trim();

            int compAno, compMes;
            if (!TryParseCompetencia(compStr, out compAno, out compMes))
            {
                compAno = fim.Year;
                compMes = fim.Month;
            }

            if (parcelas < 1) parcelas = 1;
            if (parcelas > 36) parcelas = 36;

            // ✅ Rateio com fechamento de centavos na última parcela
            var valorParcelaBase = Math.Round(valorTotal / parcelas, 2, MidpointRounding.AwayFromZero);
            var somaParcial = valorParcelaBase * (parcelas - 1);
            var valorUltima = Math.Round(valorTotal - somaParcial, 2, MidpointRounding.AwayFromZero);

            // ✅ Lote p/ rastreio (sem mudar banco)
            var lote = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

            for (int i = 0; i < parcelas; i++)
            {
                var dt = new DateTime(compAno, compMes, 1).AddMonths(i);
                var pIni = new DateTime(dt.Year, dt.Month, 1);
                var pFim = new DateTime(dt.Year, dt.Month, DateTime.DaysInMonth(dt.Year, dt.Month));

                var v = (i == parcelas - 1) ? valorUltima : valorParcelaBase;

                var obs = (observacao ?? "").Trim();
                if (parcelas > 1)
                {
                    var prefixo = $"[Parcela {i + 1}/{parcelas} | Lote {lote}]";
                    obs = string.IsNullOrWhiteSpace(obs) ? prefixo : $"{prefixo} {obs}";
                }

                _db.ComissaoAjustes.Add(new ComissaoAjuste
                {
                    SlpCode = slpCode,
                    DataIni = pIni,
                    DataFim = pFim,
                    Tipo = tipo,
                    Valor = v,
                    Observacao = string.IsNullOrWhiteSpace(obs) ? null : obs,
                    NFsRelacionadas = nfsRelacionadas
                });
            }

            await _db.SaveChangesAsync(ct);

            TempData["Ok"] = parcelas == 1
                ? "Desconto lançado com sucesso."
                : $"Desconto lançado em {parcelas} parcelas (competências futuras registradas).";

            return RedirectToAction("Relatorio", new
            {
                slpCode,
                ini = ini.ToString("yyyy-MM-dd"),
                fim = fim.ToString("yyyy-MM-dd"),
                periodo
            });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExcluirDesconto(
            int id,
            int slpCode,
            DateTime ini,
            DateTime fim,
            string? periodo,
            CancellationToken ct)
        {
            var item = await _db.ComissaoAjustes.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (item != null && item.Tipo.StartsWith("DESCONTO_REP_"))
            {
                _db.ComissaoAjustes.Remove(item);
                await _db.SaveChangesAsync(ct);
                TempData["Ok"] = "Desconto removido.";
            }

            return RedirectToAction("Relatorio", new
            {
                slpCode,
                ini = ini.ToString("yyyy-MM-dd"),
                fim = fim.ToString("yyyy-MM-dd"),
                periodo
            });
        }

        // =========================================================
        // RELATORIO - MANUAL
        // =========================================================

        [HttpGet]
        public async Task<IActionResult> Relatorio(int slpCode, DateTime ini, DateTime fim, string? periodo, CancellationToken ct)
        {
            var vm = await _svc.GerarRelatorioAsync(slpCode, ini, fim, ct);
            ViewBag.PeriodoResumo = periodo;
            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> RelatorioPdf(int slpCode, DateTime ini, DateTime fim, CancellationToken ct)
        {
            var vm = await _svc.GerarRelatorioAsync(slpCode, ini, fim, ct);
            var logoPath = Path.Combine(_env.WebRootPath, "images", "logo.png");
            var bytes = ComissaoPdfBuilder.Gerar(vm, logoPath);
            return File(bytes, "application/pdf", $"Comissao_{vm.SlpCode}_{ini:yyyyMMdd}_{fim:yyyyMMdd}.pdf");
        }

        // =========================================================
        // RELATORIO - AUTOMATICO (Ref mês/ano)
        // =========================================================

        [HttpGet]
        public async Task<IActionResult> RelatorioRef(int slpCode, int ano, int mes, string? periodo, CancellationToken ct)
        {
            if (mes < 1 || mes > 12) return BadRequest("Mês inválido.");

            var vend = await _db.ComissaoVendedores
                .Where(x => x.SlpCode == slpCode && x.Ativo)
                .Select(x => new { x.SlpCode, x.TipoVendedor })
                .FirstOrDefaultAsync(ct);

            if (vend == null) return NotFound("Vendedor não encontrado/ativo.");

            var (ini, fim) = CalcularPeriodoApuracao(vend.TipoVendedor, ano, mes);

            var vm = await _svc.GerarRelatorioAsync(slpCode, ini, fim, ct);

            ViewBag.PeriodoResumo = periodo ?? $"{ano}-{mes}";
            return View("Relatorio", vm);
        }

        [HttpGet]
        public async Task<IActionResult> RelatorioPdfRef(int slpCode, int ano, int mes, CancellationToken ct)
        {
            if (mes < 1 || mes > 12) return BadRequest("Mês inválido.");

            var vend = await _db.ComissaoVendedores
                .Where(x => x.SlpCode == slpCode && x.Ativo)
                .Select(x => new { x.SlpCode, x.TipoVendedor })
                .FirstOrDefaultAsync(ct);

            if (vend == null) return NotFound("Vendedor não encontrado/ativo.");

            var (ini, fim) = CalcularPeriodoApuracao(vend.TipoVendedor, ano, mes);

            var vm = await _svc.GerarRelatorioAsync(slpCode, ini, fim, ct);
            var logoPath = Path.Combine(_env.WebRootPath, "images", "logo.png");
            var bytes = ComissaoPdfBuilder.Gerar(vm, logoPath);

            return File(bytes, "application/pdf", $"Comissao_{vm.SlpCode}_{ini:yyyyMMdd}_{fim:yyyyMMdd}.pdf");
        }

        // =========================================================
        // RESUMO / INDEX
        // =========================================================

        [HttpGet]
        public IActionResult Index()
        {
            ViewBag.Periodos = GerarListaPeriodos();
            return View(model: null);
        }

        [HttpGet]
        public async Task<IActionResult> Resumo(string? periodo, int? refreshSlpCode, CancellationToken ct)
        {
            ViewBag.Periodos = GerarListaPeriodos();

            if (string.IsNullOrWhiteSpace(periodo))
                return View("Index"); // só mostra filtro

            if (!TryParsePeriodo(periodo, out var ano, out var mes))
            {
                TempData["Erro"] = "Período inválido.";
                return View("Index");
            }

            var cacheKey = $"comissao:resumo:{ano:D4}-{mes:D2}";

            ResumoComissaoVm? vm = null;

            // 1) tenta pegar do cache
            if (!_cache.TryGetValue(cacheKey, out vm) || vm == null)
            {
                await _resumoLock.WaitAsync(ct);
                try
                {
                    // ✅ double-check dentro do lock
                    if (!_cache.TryGetValue(cacheKey, out vm) || vm == null)
                    {
                        vm = await _svc.GerarResumoAsync(ano, mes, ct);

                        _cache.Set(cacheKey, vm, new MemoryCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(20)
                        });
                        Console.WriteLine($"[Resumo] cacheKey={cacheKey} SET");

                    }
                }
                finally
                {
                    _resumoLock.Release();
                }
            }

            // 2) refresh de uma linha (opcional: pode colocar lock também se quiser reduzir carga)
            if (refreshSlpCode.HasValue && vm != null)
            {
                var slpCode = refreshSlpCode.Value;

                var vend = await _db.ComissaoVendedores
                    .Where(x => x.Ativo && x.ParticipaRelatorio && x.SlpCode == slpCode)
                    .Select(x => new { x.SlpCode, x.SlpName, x.TipoVendedor, x.BaseCalculo })
                    .FirstOrDefaultAsync(ct);

                if (vend != null)
                {
                    var (ini, fim) = CalcularPeriodoApuracao(vend.TipoVendedor, ano, mes);

                    var rel = await _svc.GerarRelatorioAsync(slpCode, ini, fim, ct);

                    var linha = vm.Linhas.FirstOrDefault(x => x.SlpCode == slpCode);
                    if (linha != null)
                    {
                        linha.SlpName = vend.SlpName;
                        linha.TipoVendedor = vend.TipoVendedor;
                        linha.BaseCalculo = vend.BaseCalculo;
                        linha.ReceitaLiquida = rel.ReceitaLiquida;
                        linha.ComissaoBruta = rel.ComissaoBruta;
                        linha.Tributos = rel.Tributos;

                        // ⚠️ você estava perdendo desconto condicionado + outros:
                        // se no resumo você usa "Descontos = rel.DescontoCondicionado + rel.DescontosRepresentante"
                        // mantenha o mesmo padrão:
                        linha.Descontos = rel.DescontoCondicionado + rel.DescontosRepresentante;

                        // se você adicionou QtdLinhas:
                        // linha.QtdLinhas = (rel.Linhas?.Count ?? 0) + (rel.Devolucoes?.Count ?? 0);
                    }

                    vm.DataIni = ini;
                    vm.DataFim = fim;

                    _cache.Set(cacheKey, vm, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(20)
                    });
                    Console.WriteLine($"[Resumo] cacheKey={cacheKey} SET");
                }
            }

            ViewBag.PeriodoSelecionado = periodo;
            return View("Index", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult GerarResumo(string periodo)
        {
            if (string.IsNullOrWhiteSpace(periodo))
            {
                TempData["Erro"] = "Selecione um período.";
                return RedirectToAction(nameof(Resumo));
            }

            // Redireciona para GET (evita voltar para POST no navegador)
            return RedirectToAction(nameof(Resumo), new { periodo });
        }

        // =========================================================
        // ENVIO DE EMAILS (JOB)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EnviarEmailsStart(string periodo, string grupo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(periodo))
                    return BadRequest(new { error = "Período não informado." });

                grupo = (grupo ?? "").Trim().ToUpperInvariant();
                if (grupo != "IE" && grupo != "REP" && grupo != "FISCAL")
                    return BadRequest(new { error = "Grupo inválido. Use IE (Interno/Externo) ou REP (Representantes)." });

                // inicia job (não bloqueia request)
                var jobId = _job.StartJob(periodo, grupo);

                return Ok(new { jobId });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EnviarEmails(string periodo, string grupo)
        {
            if (string.IsNullOrWhiteSpace(periodo))
            {
                TempData["Erro"] = "Selecione um período.";
                return RedirectToAction(nameof(Resumo));
            }

            grupo = (grupo ?? "").Trim().ToUpperInvariant();

            if (grupo != "IE" && grupo != "REP" && grupo != "FISCAL")
            {
                TempData["Erro"] = "Grupo inválido para envio de e-mails.";
                return RedirectToAction(nameof(Resumo), new { periodo });
            }

            var jobId = _job.StartJob(periodo, grupo);
            return RedirectToAction(nameof(Resumo), new { periodo, jobId });
        }

        [HttpGet]
        public IActionResult EnvioStatus(string jobId)
        {
            var st = _job.Get(jobId);
            if (st == null) return NotFound();

            return Json(new
            {
                st.Status,
                st.Total,
                st.Processados,
                st.Enviados,
                st.Falhas,
                st.SemEmail,
                st.Finished,
                st.FinishedAt,
                falhasItens = st.FalhasItens // <-- adicionar
            });
        }

        // =========================================================
        // HELPERS
        // =========================================================
        private static bool TryParseCompetencia(string? competencia, out int ano, out int mes)
        {
            ano = 0;
            mes = 0;

            var c = (competencia ?? "").Trim();
            if (string.IsNullOrWhiteSpace(c)) return false;

            var parts = c.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return false;

            if (!int.TryParse(parts[0], out ano)) return false;
            if (!int.TryParse(parts[1], out mes)) return false;

            return ano >= 2000 && ano <= 2100 && mes >= 1 && mes <= 12;
        }

        private static (DateTime Ini, DateTime Fim) CalcularPeriodoApuracao(string? tipoVendedor, int ano, int mes)
        {
            var tipo = (tipoVendedor ?? "").Trim().ToUpperInvariant();

            if (tipo == "REPRESENTANTE")
            {
                var ini = new DateTime(ano, mes, 1);
                var fim = new DateTime(ano, mes, DateTime.DaysInMonth(ano, mes));
                return (ini, fim);
            }

            var primeiroDiaMesRef = new DateTime(ano, mes, 1);
            var mesAnterior = primeiroDiaMesRef.AddMonths(-1);

            var ini2 = new DateTime(mesAnterior.Year, mesAnterior.Month, 26);
            var fim2 = new DateTime(ano, mes, 25);
            return (ini2, fim2);
        }

        private static List<SelectListItem> GerarListaPeriodos()
        {
            var lista = new List<SelectListItem>();
            var hoje = DateTime.Today;

            for (int i = 0; i <= 6; i++)
            {
                var d = hoje.AddMonths(-i);
                lista.Add(new SelectListItem
                {
                    Value = $"{d.Year}-{d.Month:D2}",
                    Text = d.ToString("MMMM/yyyy", new CultureInfo("pt-BR"))
                });
            }

            return lista;
        }

        private static bool TryParsePeriodo(string periodo, out int ano, out int mes)
        {
            ano = 0;
            mes = 0;

            var parts = periodo.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return false;

            if (!int.TryParse(parts[0], out ano)) return false;
            if (!int.TryParse(parts[1], out mes)) return false;

            return mes >= 1 && mes <= 12;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SincronizarVendedores(string? periodo, CancellationToken ct)
        {
            try
            {
                // 1) Busca no SAP
                var sapVendedores = await _svc.BuscarVendedoresSapAsync(ct);

                // 2) Carrega os do portal (para upsert)
                var portal = await _db.ComissaoVendedores.ToListAsync(ct);
                var portalByCode = portal.ToDictionary(x => x.SlpCode, x => x);

                int inseridos = 0;
                int atualizados = 0;

                foreach (var s in sapVendedores)
                {
                    var ativoSap = (s.Active ?? "").Trim().Equals("Y", StringComparison.OrdinalIgnoreCase);

                    // Normaliza e-mail
                    var emailSap = (s.E_Mail ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(emailSap)) emailSap = null;

                    if (!portalByCode.TryGetValue(s.SlpCode, out var p))
                    {
                        // NOVO: cria com defaults do portal
                        var novo = new ComissaoVendedor
                        {
                            SlpCode = s.SlpCode,
                            SlpName = s.SlpName?.Trim() ?? "",
                            Ativo = ativoSap,

                            // Defaults (ajuste se quiser)
                            Percentual = 0.0449m,          // ou 0m se preferir não presumir
                            BaseCalculo = "FATURAMENTO",   // conforme sua regra
                            TipoVendedor = "REPRESENTANTE",// conforme sua regra inicial
                            Email = emailSap
                        };

                        _db.ComissaoVendedores.Add(novo);
                        inseridos++;
                    }
                    else
                    {
                        // EXISTE: atualiza apenas campos vindos do SAP
                        var novoNome = s.SlpName?.Trim() ?? "";
                        var mudou = false;

                        if (!string.Equals(p.SlpName ?? "", novoNome, StringComparison.Ordinal))
                        {
                            p.SlpName = novoNome;
                            mudou = true;
                        }

                        if (p.Ativo != ativoSap)
                        {
                            p.Ativo = ativoSap;
                            mudou = true;
                        }

                        // Atualiza email se veio preenchido no SAP; se veio vazio, não apaga o do portal
                        if (!string.IsNullOrWhiteSpace(emailSap) && !string.Equals((p.Email ?? "").Trim(), emailSap, StringComparison.OrdinalIgnoreCase))
                        {
                            p.Email = emailSap;
                            mudou = true;
                        }

                        if (mudou) atualizados++;
                    }
                }

                await _db.SaveChangesAsync(ct);

                TempData["Ok"] = $"Sincronização concluída. Inseridos: {inseridos}. Atualizados: {atualizados}. Total SAP: {sapVendedores.Count}.";
            }
            catch (Exception ex)
            {
                TempData["Erro"] = $"Falha ao sincronizar vendedores: {ex.Message}";
            }

            // Volta para a tela (mantém período se houver)
            if (!string.IsNullOrWhiteSpace(periodo))
                return RedirectToAction("Resumo", new { periodo });

            return RedirectToAction("Resumo");
        }

        [HttpGet]
        public async Task<IActionResult> ResumoGeralPdf(string periodo, CancellationToken ct)
        {
            if (!TryParsePeriodo(periodo, out var ano, out var mes))
                return BadRequest("Período inválido.");

            var cacheKey = $"comissao:resumo:{ano:D4}-{mes:D2}";
            var hit = _cache.TryGetValue(cacheKey, out ResumoComissaoVm? dbg) && dbg != null;
            Console.WriteLine($"[ResumoGeralPdf] cacheKey={cacheKey} HIT={hit}");

            ResumoComissaoVm? vm = null;

            // 1) tenta pegar do cache
            if (!_cache.TryGetValue(cacheKey, out vm) || vm == null)
            {
                await _resumoLock.WaitAsync(ct);
                try
                {
                    // ✅ double-check dentro do lock
                    if (!_cache.TryGetValue(cacheKey, out vm) || vm == null)
                    {
                        vm = await _svc.GerarResumoAsync(ano, mes, ct);

                        _cache.Set(cacheKey, vm, new MemoryCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(20)
                        });
                    }
                }
                finally
                {
                    _resumoLock.Release();
                }
            }

            if (vm?.Linhas == null || vm.Linhas.Count == 0)
                return NotFound("Sem dados para o período.");

            // Use o mesmo padrão do resto do controller (melhor do que Directory.GetCurrentDirectory)
            var logoPath = Path.Combine(_env.WebRootPath, "images", "logo.png");

            var pdfBytes = ComissaoResumoGeralPdfBuilder.Gerar(vm, logoPath);

            var nome = $"Comissao_Representantes_{ano:D4}_{mes:D2}.pdf";
            return File(pdfBytes, "application/pdf", nome);
        }
    }
}
