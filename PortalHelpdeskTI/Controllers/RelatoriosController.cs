// ======================
// iText (PDF)
// ======================
//barcodes
using PortalHelpdeskTI.Services.SAP;
using iText.Barcodes;
using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Events;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using PortalHelpdeskTI.Models.Relatorios;
using PortalHelpdeskTI.Services.Relatorios;
// ======================
// EPPlus (Excel)
// ======================
using OfficeOpenXml;
using OfficeOpenXml.Drawing.Chart;
using OfficeOpenXml.Style;
using PortalHelpdeskTI.Infrastructure.Security;
using PortalHelpdeskTI.Services;
using PortalHelpdeskTI.Services.Permissoes;
using PortalHelpdeskTI.Services.ServiceLayer;
using PortalHelpdeskTI.ViewModels.Relatorios;
using PortalHelpdeskTI.Views.Relatorios;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
// ----------------------
// Aliases iText (para manter seu código)
// ----------------------
using AB = iText.Layout.Element.AreaBreak;
using ABT = iText.Layout.Properties.AreaBreakType;
using CellEl = iText.Layout.Element.Cell;
using Doc = iText.Layout.Document;
using DxColor = System.Drawing.Color;
using Img = iText.Layout.Element.Image;
using IOPath = System.IO.Path;
// Alias para Border iText (evita conflito com EPPlus Border)
using ITextBorder = iText.Layout.Borders.Border;
using Para = iText.Layout.Element.Paragraph;
using PdfDoc = iText.Kernel.Pdf.PdfDocument;
using RGB = iText.Kernel.Colors.DeviceRgb;
using Tbl = iText.Layout.Element.Table;

public class RelatoriosController : Controller
{
    private readonly RelatorioTempoService _relatorioTempo;
    private readonly RelatorioProdutosService _relProdutos;
    private readonly RepresentantesVendasService _repVendas;
    private readonly MudancaCarteiraService _mudancaCarteira;
    private readonly RelatorioRupturasService _relatorioRupturas;
    private readonly RelatorioRupturaPrevisaoService _rupturaPrevisao;
    private readonly StatusIndicadorService _status;
    private readonly CadColaboradorService _cadColaboradorService;
    private readonly PrevisaoRupturaService _prevRuptura;
    private readonly DashboardLiberacaoPedidosService _dashService;
    private readonly DashboardSavingComprasService _dashboardSavingComprasService;
    private readonly IRelatorioPermissaoService _relPerms;
    private readonly RelatoriosCatalogoService _catalogo;
    private readonly IndicadorTiService _indicadorTi;
    private readonly InativacaoParceiroService _inativacaoParceiroService;
    private readonly IMemoryCache _cache;
    private readonly ServiceLayerClient _slClient;
    private readonly RupturaPrevisaoJobRunner _rupturaPrevisaoJobRunner;

    public RelatoriosController(
    RelatorioTempoService relatorioTempo,
    RelatorioProdutosService relProdutos,
    RepresentantesVendasService repVendas,
    MudancaCarteiraService mudancaCarteira,
    RelatorioRupturasService relatorioRupturas,
    RelatorioRupturaPrevisaoService rupturaPrevisao,
    StatusIndicadorService status,
    CadColaboradorService cadColaboradorService,
    PrevisaoRupturaService prevRuptura,
    DashboardLiberacaoPedidosService dashService,
    DashboardSavingComprasService dashboardSavingComprasService,
    IRelatorioPermissaoService relPerms,
    RelatoriosCatalogoService catalogo,
    IndicadorTiService indicadorTi,
    InativacaoParceiroService inativacaoParceiroService,
    IMemoryCache cache,
    ServiceLayerClient slClient,
    RupturaPrevisaoJobRunner rupturaPrevisaoJobRunner)
    {
        _relatorioTempo = relatorioTempo;
        _relProdutos = relProdutos;
        _repVendas = repVendas;
        _mudancaCarteira = mudancaCarteira;
        _relatorioRupturas = relatorioRupturas;
        _rupturaPrevisao = rupturaPrevisao;
        _status = status;
        _cadColaboradorService = cadColaboradorService;
        _prevRuptura = prevRuptura;
        _dashService = dashService;
        _dashboardSavingComprasService = dashboardSavingComprasService;
        _relPerms = relPerms;
        _catalogo = catalogo;
        _indicadorTi = indicadorTi;
        _inativacaoParceiroService = inativacaoParceiroService;
        _cache = cache;
        _slClient = slClient;
        _rupturaPrevisaoJobRunner = rupturaPrevisaoJobRunner;
    }

    const float FONT_BASE = 9.5f;
    const float FONT_SMALL = 9.0f;
    const float PAD_Y = 5f;
    const float PAD_X = 6f;

    private static string CacheKeyInativarMassa(string jobId)
    => $"InativarMassa:Status:{jobId}";

    // -------- Index --------
    [HttpGet]
    public async Task<IActionResult> Index(string? cat = null, string? q = null)
    {
        var usuarioId = HttpContext.Session.GetInt32("UsuarioId");
        if (!usuarioId.HasValue || usuarioId.Value <= 0)
            return RedirectToAction("Login", "Account");

        var catalogo = await _catalogo.ListarAtivosAsync(HttpContext.RequestAborted);

        // ============================
        // KPI do card do Indicador TI (calcula 1x)
        // ============================
        var serieTi = await _indicadorTi.GerarMensalAsync(null, null);
        var ultimoTi = serieTi.OrderByDescending(x => x.MesRef).FirstOrDefault();

        var itens = new List<ReportsIndexVm.ReportItemVm>();

        foreach (var r in catalogo)
        {
            var key = (r.Key ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            if (string.IsNullOrWhiteSpace(r.UrlVisualizar))
                continue;

            if (!await _relPerms.PodeVerAsync(usuarioId.Value, key, HttpContext.RequestAborted))
                continue;

            var item = new ReportsIndexVm.ReportItemVm
            {
                Key = r.Key,
                Titulo = r.Titulo,
                Descricao = r.Descricao,
                Departamento = r.Departamento,
                Formato = "Tela",
                Tags = new List<string> { r.Departamento },
                UrlVisualizar = r.UrlVisualizar,
                AtualizadoEm = DateTime.UtcNow,
                Favorito = false
            };


            // ------------------------------
            // KPI do card: Indicador TI
            // ------------------------------
            if (key == "INDICADOR_TI" && ultimoTi != null)
            {
                item.KpiUltimoValorPct = ultimoTi.IndicadorFinal;
                item.KpiUltimoMes = ultimoTi.MesRef.ToString("MM/yyyy");
                item.KpiAtingiuMeta = ultimoTi.IndicadorFinal >= 95.0;
                item.KpiBadgeTexto = item.KpiAtingiuMeta.Value
                    ? "Meta 95% atingida"
                    : "Meta 95% não atingida";

                // opcional: faz o "AtualizadoEm" refletir o mês do KPI
                item.AtualizadoEm = new DateTime(ultimoTi.MesRef.Year, ultimoTi.MesRef.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            }

            itens.Add(item);
        }

        var categorias = itens
            .Select(x => (x.Departamento ?? "").Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        categorias.Insert(0, "Todos");

        var vm = new ReportsIndexVm
        {
            Categorias = categorias,
            Itens = itens,
            CategoriaAtiva = string.IsNullOrWhiteSpace(cat) ? "Todos" : cat,
            Busca = q
        };

        return View("~/Views/Relatorios/Index.cshtml", vm);
    }


    [HttpGet]
    [RequireReport("DASH_SAVING_COMPRAS")]
    public async Task<IActionResult> DetalhesProcessoCompra(int id)
    {
        var vm = await _dashboardSavingComprasService.ObterDetalhesProcessoAsync(id);
        if (vm == null)
            return NotFound();

        return View("DetalhesProcessoCompra", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireReport("DASH_SAVING_COMPRAS")]
    public async Task<IActionResult> AdicionarCotacaoConcorrente(ProcessoCompraDetalhesVM model)
    {
        if (model == null || model.NovaCotacao == null)
            return BadRequest();

        var nova = model.NovaCotacao;

        if (nova.ProcessoCompraId <= 0)
            return BadRequest();

        int? usuarioId = HttpContext.Session.GetInt32("UsuarioId");

        await _dashboardSavingComprasService.AdicionarCotacaoConcorrenteAsync(nova, usuarioId);

        TempData["ToastTipo"] = "success";
        TempData["ToastMsg"] = "Cotação concorrente adicionada com sucesso.";

        return RedirectToAction(nameof(DetalhesProcessoCompra), new { id = nova.ProcessoCompraId });
    }

    [HttpGet]
    [RequireReport("DASH_SAVING_COMPRAS")]
    public async Task<IActionResult> DashboardSavingCompras(
        DateTime? dataDe,
        DateTime? dataAte,
        string[]? departamentos,
        string? equipeResponsavel,
        int pagina = 1,
        string? termoBusca = null)
    {
        var hoje = DateTime.Today;

        var inicioMes = new DateTime(hoje.Year, hoje.Month, 1);
        var fimMes = inicioMes.AddMonths(1).AddDays(-1);

        var de = dataDe ?? inicioMes;
        var ate = dataAte ?? fimMes;

        var vm = await _dashboardSavingComprasService.GerarResumoPeriodoAsync(
            de,
            ate,
            departamentos,
            equipeResponsavel,
            pagina,
            termoBusca
        );

        return View("DashboardSavingCompras", vm);
    }

    [HttpGet]
    [RequireReport("DASH_LIBERACAO_PEDIDOS")]
    public async Task<IActionResult> DashboardLiberacaoPedidos(
        DateTime? de,
        DateTime? ate,
        string tipos,
        string tipoData = "Pedido")
    {
        var vm = await _dashService.BuscarAsync(de, ate, tipos, tipoData);
        return View(vm);
    }

    // -------- Relatório de Tempo --------
    [HttpGet]
    [RequireReport("RELATORIO_TEMPO")]
    public async Task<IActionResult> Tempo(DateTime? de, DateTime? ate)
    {
        var dados = await _relatorioTempo.GerarAsync(de, ate);

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            return PartialView("~/Views/Configuracoes/_GridRelatorioTempo.cshtml", dados);

        return View("~/Views/Chamados/RelatorioTempo.cshtml", dados);
    }

    // -------- Dados Produtos --------
    [HttpGet]
    [RequireReport("DADOS_PRODUTOS")]
    public async Task<IActionResult> DadosProdutos(int page = 1, int pageSize = 200, string? q = null)
    {
        var model = await _relProdutos.ExecutarPaginadoAsync(page, pageSize, q, HttpContext.RequestAborted);

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            return PartialView("~/Views/Relatorios/_GridDadosProdutos_DataTable.cshtml", model);

        return View("~/Views/Relatorios/DadosProdutos.cshtml", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireReport("DADOS_PRODUTOS")]
    public async Task<IActionResult> ExportCsv_DadosProdutos(int page = 1, int pageSize = 20, string? q = null)
    {
        var pageModel = await _relProdutos.ExecutarPaginadoAsync(page, pageSize, q, HttpContext.RequestAborted);
        var csv = DataTableToCsv(pageModel.Data);
        var fileName = $"DadosProdutos_p{page}_n{pageSize}_q{(q ?? "vazio")}_{DateTime.Now:yyyyMMdd_HHmm}.csv";
        return File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", fileName);
    }

    // -------- Representantes --------
    [HttpGet]
    [RequireReport("REPRESENTANTES")]
    public async Task<IActionResult> RepresentantesVendas(int page = 1, int pageSize = 20, string? q = null)
    {
        var model = await _repVendas.ExecutarPaginadoAsync(page, pageSize, q, HttpContext.RequestAborted);

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            return PartialView("~/Views/Relatorios/_GridRepresentantesVendas_DataTable.cshtml", model);

        return View("~/Views/Relatorios/RepresentantesVendas.cshtml", model);
    }

    // -------- Mudança Carteira --------
    [HttpGet]
    [RequireReport("MUDANCA_CARTEIRA")]
    public IActionResult MudancaCarteira()
    {
        var vm = new MudancaCarteiraUploadViewModel();
        return View("~/Views/Relatorios/MudancaCarteira.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireReport("MUDANCA_CARTEIRA")]
    public async Task<IActionResult> MudancaCarteira(MudancaCarteiraUploadViewModel model)
    {
        if (!ModelState.IsValid)
            return View("~/Views/Relatorios/MudancaCarteira.cshtml", model);

        if (model.Arquivo == null || model.Arquivo.Length == 0)
        {
            ModelState.AddModelError(nameof(model.Arquivo), "Selecione um arquivo Excel.");
            return View("~/Views/Relatorios/MudancaCarteira.cshtml", model);
        }

        if (string.IsNullOrWhiteSpace(model.TipoVendedor))
        {
            ModelState.AddModelError(nameof(model.TipoVendedor), "Escolha qual vendedor deseja atualizar.");
            return View("~/Views/Relatorios/MudancaCarteira.cshtml", model);
        }

        var tipo = model.TipoVendedor.ToLowerInvariant();
        var atualizarPrincipal = tipo == "principal";

        using var stream = model.Arquivo.OpenReadStream();
        var resultado = await _mudancaCarteira.ProcessarAsync(stream, atualizarPrincipal);

        model.TotalLinhas = resultado.Total;
        model.Atualizados = resultado.Atualizados;
        model.Erros = resultado.Erros;
        model.Processado = true;

        return View("~/Views/Relatorios/MudancaCarteira.cshtml", model);
    }

    // -------- Rupturas --------
    [HttpGet]
    [RequireReport("RUPTURAS_HISTORICO")]
    public async Task<IActionResult> HistoricoRupturas(string? item, bool load = false)
    {
        if (load && Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            var dados = await _relatorioRupturas.GerarAsync(item);
            return PartialView("~/Views/Relatorios/_GridHistoricoRupturas.cshtml", dados);
        }

        var empty = new DataTablePage
        {
            Data = new DataTable(),
            Total = 0,
            Page = 1,
            PageSize = 20
        };

        return View("~/Views/Relatorios/HistoricoRupturas.cshtml", empty);
    }

    [HttpGet]
    [RequireReport("RUPTURAS_PREVISAO")]
    public async Task<IActionResult> RupturaPrevisao(string? item, bool load = false)
    {
        if (load && Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            var dados = await _rupturaPrevisao.GerarAsync(item);
            return PartialView("~/Views/Relatorios/_GridRupturaPrevisao.cshtml", dados);
        }

        var empty = new DataTablePage
        {
            Data = new DataTable(),
            Total = 0,
            Page = 1,
            PageSize = 50
        };

        return View("~/Views/Relatorios/RupturaPrevisao.cshtml", empty);
    }

    [HttpGet]
    [RequireReport("STATUS_INDICADOR")]
    public async Task<IActionResult> StatusIndicador(DateTime? de)
    {
        var linhas = await _status.BuscarAsync(de);

        var modelo = linhas
            .GroupBy(x => x.Pedido)
            .Select(g =>
            {
                var first = g.OrderBy(l => l.LogId).First();
                return new StatusIndicadorPedidoVM
                {
                    Pedido = g.Key,
                    CardCode = first.CardCode,
                    CardName = first.CardName,
                    Criacao = first.Criacao,
                    Logs = g.OrderBy(l => l.LogId).ToList()
                };
            })
            .OrderBy(p => p.Pedido)
            .ToList();

        return View(modelo);
    }

    [HttpGet]
    [RequireReport("RUPTURAS_PREVISAO")]
    public async Task<IActionResult> PrevisaoRuptura(
        string? item,
        string? risco,
        int? diasMax,
        int page = 1,
        int pageSize = 20)
    {
        try
        {
            if (page < 1) page = 1;
            if (pageSize <= 0) pageSize = 20;

            var vm = await _prevRuptura.BuscarAsync(item, null, risco, diasMax);

            vm.Total = vm.Linhas.Count;
            vm.Page = page;
            vm.PageSize = pageSize;

            vm.Linhas = vm.Linhas
                .OrderBy(x => x.ItemCode)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var resumoRuptura = await _prevRuptura.ObterResumoAsync();
            ViewBag.RupturaResumo = resumoRuptura;

            return View(vm);
        }
        catch (Exception ex)
        {
            return Content(ex.ToString());
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireReport("RUPTURAS_PREVISAO")]
    public async Task<IActionResult> AtualizarPrevisaoRuptura()
    {
        var iniciado = await _rupturaPrevisaoJobRunner.TryStartAsync("manual", HttpContext.RequestAborted);

        if (!iniciado)
            return Conflict(_rupturaPrevisaoJobRunner.GetStatus());

        return Json(_rupturaPrevisaoJobRunner.GetStatus());
    }

    [HttpGet]
    [RequireReport("RUPTURAS_PREVISAO")]
    public IActionResult StatusAtualizacaoPrevisaoRuptura()
    {
        return Json(_rupturaPrevisaoJobRunner.GetStatus());
    }

    // -------- Helpers --------
    private static string DataTableToCsv(DataTable dt)
    {
        var sb = new StringBuilder();

        for (int i = 0; i < dt.Columns.Count; i++)
        {
            if (i > 0) sb.Append(';');
            sb.Append(Escape(dt.Columns[i].ColumnName));
        }
        sb.AppendLine();

        foreach (DataRow row in dt.Rows)
        {
            for (int i = 0; i < dt.Columns.Count; i++)
            {
                if (i > 0) sb.Append(';');
                var val = row[i] is DBNull ? "" : row[i]?.ToString() ?? "";
                sb.Append(Escape(val));
            }
            sb.AppendLine();
        }

        return sb.ToString();

        static string Escape(string s)
        {
            s = s.Replace("\"", "\"\"");
            s = s.Replace("\r", " ").Replace("\n", " ");
            return $"\"{s}\"";
        }
    }

    // -------- Ficha técnica --------
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireReport("DADOS_PRODUTOS")]
    public async Task<IActionResult> FichaTecnicaPdf([FromForm] string codes)
    {
        try
        {
            List<string>? itens;
            try { itens = JsonSerializer.Deserialize<List<string>>(codes); }
            catch { return BadRequest("Lista inválida."); }
            if (itens is null || itens.Count == 0) return BadRequest("Nenhum item selecionado.");

            var rows = await _relProdutos.BuscarDadosFichaTecnica_FromFileAsync(itens, HttpContext.RequestAborted);
            if (rows.Rows.Count == 0) return NotFound("Itens não encontrados.");

            using var ms = new MemoryStream();
            using (var writer = new PdfWriter(ms))
            using (var pdf = new PdfDoc(writer))
            {
                var logoPath = IOPath.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "logo.png");
                pdf.AddEventHandler(PdfDocumentEvent.START_PAGE, new BrwHeaderHandler(logoPath));
                pdf.AddEventHandler(PdfDocumentEvent.END_PAGE, new BrwFooterHandler());

                using var doc = new Doc(pdf, PageSize.A4);
                doc.SetMargins(90, 36, 60, 36);

                bool first = true;
                foreach (DataRow r in rows.Rows)
                {
                    if (!first) doc.Add(new AB(ABT.NEXT_PAGE));
                    first = false;

                    var itemCode = Str(r, "ItemCode");
                    var itemName = Str(r, "ItemName");

                    doc.Add(new Para(itemCode ?? "").SetFontSize(20).SetBold()
                        .SetTextAlignment(TextAlignment.CENTER));
                    doc.Add(new Para(itemName ?? "").SetFontSize(11).SetFontColor(new RGB(90, 90, 90))
                        .SetTextAlignment(TextAlignment.CENTER));
                    doc.Add(new Para(" "));

                    var allUrlsRaw = Str(r, "URL_IMAGEM") ?? "";
                    var urls = allUrlsRaw
                        .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(u => u.Trim())
                        .Where(u => !string.IsNullOrWhiteSpace(u) && Uri.IsWellFormedUriString(u, UriKind.Absolute))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    Img? hero = null;
                    string? heroUsed = null;
                    foreach (var u in urls)
                    {
                        var tmp = await TryLoadImage(u);
                        if (tmp != null) { hero = tmp; heroUsed = u; break; }
                    }

                    if (hero != null)
                    {
                        hero.ScaleToFit(220, 220).SetAutoScale(false)
                            .SetHorizontalAlignment(HorizontalAlignment.CENTER);
                        doc.Add(hero);
                        doc.Add(new Para(" "));
                    }

                    var kv = new List<(string Label, string? Val)>
                    {
                        ("Descrição", itemName),
                        ("Origem",    Str(r,"Name")),
                        ("NCM",       Str(r,"NcmCode")),
                        ("Uni. Venda", Str(r,"SalUnitMsr")),
                        ("Reg. Inmetro", Str(r,"INMETRO")),

                        ("Cód. barras (produto)", Str(r,"U_ProdCodBarras")),
                        ("Dimensões do produto (C×L×A)", DimCm(r["U_ProdComprimento"], r["U_ProdLargura"], r["U_ProdAltura"])),

                        ("Cód. barras (Embalagem)", Str(r,"U_EmbCodBarras")),
                        ("Dim. Embalagem (C×L×A)",   DimCm(r["U_EmbComprimento"], r["U_EmbLargura"], r["U_EmbAltura"])),
                        ("Peso bruto (Embalagem)",   Kg(r["U_EmbPeso"])),
                        ("Peso líquido (Embalagem)", Kg(r["U_EmbPesoLiq"])),

                        ("Cód. Barras (Inner)", Str(r,"U_InnerCodBarras")),
                        ("Quantidade (Inner)",        Str(r,"U_QdeInner")),
                        ("Dimensões (Inner)(C×L×A)", DimCm(r["U_InnerComprimento"], r["U_InnerLargura"], r["U_InnerAltura"])),
                        ("Peso Bruto (Inner)",        Kg(r["U_InnerPeso"])),
                        ("Peso Líquido (Inner)",      Kg(r["U_InnerPesoLiq"])),

                        ("Cód. Barras (Master)", Str(r,"U_MasterCodBarras")),
                        ("Quantidade (Master)",       Str(r,"U_QdeMaster")),
                        ("Dimensões (Master)(C×L×A)",DimCm(r["U_MasterComprimento"], r["U_MasterLargura"], r["U_MasterAltura"])),
                        ("Peso Bruto (Master)",       Kg(r["U_MasterPeso"])),
                        ("Peso Líquido (Master)",     Kg(r["U_MasterPesoLiq"])),

                        ("Composição", Str(r, "InfoTecnicas")),
                        ("Característica 1",     Str(r, "Caracteristica_1")),
                        ("Característica 2",     Str(r, "Caracteristica_2")),
                        ("Característica 3",     Str(r, "Caracteristica_3")),
                        ("Característica 4",     Str(r, "Caracteristica_4")),
                    };

                    var t = new Tbl(new float[] { 180, 360 }).UseAllAvailableWidth().SetFontSize(FONT_BASE);
                    foreach (var (label, val) in kv)
                    {
                        t.AddCell(KeyCell(label));
                        t.AddCell(ValCell(val));
                    }

                    doc.Add(t);

                    var descAdd = Str(r, "Descricao_Adicional");
                    if (!string.IsNullOrWhiteSpace(descAdd))
                    {
                        doc.Add(new Para(" "));

                        var box = new Tbl(new float[] { 1f }).UseAllAvailableWidth();

                        var title = new Para("INFORMAÇÕES ADICIONAIS")
                            .SetBold()
                            .SetFontSize(11)
                            .SetTextAlignment(TextAlignment.CENTER)
                            .SetMarginBottom(6);

                        var text = new Para(descAdd.Trim())
                            .SetFontSize(10)
                            .SetTextAlignment(TextAlignment.JUSTIFIED)
                            .SetMultipliedLeading(1.3f);

                        var cell = new CellEl().Add(title).Add(text)
                            .SetBackgroundColor(new RGB(235, 235, 235))
                            .SetPadding(12);

                        box.AddCell(cell);
                        doc.Add(box);
                    }

                    var rest = urls.Where(u => !string.Equals(u, heroUsed, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (rest.Count > 0)
                    {
                        doc.Add(new AB(ABT.NEXT_PAGE));
                        doc.Add(new Para("Imagens do produto").SetFontSize(14).SetBold());

                        var grid = new Tbl(new float[] { 1, 1 }).UseAllAvailableWidth();
                        foreach (var url in rest)
                        {
                            var img = await TryLoadImage(url);
                            if (img == null)
                            {
                                grid.AddCell(new CellEl().Add(new Para("Imagem não disponível")).SetHeight(220));
                                continue;
                            }
                            img.SetAutoScale(true);
                            grid.AddCell(new CellEl().Add(img).SetVerticalAlignment(VerticalAlignment.MIDDLE));
                        }
                        if (rest.Count % 2 != 0) grid.AddCell(new CellEl());
                        doc.Add(grid);
                    }
                }

                doc.Flush();
            }

            return File(ms.ToArray(), "application/pdf", $"FichasTecnicas_{DateTime.Now:yyyyMMdd_HHmm}.pdf");
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(
                @"C:\temp\FichaTecnica_Erros.txt",
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - ERRO: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}"
            );

            return Content("Erro ao gerar ficha técnica. Verifique o log no servidor.");
        }
    }

    [HttpGet]
    [RequireReport("RUPTURAS_PREVISAO")]
    public async Task<IActionResult> ExportCsv_RupturaPrevisao(string? item = null)
    {
        var dados = await _rupturaPrevisao.GerarAsync(item);

        if (dados == null || dados.Data == null || dados.Data.Rows.Count == 0)
        {
            var vazio = "Sem dados para exportar.";
            return File(Encoding.UTF8.GetBytes(vazio),
                        "text/plain; charset=utf-8",
                        $"RupturaPrevisao_vazio_{DateTime.Now:yyyyMMdd_HHmm}.txt");
        }

        var csv = DataTableToCsv(dados.Data);
        var bytes = Encoding.UTF8.GetBytes(csv);

        var filtro = string.IsNullOrWhiteSpace(item) ? "todos" : item.Trim();
        var fileName = $"RupturaPrevisao_{filtro}_{DateTime.Now:yyyyMMdd_HHmm}.csv";

        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    [HttpGet]
    [RequireReport("COLABORADORES")]
    public async Task<IActionResult> CadColaboradores(string? q, int page = 1, int pageSize = 20)
    {
        var model = await _cadColaboradorService.ListarAsync(q, page, pageSize);
        return View("~/Views/Relatorios/CadColaboradores.cshtml", model);
    }

    [HttpGet]
    [RequireReport("COLABORADORES")]
    public async Task<IActionResult> CadColaboradoresCsv(string? q)
    {
        var csv = await _cadColaboradorService.GerarCsvAsync(q);

        var fileName = $"cad-colaboradores-{DateTime.Now:yyyyMMdd-HHmmss}.csv";

        var encoding = Encoding.GetEncoding(1252);
        var bytes = encoding.GetBytes(csv);

        return File(bytes, "text/csv; charset=Windows-1252", fileName);
    }

    [HttpGet]
    [RequireReport("RUPTURAS_PREVISAO")]
    public async Task<IActionResult> AnaliseRuptura(string item)
    {
        if (string.IsNullOrWhiteSpace(item))
            return BadRequest(new { message = "Item não informado." });

        var vm = await _prevRuptura.BuscarAnalisePorItemAsync(item);
        if (vm == null)
            return NotFound(new { message = "Item não encontrado na previsão de ruptura." });

        return Json(new
        {
            itemCode = vm.ItemCode,
            itemName = vm.ItemName,
            estoqueAtual = vm.EstoqueAtual,
            emTransito = vm.EmTransito,
            comprometido = vm.Comprometido,
            estoqueProjetado = vm.EstoqueProjetado,
            consumoHistorico = vm.ConsumoHistoricoDia,
            consumoIa = vm.ConsumoIaDia,
            consumoUsado = vm.ConsumoMedioDia,
            leadTime = vm.LeadTimeDias,
            diasRuptura = vm.DiasAteRuptura,
            dataRuptura = vm.DataRuptura?.ToString("dd/MM/yyyy"),
            demandaLeadTime = vm.DemandaLeadTime,
            nivelRisco = vm.NivelRisco,
            motivoRisco = vm.MotivoRisco,
            explicacaoIa = vm.ExplicacaoIa
        });
    }

    [HttpGet]
    public async Task<IActionResult> ExportarDashboardLiberacaoPedidosExcel(
    DateTime de,
    DateTime ate,
    string? tipoData,
    string? tipos,
    string? status)
    {
        var vm = await _dashService.BuscarAsync(
            de: de,
            ate: ate,
            tipos: tipos,
            tipoDataBase: tipoData ?? "Pedido"
        );

        var statusNorm = (status ?? "").Trim();

        if (!string.IsNullOrWhiteSpace(statusNorm))
        {
            if (statusNorm.Equals("Devolvido", StringComparison.OrdinalIgnoreCase))
            {
                vm.Pedidos = vm.Pedidos
                    .Where(p => string.Equals(p.StatusDocumento, "Devolvido", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            else
            {
                vm.Pedidos = vm.Pedidos
                    .Where(p =>
                    {
                        var teveCom = p.TeveBloqueioComercial;
                        var teveFin = p.TeveBloqueioFinanceiro;
                        var temBloqueio = teveCom || teveFin;

                        string labelStatus;
                        if (p.StatusFinal == "LIBERADO" && !temBloqueio) labelStatus = "Liberado sem bloq";
                        else if (p.StatusFinal == "LIBERADO" && temBloqueio && p.DentroDoSla) labelStatus = "Liberado dentro do SLA";
                        else if (p.StatusFinal == "LIBERADO" && temBloqueio && p.Atrasado) labelStatus = "Liberado com atraso";
                        else if (p.StatusFinal == "RECUSADO") labelStatus = "Recusado";
                        else if (p.StatusFinal == "PENDENTE" && temBloqueio) labelStatus = "Pendente";
                        else if (p.StatusFinal == "PENDENTE" && !temBloqueio) labelStatus = "Sem bloqueio";
                        else labelStatus = "Desconhecido";

                        return labelStatus.Equals(statusNorm, StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();
            }
        }

        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("Pedidos");

        var headers = new[]
        {
        "Pedido","Cliente","Cód. Cliente","Data (Pedido)","Data (Entrega)","Data (Faturamento)","Data Devolução",
        "Valor Pedido","Valor Devolvido","NF (Serial)","Obs. devolução","Bloqueio","Situação","Status"
    };

        for (int i = 0; i < headers.Length; i++)
            ws.Cells[1, i + 1].Value = headers[i];

        using (var rng = ws.Cells[1, 1, 1, headers.Length])
        {
            rng.Style.Font.Bold = true;
            rng.Style.Fill.PatternType = ExcelFillStyle.Solid;
            rng.Style.Fill.BackgroundColor.SetColor(DxColor.FromArgb(240, 240, 240));
            rng.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        }

        int row = 2;
        foreach (var p in vm.Pedidos)
        {
            var teveCom = p.TeveBloqueioComercial;
            var teveFin = p.TeveBloqueioFinanceiro;

            // ✅ NOVO: montar "Bloqueio" refletindo o status que aparece na VIEW
            string StatusComercial()
            {
                if (p.DataLiberacaoComercial.HasValue) return "Aprovado";
                return string.Equals(p.PenComAtual, "Y", StringComparison.OrdinalIgnoreCase) ? "Pendente" : "Sem bloqueio";
            }

            string StatusFinanceiro()
            {
                if (p.DataLiberacaoFinanceiro.HasValue) return "Aprovado";
                return string.Equals(p.PenFinAtual, "Y", StringComparison.OrdinalIgnoreCase) ? "Pendente" : "Sem bloqueio";
            }

            string bloqueio;
            if (!teveCom && !teveFin)
            {
                bloqueio = "Sem bloqueio";
            }
            else
            {
                var partes = new List<string>();
                if (teveCom) partes.Add($"Comercial - {StatusComercial()}");
                if (teveFin) partes.Add($"Financeiro - {StatusFinanceiro()}");
                bloqueio = string.Join(" | ", partes);
            }

            var temBloqueio = teveCom || teveFin;
            string labelStatus;
            if (p.StatusFinal == "LIBERADO" && !temBloqueio) labelStatus = "Liberado sem bloq";
            else if (p.StatusFinal == "LIBERADO" && temBloqueio && p.DentroDoSla) labelStatus = "Liberado dentro do SLA";
            else if (p.StatusFinal == "LIBERADO" && temBloqueio && p.Atrasado) labelStatus = "Liberado com atraso";
            else if (p.StatusFinal == "RECUSADO") labelStatus = "Recusado";
            else if (p.StatusFinal == "PENDENTE" && temBloqueio) labelStatus = "Pendente";
            else if (p.StatusFinal == "PENDENTE" && !temBloqueio) labelStatus = "Sem bloqueio";
            else labelStatus = "Desconhecido";

            ws.Cells[row, 1].Value = p.DocNum;
            ws.Cells[row, 2].Value = p.CardName;
            ws.Cells[row, 3].Value = p.CardCode;

            ws.Cells[row, 4].Value = p.DocDate;
            ws.Cells[row, 5].Value = p.DataEntrega;
            ws.Cells[row, 6].Value = p.DataFaturamento;

            ws.Cells[row, 7].Value = p.DataDevolucao;
            ws.Cells[row, 8].Value = (double)p.DocTotal;
            ws.Cells[row, 9].Value = p.ValorDevolvido.HasValue ? (double)p.ValorDevolvido.Value : 0d;

            var isDevolvido = string.Equals(p.StatusDocumento, "Devolvido", StringComparison.OrdinalIgnoreCase);

            ws.Cells[row, 10].Value = isDevolvido ? (string.IsNullOrWhiteSpace(p.SerialNF) ? "-" : p.SerialNF) : "-";
            ws.Cells[row, 11].Value = isDevolvido ? (string.IsNullOrWhiteSpace(p.ComentariosDevolucao) ? "-" : p.ComentariosDevolucao) : "-";

            ws.Cells[row, 12].Value = bloqueio;
            ws.Cells[row, 13].Value = p.StatusDocumento ?? "";
            ws.Cells[row, 14].Value = labelStatus;

            row++;
        }

        ws.Column(4).Style.Numberformat.Format = "dd/mm/yyyy";
        ws.Column(5).Style.Numberformat.Format = "dd/mm/yyyy";
        ws.Column(6).Style.Numberformat.Format = "dd/mm/yyyy";
        ws.Column(7).Style.Numberformat.Format = "dd/mm/yyyy";
        ws.Column(8).Style.Numberformat.Format = "R$ #,##0.00";
        ws.Column(9).Style.Numberformat.Format = "R$ #,##0.00";
        ws.Column(10).Style.Numberformat.Format = "@";

        ws.Cells[ws.Dimension.Address].AutoFitColumns();
        ws.View.FreezePanes(2, 1);

        pkg.Workbook.Calculate();
        var bytes = pkg.GetAsByteArray();
        var nomeArquivo = $"Dash_LiberacaoPedidos_{de:yyyyMMdd}_{ate:yyyyMMdd}.xlsx";

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nomeArquivo);
    }

    [HttpGet]
    [RequireReport("MUDANCA_CARTEIRA")]
    public IActionResult DownloadTemplateInativar()
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("Inativar");

        ws.Cells[1, 1].Value = "CardCode";

        using (var rng = ws.Cells[1, 1, 1, 1])
        {
            rng.Style.Font.Bold = true;
            rng.Style.Fill.PatternType = ExcelFillStyle.Solid;
            rng.Style.Fill.BackgroundColor.SetColor(DxColor.FromArgb(240, 240, 240));
            rng.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        }

        ws.Column(1).Width = 25;
        ws.View.FreezePanes(2, 1);

        pkg.Workbook.Calculate();
        var bytes = pkg.GetAsByteArray();
        var nomeArquivo = $"Template_Inativacao_Parceiro_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            nomeArquivo);
    }

    [HttpGet]
    [RequireReport("MUDANCA_CARTEIRA")]
    public IActionResult DownloadTemplateCarteira()
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("Carteira");

        ws.Cells[1, 1].Value = "CardCode";
        ws.Cells[1, 2].Value = "SlpCode";

        using (var rng = ws.Cells[1, 1, 1, 2])
        {
            rng.Style.Font.Bold = true;
            rng.Style.Fill.PatternType = ExcelFillStyle.Solid;
            rng.Style.Fill.BackgroundColor.SetColor(DxColor.FromArgb(240, 240, 240));
            rng.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        }

        ws.Column(1).Width = 25;
        ws.Column(2).Width = 12;
        ws.View.FreezePanes(2, 1);

        pkg.Workbook.Calculate();
        var bytes = pkg.GetAsByteArray();
        var nomeArquivo = $"Template_Mudanca_Carteira_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            nomeArquivo);
    }

    // ==========================================
    // RELATÓRIO TEMPO -> EXCEL (profissional) ✅
    // (Gráficos OK usando aba _Dados oculta)
    // ==========================================
    [HttpGet]
    [RequireReport("RELATORIO_TEMPO")]
    public async Task<IActionResult> TempoExcel(DateTime? de, DateTime? ate)
    {
        var itens = await _relatorioTempo.GerarAsync(de, ate);

        var total = itens.Count;
        var dentro = itens.Count(x => x.SlaDentroPrazo);
        var fora = total - dentro;
        var pctDentro = total == 0 ? 0 : (double)dentro / total;

        var temposMin = itens.Select(x => x.TempoUtil.TotalMinutes).OrderBy(x => x).ToList();
        double avgMin = temposMin.Count == 0 ? 0 : temposMin.Average();
        double p95Min = temposMin.Count == 0 ? 0 : temposMin[(int)Math.Floor(0.95 * (temposMin.Count - 1))];

        var foraPorTecnico = itens
            .Where(x => !x.SlaDentroPrazo)
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Tecnico) ? "Sem técnico" : x.Tecnico!)
            .Select(g => new { Tecnico = g.Key, Qtde = g.Count() })
            .OrderByDescending(x => x.Qtde)
            .Take(10)
            .ToList();

        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        using var pkg = new ExcelPackage();
        pkg.Workbook.Properties.Title = "Relatório de Tempo de Atendimento - Chamados";
        pkg.Workbook.CalcMode = ExcelCalcMode.Automatic;
        pkg.Workbook.FullCalcOnLoad = true;

        // ==========================================================
        // ABA OCULTA: _Dados (fonte dos gráficos) ✅
        // ==========================================================
        var wsData = pkg.Workbook.Worksheets.Add("_Dados");
        wsData.View.ShowGridLines = false;

        // SLA (Pizza)
        wsData.Cells["A1"].Value = "Categoria";
        wsData.Cells["B1"].Value = "Qtde";
        wsData.Cells["A2"].Value = "Dentro do SLA";
        wsData.Cells["B2"].Value = dentro;
        wsData.Cells["A3"].Value = "Fora do SLA";
        wsData.Cells["B3"].Value = fora;

        // Top 10 fora SLA (Barras)
        wsData.Cells["D1"].Value = "Técnico";
        wsData.Cells["E1"].Value = "Fora SLA";

        int dr = 2;
        if (foraPorTecnico.Count == 0)
        {
            wsData.Cells[dr, 4].Value = "Sem dados";
            wsData.Cells[dr, 5].Value = 0;
            dr++;
        }
        else
        {
            foreach (var x in foraPorTecnico)
            {
                wsData.Cells[dr, 4].Value = x.Tecnico;
                wsData.Cells[dr, 5].Value = x.Qtde;
                dr++;
            }
        }

        // Oculta a aba (Excel continua permitindo gráfico com dados de aba oculta)
        wsData.Hidden = eWorkSheetHidden.Hidden;

        // ==========================================================
        // ABA: Resumo
        // ==========================================================
        var ws = pkg.Workbook.Worksheets.Add("Resumo");
        ws.View.ShowGridLines = false;

        // Cabeçalho
        ws.Cells["A1:H1"].Merge = true;
        ws.Cells["A1"].Value = "Relatório de Tempo de Atendimento - Chamados";
        ws.Cells["A1"].Style.Font.Bold = true;
        ws.Cells["A1"].Style.Font.Size = 16;
        ws.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;

        ws.Cells["A2:H2"].Merge = true;
        ws.Cells["A2"].Value = $"Período: {(de?.ToString("dd/MM/yyyy") ?? "Início")} até {(ate?.ToString("dd/MM/yyyy") ?? "Hoje")}";
        ws.Cells["A2"].Style.Font.Color.SetColor(DxColor.FromArgb(90, 90, 90));
        ws.Cells["A2"].Style.Font.Size = 10;

        // KPI Cards (4)
        void Kpi(string addrTitle, string addrValue, string title, string value, DxColor bg)
        {
            ws.Cells[addrTitle].Value = title;
            ws.Cells[addrValue].Value = value;

            ws.Cells[$"{addrTitle}:{addrValue}"].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[$"{addrTitle}:{addrValue}"].Style.Fill.BackgroundColor.SetColor(bg);
            ws.Cells[$"{addrTitle}:{addrValue}"].Style.Border.BorderAround(ExcelBorderStyle.Thin, DxColor.FromArgb(230, 230, 230));

            ws.Cells[addrTitle].Style.Font.Bold = true;
            ws.Cells[addrTitle].Style.Font.Color.SetColor(DxColor.White);
            ws.Cells[addrTitle].Style.Font.Size = 10;

            ws.Cells[addrValue].Style.Font.Bold = true;
            ws.Cells[addrValue].Style.Font.Color.SetColor(DxColor.White);
            ws.Cells[addrValue].Style.Font.Size = 18;

            ws.Cells[addrTitle].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            ws.Cells[addrValue].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
        }

        ws.Row(4).Height = 18;
        ws.Row(5).Height = 28;

        Kpi("A4", "A5", "TOTAL", total.ToString(), DxColor.FromArgb(13, 110, 253));
        Kpi("C4", "C5", "DENTRO SLA", dentro.ToString(), DxColor.FromArgb(25, 135, 84));
        Kpi("E4", "E5", "FORA SLA", fora.ToString(), DxColor.FromArgb(220, 53, 69));
        Kpi("G4", "G5", "% SLA", (pctDentro).ToString("P2"), DxColor.FromArgb(108, 117, 125));

        // KPIs secundários
        ws.Cells["A7"].Value = "Tempo médio (min)";
        ws.Cells["B7"].Value = avgMin;
        ws.Cells["A8"].Value = "P95 (min)";
        ws.Cells["B8"].Value = p95Min;

        ws.Cells["A7:A8"].Style.Font.Bold = true;
        ws.Cells["B7:B8"].Style.Numberformat.Format = "0.00";
        ws.Cells["A7:B8"].Style.Border.BorderAround(ExcelBorderStyle.Thin, DxColor.FromArgb(230, 230, 230));

        ws.Column(1).Width = 26;
        ws.Column(2).Width = 16;

        // ==========================================================
        // GRÁFICOS (referência explícita com nome da planilha)
        // ==========================================================

        // Pizza SLA
        var pie = ws.Drawings.AddChart("chtSla", eChartType.Pie) as ExcelPieChart;
        pie.Title.Text = "SLA - Dentro x Fora";
        pie.SetPosition(10, 0, 4, 0);
        pie.SetSize(520, 320);
        pie.DataLabel.ShowPercent = true;
        pie.DataLabel.ShowCategory = true;

        // Referência EXPLÍCITA com nome da aba
        var seriePie = pie.Series.Add(
            wsData.Cells["B2:B3"],
            wsData.Cells["A2:A3"]
        );
        seriePie.Header = "SLA";

        // Barras Top 10
        int lastRow = Math.Max(2, dr - 1);

        var bar = ws.Drawings.AddChart("chtTop10", eChartType.ColumnClustered) as ExcelBarChart;
        bar.Title.Text = "Top 10 - Fora do SLA por Técnico";
        bar.SetPosition(10, 0, 0, 0);
        bar.SetSize(520, 320);
        bar.YAxis.MinValue = 0;

        // Referência EXPLÍCITA
        var serieBar = bar.Series.Add(
            wsData.Cells[$"E2:E{lastRow}"],
            wsData.Cells[$"D2:D{lastRow}"]
        );
        serieBar.Header = "Chamados fora do SLA";


        // ==========================================================
        // ABA: Chamados (Detalhe)
        // ==========================================================
        var wd = pkg.Workbook.Worksheets.Add("Chamados");
        wd.View.FreezePanes(2, 1);

        var headers = new[]
        {
        "#","Chamado","Usuário","Técnico","Aberto","Fechado","Total","Parado","Atend.","SLA %","SLA OK","Resumo"
    };

        for (int i = 0; i < headers.Length; i++)
        {
            wd.Cells[1, i + 1].Value = headers[i];
            wd.Cells[1, i + 1].Style.Font.Bold = true;
            wd.Cells[1, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            wd.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(DxColor.FromArgb(240, 240, 240));
            wd.Cells[1, i + 1].Style.Border.BorderAround(ExcelBorderStyle.Thin, DxColor.FromArgb(220, 220, 220));
        }

        int row = 2;
        foreach (var it in itens)
        {
            wd.Cells[row, 1].Value = it.ChamadoId;
            wd.Cells[row, 2].Value = it.Titulo;
            wd.Cells[row, 3].Value = it.Solicitante;
            wd.Cells[row, 4].Value = it.Tecnico;
            wd.Cells[row, 5].Value = it.DataAbertura?.ToString("dd/MM/yyyy HH:mm");
            wd.Cells[row, 6].Value = it.DataConclusao?.ToString("dd/MM/yyyy HH:mm");
            wd.Cells[row, 7].Value = it.TempoBrutoFmt;
            wd.Cells[row, 8].Value = it.TempoPausadoFmt;
            wd.Cells[row, 9].Value = it.TempoUtilFmt;
            wd.Cells[row, 10].Value = it.SlaPercent;
            wd.Cells[row, 11].Value = it.SlaDentroPrazo ? "Sim" : "Não";
            wd.Cells[row, 12].Value = it.SlaResumo;

            // zebra
            if (row % 2 == 0)
            {
                wd.Cells[row, 1, row, 12].Style.Fill.PatternType = ExcelFillStyle.Solid;
                wd.Cells[row, 1, row, 12].Style.Fill.BackgroundColor.SetColor(DxColor.FromArgb(250, 250, 250));
            }

            row++;
        }

        wd.Cells[wd.Dimension.Address].AutoFitColumns();
        wd.Column(2).Width = 45;

        // Evita arquivo abrindo “sem recalcular”
        pkg.Workbook.CalcMode = ExcelCalcMode.Automatic;
        pkg.Workbook.FullCalcOnLoad = true;

        pkg.Workbook.Calculate();
        var bytes = pkg.GetAsByteArray();
        var nomeArquivo = $"RelatorioTempo_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            nomeArquivo);
    }



    // ==========================================
    // RELATÓRIO TEMPO -> PDF (profissional)
    // ==========================================
    [HttpGet]
    [RequireReport("RELATORIO_TEMPO")]
    public async Task<IActionResult> TempoPdf(DateTime? de, DateTime? ate)
    {
        var itens = await _relatorioTempo.GerarAsync(de, ate);

        var total = itens.Count;
        var dentro = itens.Count(x => x.SlaDentroPrazo);
        var fora = total - dentro;
        var pctDentro = total == 0 ? 0 : (double)dentro / total;

        var temposMin = itens.Select(x => x.TempoUtil.TotalMinutes).OrderBy(x => x).ToList();
        double avgMin = temposMin.Count == 0 ? 0 : temposMin.Average();
        double p95Min = temposMin.Count == 0 ? 0 : temposMin[(int)Math.Floor(0.95 * (temposMin.Count - 1))];

        var foraPorTecnico = itens
            .Where(x => !x.SlaDentroPrazo)
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Tecnico) ? "Sem técnico" : x.Tecnico!)
            .Select(g => new { Tecnico = g.Key, Qtde = g.Count() })
            .OrderByDescending(x => x.Qtde)
            .Take(10)
            .ToList();

        using var ms = new MemoryStream();
        using var writer = new PdfWriter(ms);
        using var pdf = new PdfDocument(writer);
        using var doc = new Document(pdf, PageSize.A4);

        doc.SetMargins(36, 28, 36, 28);

        var blue = new DeviceRgb(13, 110, 253);
        var green = new DeviceRgb(25, 135, 84);
        var red = new DeviceRgb(220, 53, 69);
        var gray = new DeviceRgb(108, 117, 125);
        var light = new DeviceRgb(245, 246, 248);

        var title = new Paragraph("Relatório de Tempo de Atendimento - Chamados")
            .SetFontSize(16).SetBold().SetMarginBottom(2);

        var sub = new Paragraph($"Período: {(de?.ToString("dd/MM/yyyy") ?? "Início")} até {(ate?.ToString("dd/MM/yyyy") ?? "Hoje")}")
            .SetFontSize(10).SetFontColor(gray)
            .SetMarginBottom(14);

        doc.Add(title);
        doc.Add(sub);

        Table cards = new Table(4).UseAllAvailableWidth().SetMarginBottom(14);

        Cell Card(string t, string v, DeviceRgb bg)
        {
            var c = new Cell()
                .SetBorder(ITextBorder.NO_BORDER)
                .SetBackgroundColor(bg)
                .SetPadding(10);

            c.Add(new Paragraph(t).SetFontSize(9).SetFontColor(ColorConstants.WHITE).SetBold().SetMarginBottom(2));
            c.Add(new Paragraph(v).SetFontSize(16).SetFontColor(ColorConstants.WHITE).SetBold().SetMarginBottom(0));
            return c;
        }

        cards.AddCell(Card("TOTAL", total.ToString(), blue));
        cards.AddCell(Card("DENTRO SLA", dentro.ToString(), green));
        cards.AddCell(Card("FORA SLA", fora.ToString(), red));
        cards.AddCell(Card("% SLA", pctDentro.ToString("P2"), gray));
        doc.Add(cards);

        Table kpis2 = new Table(new float[] { 1, 1 }).UseAllAvailableWidth().SetMarginBottom(14);

        Cell K2(string k, string v)
        {
            return new Cell()
                .SetBorder(ITextBorder.NO_BORDER)
                .SetBackgroundColor(light)
                .SetPadding(10)
                .Add(new Paragraph(k).SetFontSize(9).SetFontColor(gray).SetBold())
                .Add(new Paragraph(v).SetFontSize(12).SetBold().SetMarginTop(2));
        }

        kpis2.AddCell(K2("Tempo médio (min)", avgMin.ToString("0.00")));
        kpis2.AddCell(K2("P95 (min)", p95Min.ToString("0.00")));
        doc.Add(kpis2);

        doc.Add(new Paragraph("Top 10 - Fora do SLA por Técnico").SetBold().SetFontSize(12).SetMarginBottom(8));

        Table top = new Table(new float[] { 3, 1 }).UseAllAvailableWidth();

        void HeaderCell(string txt)
        {
            top.AddHeaderCell(new Cell()
                .SetBackgroundColor(new DeviceRgb(240, 240, 240))
                .SetPadding(6)
                .SetBorder(new SolidBorder(new DeviceRgb(220, 220, 220), 1))
                .Add(new Paragraph(txt).SetFontSize(9).SetBold()));
        }

        HeaderCell("Técnico");
        HeaderCell("Qtde");

        if (foraPorTecnico.Count == 0)
        {
            top.AddCell(new Cell(1, 2)
                .SetPadding(10)
                .SetBorder(new SolidBorder(new DeviceRgb(220, 220, 220), 1))
                .Add(new Paragraph("Nenhum chamado fora do SLA no período.").SetFontSize(10)));
        }
        else
        {
            foreach (var (x, idx) in foraPorTecnico.Select((v, i) => (v, i)))
            {
                var bg = idx % 2 == 0 ? ColorConstants.WHITE : new DeviceRgb(250, 250, 250);

                top.AddCell(new Cell().SetBackgroundColor(bg).SetPadding(6)
                    .SetBorder(new SolidBorder(new DeviceRgb(230, 230, 230), 1))
                    .Add(new Paragraph(x.Tecnico).SetFontSize(9)));

                top.AddCell(new Cell().SetBackgroundColor(bg).SetPadding(6)
                    .SetBorder(new SolidBorder(new DeviceRgb(230, 230, 230), 1))
                    .SetTextAlignment(TextAlignment.CENTER)
                    .Add(new Paragraph(x.Qtde.ToString()).SetFontSize(9).SetBold()));
            }
        }

        doc.Add(top);

        doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));

        doc.Add(new Paragraph("Lista de Chamados (Detalhado)")
            .SetBold().SetFontSize(12).SetMarginBottom(8));

        Table t = new Table(new float[] { 1.2f, 4.2f, 2.4f, 2.2f, 2.3f, 2.3f, 1.8f, 1.6f })
            .UseAllAvailableWidth();

        string[] h = { "#", "Chamado", "Usuário", "Técnico", "Aberto", "Fechado", "Atend.", "SLA" };
        foreach (var hh in h)
        {
            t.AddHeaderCell(new Cell()
                .SetBackgroundColor(new DeviceRgb(240, 240, 240))
                .SetPadding(6)
                .SetBorder(new SolidBorder(new DeviceRgb(220, 220, 220), 1))
                .Add(new Paragraph(hh).SetFontSize(9).SetBold()));
        }

        foreach (var (it, idx) in itens.Select((v, ix) => (v, ix)))
        {
            var bg = idx % 2 == 0 ? ColorConstants.WHITE : new DeviceRgb(250, 250, 250);

            void Add(string s, TextAlignment align = TextAlignment.LEFT, bool bold = false)
            {
                var p = new Paragraph(s ?? "").SetFontSize(8.8f);
                if (bold) p.SetBold();

                t.AddCell(new Cell().SetBackgroundColor(bg).SetPadding(5)
                    .SetBorder(new SolidBorder(new DeviceRgb(230, 230, 230), 1))
                    .SetTextAlignment(align)
                    .Add(p));
            }

            Add(it.ChamadoId.ToString(), TextAlignment.CENTER);
            Add(it.Titulo ?? "");
            Add(it.Solicitante ?? "");
            Add(it.Tecnico ?? "");
            Add(it.DataAbertura?.ToString("dd/MM/yyyy HH:mm") ?? "", TextAlignment.CENTER);
            Add(it.DataConclusao?.ToString("dd/MM/yyyy HH:mm") ?? "", TextAlignment.CENTER);
            Add(it.TempoUtilFmt ?? "", TextAlignment.CENTER);
            Add(it.SlaDentroPrazo ? "OK" : "Fora", TextAlignment.CENTER, bold: true);
        }

        doc.Add(t);
        doc.Close();

        var fileName = $"RelatorioTempo_{DateTime.Now:yyyyMMdd_HHmm}.pdf";
        return File(ms.ToArray(), "application/pdf", fileName);
    }

    // -------- Indicador TI --------
    [HttpGet]
    [RequireReport("INDICADOR_TI")] // se preferir, crie "INDICADOR_TI"
    public async Task<IActionResult> IndicadorTi(DateTime? de, DateTime? ate)
    {
        var dados = await _indicadorTi.GerarMensalAsync(de, ate);

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            return PartialView("~/Views/Relatorios/_GridIndicadorTiMensal.cshtml", dados);

        return View("~/Views/Relatorios/IndicadorTi.cshtml", dados);
    }

    [HttpGet]
    [RequireReport("INDICADOR_TI")]
    public async Task<IActionResult> IndicadorTiDetalhe(DateTime mesRef, DateTime? de, DateTime? ate)
    {
        var (consolidado, detalhes) = await _indicadorTi.GerarDetalheDoMesAsync(mesRef, de, ate);

        var vm = new PortalHelpdeskTI.Models.Relatorios.IndicadorTiDetalheVm
        {
            Consolidado = consolidado,
            Detalhes = detalhes
        };

        return PartialView("~/Views/Relatorios/_DetalheIndicadorTi.cshtml", vm);
    }

    [HttpGet]
    [RequireReport("INDICADOR_TI")]
    public async Task<IActionResult> IndicadorTiDetalheExcel(DateTime mesRef, DateTime? de, DateTime? ate)
    {
        var (consolidado, detalhes) = await _indicadorTi.GerarDetalheDoMesAsync(mesRef, de, ate);

        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("Chamados");

        var headers = new[]
        {
            "#",
            "Chamado",
            "Solicitante",
            "Técnico",
            "Categoria",
            "Subcategoria",
            "Aberto",
            "Fechado",
            "Est. (h)",
            "Real (h)",
            "Perf. %",
            "Perf. (cap110)",
            "Nota",
            "Satisf. %",
            "Comentário avaliação"
        };

        ws.Cells[1, 1].Value = $"Indicador TI - Chamados {mesRef:MM/yyyy}";
        ws.Cells[1, 1, 1, headers.Length].Merge = true;
        ws.Cells[1, 1].Style.Font.Bold = true;
        ws.Cells[1, 1].Style.Font.Size = 14;

        if (consolidado != null)
        {
            ws.Cells[2, 1].Value =
                $"Chamados: {consolidado.QtdeChamados} | Est. (h): {consolidado.HorasEstimadasTotal:N2} | Real (h): {consolidado.HorasReaisTotal:N2} | Final: {consolidado.IndicadorFinal:N2}%";
            ws.Cells[2, 1, 2, headers.Length].Merge = true;
        }

        for (var i = 0; i < headers.Length; i++)
            ws.Cells[4, i + 1].Value = headers[i];

        using (var rng = ws.Cells[4, 1, 4, headers.Length])
        {
            rng.Style.Font.Bold = true;
            rng.Style.Fill.PatternType = ExcelFillStyle.Solid;
            rng.Style.Fill.BackgroundColor.SetColor(DxColor.FromArgb(13, 110, 253));
            rng.Style.Font.Color.SetColor(DxColor.White);
            rng.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        }

        var row = 5;
        foreach (var d in detalhes)
        {
            ws.Cells[row, 1].Value = d.ChamadoId;
            ws.Cells[row, 2].Value = d.Titulo;
            ws.Cells[row, 3].Value = d.Solicitante;
            ws.Cells[row, 4].Value = d.Tecnico;
            ws.Cells[row, 5].Value = d.Categoria;
            ws.Cells[row, 6].Value = d.Subcategoria;
            ws.Cells[row, 7].Value = d.DataAbertura;
            ws.Cells[row, 8].Value = d.DataConclusao;
            ws.Cells[row, 9].Value = d.HorasEstimadas;
            ws.Cells[row, 10].Value = d.HorasReais;
            ws.Cells[row, 11].Value = d.PerformancePct;
            ws.Cells[row, 12].Value = d.PerformancePctCap110;
            ws.Cells[row, 13].Value = d.Nota_1a5;
            ws.Cells[row, 14].Value = d.SatisfacaoPct;
            ws.Cells[row, 15].Value = d.ComentarioAvaliacao;
            row++;
        }

        if (row > 5)
        {
            ws.Cells[5, 7, row - 1, 8].Style.Numberformat.Format = "dd/mm/yyyy hh:mm";
            ws.Cells[5, 9, row - 1, 14].Style.Numberformat.Format = "#,##0.00";
            ws.Cells[4, 1, row - 1, headers.Length].AutoFilter = true;
            ws.View.FreezePanes(5, 1);
        }

        if (ws.Dimension != null)
        {
            ws.Cells[ws.Dimension.Address].AutoFitColumns();
            ws.Column(2).Width = Math.Min(ws.Column(2).Width, 60);
            ws.Column(15).Width = Math.Min(ws.Column(15).Width, 70);
            ws.Column(15).Style.WrapText = true;
        }

        var nomeArquivo = $"IndicadorTI_Chamados_{mesRef:yyyyMM}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
        return File(
            pkg.GetAsByteArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            nomeArquivo);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireReport("DADOS_PRODUTOS")]
    public async Task<IActionResult> EtiquetaPdf([FromForm] string payload)
    {
        try
        {
            EtiquetaPdfPayload? req;
            try
            {
                req = JsonSerializer.Deserialize<EtiquetaPdfPayload>(payload, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                return BadRequest("Payload inválido.");
            }

            if (req == null || req.Codes == null || req.Codes.Count == 0)
                return BadRequest("Nenhum item selecionado.");

            var modelo = (req.Modelo ?? "").Trim().ToLowerInvariant();
            if (modelo != "inner" && modelo != "master")
                return BadRequest("Modelo de etiqueta inválido.");

            var rows = await _relProdutos.BuscarDadosFichaTecnica_FromFileAsync(req.Codes, HttpContext.RequestAborted);
            if (rows.Rows.Count == 0)
                return NotFound("Itens não encontrados.");

            using var ms = new MemoryStream();
            using (var writer = new PdfWriter(ms))
            using (var pdf = new PdfDoc(writer))
            {
                var logoPath = IOPath.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    "images",
                    "logoNovaBRW.png"
                );

                var isMaster = string.Equals(modelo, "master", StringComparison.OrdinalIgnoreCase);

                float larguraInner = 280f;
                float alturaEtiqueta = 200f;
                float espacamento = 42.5f; // 1,5 cm

                var etiquetaSize = isMaster
                    ? new PageSize((larguraInner * 2f) + espacamento, alturaEtiqueta)
                    : new PageSize(larguraInner, alturaEtiqueta);

                pdf.SetDefaultPageSize(etiquetaSize);

                using (var doc = new Doc(pdf, etiquetaSize))
                {
                    doc.SetMargins(8, 10, 8, 10);

                    bool first = true;
                    foreach (DataRow r in rows.Rows)
                    {
                        if (!first)
                            doc.Add(new AB(ABT.NEXT_PAGE));
                        first = false;

                        AdicionarEtiqueta(doc, r, modelo, logoPath);
                    }

                    doc.Flush();
                }
            }

            var nome = $"Etiquetas_{modelo}_{DateTime.Now:yyyyMMdd_HHmm}.pdf";
            return File(ms.ToArray(), "application/pdf", nome);
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(
                @"C:\temp\EtiquetaPdf_Erros.txt",
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - ERRO: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}"
            );

            return Content("Erro ao gerar etiquetas. Verifique o log no servidor.");
        }
    }

    // ==========================
    // HELPERS (Ficha Técnica)
    // ==========================
    static string? Str(DataRow r, string col)
        => r.Table.Columns.Contains(col) && r[col] != DBNull.Value ? r[col]?.ToString() : null;

    static CellEl KeyCell(string txt) =>
        new CellEl()
            .Add(new Para(txt).SetBold().SetFontSize(FONT_BASE))
            .SetBackgroundColor(new RGB(245, 245, 245))
            .SetPaddingTop(PAD_Y).SetPaddingBottom(PAD_Y)
            .SetPaddingLeft(PAD_X).SetPaddingRight(PAD_X);

    static CellEl ValCell(string? txt) =>
        new CellEl()
            .Add(new Para(string.IsNullOrWhiteSpace(txt) ? "-" : txt).SetFontSize(FONT_BASE))
            .SetPaddingTop(PAD_Y).SetPaddingBottom(PAD_Y)
            .SetPaddingLeft(PAD_X).SetPaddingRight(PAD_X);

    static async Task<Img?> TryLoadImage(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var bytes = await http.GetByteArrayAsync(url);
            var data = ImageDataFactory.Create(bytes);
            return new Img(data);
        }
        catch { return null; }
    }

    // ==========================
    // Header / Footer (Ficha Técnica)
    // ==========================
    class BrwHeaderHandler : IEventHandler
    {
        private readonly string? _logoPath;
        public BrwHeaderHandler(string? logoPath) { _logoPath = logoPath; }

        public void HandleEvent(Event @event)
        {
            var docEvent = (PdfDocumentEvent)@event;
            var pdf = docEvent.GetDocument();
            var page = docEvent.GetPage();
            var ps = page.GetPageSize();
            int pageNum = pdf.GetPageNumber(page);

            var pCanvas = new PdfCanvas(page.NewContentStreamBefore(), page.GetResources(), pdf);
            pCanvas.SaveState();
            pCanvas.SetStrokeColor(new RGB(210, 210, 210));
            pCanvas.MoveTo(ps.GetLeft() + 36, ps.GetTop() - 70);
            pCanvas.LineTo(ps.GetRight() - 36, ps.GetTop() - 70);
            pCanvas.Stroke();
            pCanvas.RestoreState();

            using var canvas = new iText.Layout.Canvas(pCanvas, ps);

            if (!string.IsNullOrWhiteSpace(_logoPath) && System.IO.File.Exists(_logoPath))
            {
                var img = new Img(ImageDataFactory.Create(_logoPath));
                img.ScaleToFit(90, 32);
                img.SetFixedPosition(pageNum, ps.GetLeft() + 36, ps.GetTop() - 50);
                canvas.Add(img);
            }

            var title = new Para("FICHA TÉCNICA")
                .SetBold().SetFontSize(12)
                .SetTextAlignment(TextAlignment.CENTER);

            title.SetFixedPosition(pageNum, ps.GetLeft() + 36, ps.GetTop() - 48, ps.GetWidth() - 72);
            canvas.Add(title);
        }
    }

    class BrwFooterHandler : IEventHandler
    {
        public void HandleEvent(Event @event)
        {
            var docEvent = (PdfDocumentEvent)@event;
            var pdf = docEvent.GetDocument();
            var page = docEvent.GetPage();
            var ps = page.GetPageSize();
            int pageNum = pdf.GetPageNumber(page);

            var pCanvas = new PdfCanvas(page.NewContentStreamBefore(), page.GetResources(), pdf);
            pCanvas.SaveState();
            pCanvas.SetStrokeColor(new RGB(210, 210, 210));
            pCanvas.MoveTo(ps.GetLeft() + 36, ps.GetBottom() + 50);
            pCanvas.LineTo(ps.GetRight() - 36, ps.GetBottom() + 50);
            pCanvas.Stroke();
            pCanvas.RestoreState();

            using var canvas = new iText.Layout.Canvas(pCanvas, ps);

            var left = new Para(DateTime.Now.ToString("dd/MM/yyyy HH:mm"))
                .SetFontSize(9).SetTextAlignment(TextAlignment.LEFT);

            var right = new Para($"Página {pageNum} de {pdf.GetNumberOfPages()}")
                .SetFontSize(9).SetTextAlignment(TextAlignment.RIGHT);

            left.SetFixedPosition(pageNum, ps.GetLeft() + 36, ps.GetBottom() + 30, 200);
            right.SetFixedPosition(pageNum, ps.GetRight() - 236, ps.GetBottom() + 30, 200);

            canvas.Add(left);
            canvas.Add(right);
        }
    }

    // ==========================
    // Helpers numéricos
    // ==========================
    static void AdicionarEtiqueta(Doc doc, DataRow r, string modelo, string? logoPath)
    {
        var isInner = string.Equals(modelo, "inner", StringComparison.OrdinalIgnoreCase);
        var isMaster = string.Equals(modelo, "master", StringComparison.OrdinalIgnoreCase);

        string codigoItem = Str(r, "ItemCode") ?? "-";
        string descricao = ToFirstUpper(Str(r, "ItemName"));
        string origem = Str(r, "Name") ?? "-";

        string codBarras = isInner
            ? (Str(r, "U_InnerCodBarras") ?? "-")
            : (Str(r, "U_MasterCodBarras") ?? "-");

        string quantidade = isInner
            ? (Str(r, "U_QdeInner") ?? "-")
            : (Str(r, "U_QdeMaster") ?? "-");

        string comprimento = isInner
            ? FmtNumNoUnit(r["U_InnerComprimento"])
            : FmtNumNoUnit(r["U_MasterComprimento"]);

        string largura = isInner
            ? FmtNumNoUnit(r["U_InnerLargura"])
            : FmtNumNoUnit(r["U_MasterLargura"]);

        string altura = isInner
            ? FmtNumNoUnit(r["U_InnerAltura"])
            : FmtNumNoUnit(r["U_MasterAltura"]);

        string pesoBruto = isInner
            ? FmtNumNoUnit(r["U_InnerPeso"])
            : FmtNumNoUnit(r["U_MasterPeso"]);

        string pesoLiquido = isInner
            ? FmtNumNoUnit(r["U_InnerPesoLiq"])
            : FmtNumNoUnit(r["U_MasterPesoLiq"]);

        if (isMaster)
        {
            var linha = new Tbl(new float[] { 1f, 1f })
                .UseAllAvailableWidth()
                .SetBorder(ITextBorder.NO_BORDER)
                .SetMargin(0);

            linha.AddCell(CriarConteudoEtiqueta(
                doc, codigoItem, descricao, quantidade,
                comprimento, largura, altura,
                pesoBruto, pesoLiquido, codBarras, origem, logoPath,
                bordaDireita: false));

            linha.AddCell(CriarConteudoEtiqueta(
                doc, codigoItem, descricao, quantidade,
                comprimento, largura, altura,
                pesoBruto, pesoLiquido, codBarras, origem, logoPath,
                bordaDireita: false));

            doc.Add(linha);
        }
        else
        {
            var linha = new Tbl(new float[] { 1f })
                .UseAllAvailableWidth()
                .SetBorder(ITextBorder.NO_BORDER)
                .SetMargin(0);

            linha.AddCell(CriarConteudoEtiqueta(
                doc, codigoItem, descricao, quantidade,
                comprimento, largura, altura,
                pesoBruto, pesoLiquido, codBarras, origem, logoPath,
                bordaDireita: false));

            doc.Add(linha);
        }
    }
    public class EtiquetaPdfPayload
{
    public List<string> Codes { get; set; } = new();
    public string? Modelo { get; set; }
}
    static decimal? ParseNullableDecimal(object? v)
    {
        if (v is null || v == DBNull.Value) return null;
        var s = v.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(s)) return null;

        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.GetCultureInfo("pt-BR"), out var d)) return d;
        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) return d;

        s = s.Replace(',', '.');
        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) return d;

        return null;
    }

    static string FmtNumNoUnit(object? v, int dec = 2)
    {
        var d = ParseNullableDecimal(v);
        return d is null ? "-" : d.Value.ToString($"N{dec}", CultureInfo.GetCultureInfo("pt-BR"));
    }

    static string FmtNum(object? v, string unit, int dec = 2)
    {
        var txt = FmtNumNoUnit(v, dec);
        return txt == "-" ? "-" : $"{txt} {unit}";
    }

    static string DimCm(object? c, object? l, object? a)
    {
        var sC = FmtNumNoUnit(c);
        var sL = FmtNumNoUnit(l);
        var sA = FmtNumNoUnit(a);
        if (sC == "-" && sL == "-" && sA == "-") return "-";
        return $"{sC} x {sL} x {sA} cm";
    }

    static string ToFirstUpper(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "-";

        text = text.Trim().ToLower();
        return char.ToUpper(text[0]) + text.Substring(1);
    }

    static CellEl CriarConteudoEtiqueta(
    Doc doc,
    string codigoItem,
    string descricao,
    string quantidade,
    string comprimento,
    string largura,
    string altura,
    string pesoBruto,
    string pesoLiquido,
    string codBarras,
    string origem,
    string? logoPath,
    bool bordaDireita)
    {
        var fontLabel = 8.6f;
        var fontValue = 8.4f;
        var fontDesc = 8.2f;

        var wrapper = new CellEl()
            .SetBorder(ITextBorder.NO_BORDER)
            .SetPaddingTop(6)
            .SetPaddingBottom(6)
            .SetPaddingLeft(10)
            .SetPaddingRight(10);

        if (bordaDireita)
        {
            wrapper.SetBorderRight(new SolidBorder(ColorConstants.BLACK, 0.6f));
        }

        if (!string.IsNullOrWhiteSpace(logoPath) && System.IO.File.Exists(logoPath))
        {
            var logo = new Img(ImageDataFactory.Create(logoPath));
            logo.ScaleToFit(82, 24);
            logo.SetHorizontalAlignment(HorizontalAlignment.CENTER);
            logo.SetMarginTop(0);
            logo.SetMarginRight(0);
            logo.SetMarginBottom(4);
            logo.SetMarginLeft(0);
            wrapper.Add(logo);
        }

        // AQUI está a mudança principal:
        // usar Div com borda arredondada, em vez de Cell
        var box = new Div()
            .SetPaddingTop(10)
            .SetPaddingRight(12)
            .SetPaddingBottom(8)
            .SetPaddingLeft(12)
            .SetBorder(new SolidBorder(ColorConstants.BLACK, 0.8f))
            .SetBorderRadius(new BorderRadius(12))
            .SetMargin(0);

        var topInfo = new Tbl(new float[] { 44, 1f }).UseAllAvailableWidth();
        topInfo.SetBorder(ITextBorder.NO_BORDER);

        topInfo.AddCell(
            new CellEl()
                .Add(new Para("Item")
                    .SetBold()
                    .SetFontSize(fontLabel))
                .SetBorder(ITextBorder.NO_BORDER)
                .SetPadding(0)
                .SetVerticalAlignment(VerticalAlignment.TOP)
        );

        topInfo.AddCell(
            new CellEl()
                .Add(new Para(descricao)
                    .SetFontSize(fontDesc)
                    .SetMultipliedLeading(1.02f)
                    .SetMargin(0))
                .SetBorder(ITextBorder.NO_BORDER)
                .SetPadding(0)
                .SetVerticalAlignment(VerticalAlignment.TOP)
        );

        box.Add(topInfo);

        box.Add(new Para("")
            .SetMarginTop(2)
            .SetMarginRight(0)
            .SetMarginBottom(3)
            .SetMarginLeft(0));

        var info = new Tbl(new float[] { 54, 66, 58, 38 }).UseAllAvailableWidth();
        info.SetBorder(ITextBorder.NO_BORDER);

        CellEl LabelCell(string text)
        {
            var safeText = (text ?? "").Replace(" ", "\u00A0");

            return new CellEl()
                .Add(new Para(safeText)
                    .SetBold()
                    .SetFontSize(fontLabel)
                    .SetTextAlignment(TextAlignment.LEFT))
                .SetBorder(ITextBorder.NO_BORDER)
                .SetPadding(0)
                .SetTextAlignment(TextAlignment.LEFT);
        }

        CellEl ValueCell(string text) => new CellEl()
            .Add(new Para(text).SetFontSize(fontValue))
            .SetBorder(ITextBorder.NO_BORDER)
            .SetPadding(0)
            .SetTextAlignment(TextAlignment.LEFT);

        info.AddCell(LabelCell("Código"));
        info.AddCell(ValueCell(codigoItem));
        info.AddCell(LabelCell("Qtde."));
        info.AddCell(ValueCell(quantidade));

        info.AddCell(LabelCell("Medida"));
        info.AddCell(new CellEl(1, 3)
            .Add(new Para($"{comprimento} x {largura} x {altura} cm").SetFontSize(fontValue))
            .SetBorder(ITextBorder.NO_BORDER)
            .SetPadding(0)
            .SetTextAlignment(TextAlignment.LEFT));

        info.AddCell(LabelCell("Peso bruto"));
        info.AddCell(ValueCell($"{pesoBruto} kg"));
        info.AddCell(LabelCell("Peso líquido"));
        info.AddCell(ValueCell($"{pesoLiquido} kg"));

        box.Add(info);

        box.Add(new Para("")
            .SetMarginTop(4)
            .SetMarginRight(0)
            .SetMarginBottom(3)
            .SetMarginLeft(0)
            .SetBorderBottom(new SolidBorder(ColorConstants.BLACK, 0.6f)));

        if (!string.IsNullOrWhiteSpace(codBarras) && codBarras != "-")
        {
            var barcode = new Barcode128(doc.GetPdfDocument());
            barcode.SetCode(codBarras);
            barcode.SetCodeType(Barcode128.CODE128);

            var img = new Img(barcode.CreateFormXObject(doc.GetPdfDocument()))
                .SetHorizontalAlignment(HorizontalAlignment.CENTER)
                .ScaleToFit(155, 30)
                .SetMarginTop(4)
                .SetMarginRight(0)
                .SetMarginBottom(0)
                .SetMarginLeft(0);

            box.Add(img);
        }

        wrapper.Add(box);

        wrapper.Add(new Para($"Origem: {origem}")
            .SetFontSize(8.2f)
            .SetTextAlignment(TextAlignment.CENTER)
            .SetMarginTop(6)
            .SetMarginRight(0)
            .SetMarginBottom(0)
            .SetMarginLeft(0));

        return wrapper;
    }
    static string Kg(object? v) => FmtNum(v, "kg", 2);
    static float Cm(float valor)
    {
        return valor * 28.35f;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireReport("MUDANCA_CARTEIRA")]
    public async Task<IActionResult> ServiceLayerInativarMassaStart(IFormFile Arquivo, bool Confirmar)
    {
        if (!Confirmar)
            return BadRequest(new { error = "Confirmação obrigatória." });

        if (Arquivo == null || Arquivo.Length == 0)
            return BadRequest(new { error = "Selecione um arquivo Excel válido." });

        var sapUser = HttpContext.Session.GetString("SAP_SL_User");
        var sapPass = HttpContext.Session.GetString("SAP_SL_Pass");

        if (string.IsNullOrWhiteSpace(sapUser) || string.IsNullOrWhiteSpace(sapPass))
            return Unauthorized(new { error = "Sessão SAP expirada. Faça login novamente." });

        var jobId = Guid.NewGuid().ToString("N");
        var tempPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"InativarPN_{jobId}.xlsx"
        );

        await using (var fs = System.IO.File.Create(tempPath))
        {
            await Arquivo.CopyToAsync(fs);
        }

        _cache.Set(CacheKeyInativarMassa(jobId), new InativacaoMassaStatus
        {
            Percent = 0,
            Processed = 0,
            Total = 0,
            Inativados = 0,
            Done = false
        }, TimeSpan.FromHours(2));

        _ = Task.Run(async () =>
        {
            try
            {
                var login = await _slClient.LoginAsync(sapUser, sapPass);

                if (!login.ok)
                    throw new Exception(login.error ?? "Falha ao fazer login no Service Layer.");

                var cardCodes = LerCardCodesExcel(tempPath);
                var total = cardCodes.Count;

                var status = new InativacaoMassaStatus
                {
                    Percent = 0,
                    Processed = 0,
                    Total = total,
                    Inativados = 0,
                    Done = false
                };

                _cache.Set(CacheKeyInativarMassa(jobId), status, TimeSpan.FromHours(2));

                foreach (var cardCode in cardCodes)
                {
                    try
                    {
                        var (ok, erro) = await _inativacaoParceiroService.InativarAsync(cardCode);

                        if (ok)
                            status.Inativados++;
                        else
                            status.Erros.Add($"PN: {cardCode} - {erro}");
                    }
                    catch (Exception ex)
                    {
                        status.Erros.Add($"PN: {cardCode} - {ex.Message}");
                    }

                    status.Processed++;
                    status.Percent = total > 0
                        ? (int)Math.Round((status.Processed * 100.0) / total)
                        : 100;

                    _cache.Set(CacheKeyInativarMassa(jobId), status, TimeSpan.FromHours(2));
                }

                status.Done = true;
                status.Percent = 100;

                _cache.Set(CacheKeyInativarMassa(jobId), status, TimeSpan.FromHours(2));
            }
            catch (Exception ex)
            {
                _cache.Set(CacheKeyInativarMassa(jobId), new InativacaoMassaStatus
                {
                    Percent = 100,
                    Processed = 0,
                    Total = 0,
                    Inativados = 0,
                    Done = true,
                    Error = ex.Message
                }, TimeSpan.FromHours(2));
            }
            finally
            {
                try { System.IO.File.Delete(tempPath); } catch { }
            }
        });

        return Json(new { jobId });
    }

    [HttpGet]
    [RequireReport("MUDANCA_CARTEIRA")]
    public IActionResult ServiceLayerInativarMassaStatus(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return BadRequest(new { error = "jobId não informado." });

        var status = _cache.Get<InativacaoMassaStatus>(CacheKeyInativarMassa(jobId));

        if (status == null)
            return NotFound(new { error = "Job não encontrado ou expirado." });

        return Json(new
        {
            percent = status.Percent,
            processed = status.Processed,
            total = status.Total,
            inativados = status.Inativados,
            errosCount = status.ErrosCount,
            erros = status.Erros.Take(50).ToList(),
            done = status.Done,
            error = status.Error
        });
    }

    private static List<string> LerCardCodesExcel(string path)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        using var pkg = new ExcelPackage(new FileInfo(path));
        var ws = pkg.Workbook.Worksheets.FirstOrDefault();

        if (ws == null || ws.Dimension == null)
            throw new Exception("A planilha está vazia.");

        var colCardCode = 0;

        for (int col = 1; col <= ws.Dimension.End.Column; col++)
        {
            var header = ws.Cells[1, col].Text?.Trim();

            if (string.Equals(header, "CardCode", StringComparison.OrdinalIgnoreCase))
            {
                colCardCode = col;
                break;
            }
        }

        if (colCardCode == 0)
            throw new Exception("Coluna 'CardCode' não encontrada na planilha.");

        var lista = new List<string>();

        for (int row = 2; row <= ws.Dimension.End.Row; row++)
        {
            var cardCode = ws.Cells[row, colCardCode].Text?.Trim();

            if (!string.IsNullOrWhiteSpace(cardCode))
                lista.Add(cardCode);
        }

        return lista
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
