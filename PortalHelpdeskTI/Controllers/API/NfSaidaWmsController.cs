using Microsoft.AspNetCore.Mvc;
using PortalHelpdeskTI.Models.Integracoes;
using PortalHelpdeskTI.Services.SAP;
using System.Data.Odbc;
using System.Text.Json;

namespace PortalHelpdeskTI.Controllers.Api
{
    [ApiController]
    [Route("api/wms/nota-fiscal-saida")]
    public sealed class NfSaidaWmsController : ControllerBase
    {
        private readonly ServiceLayerClient _serviceLayerClient;
        private readonly string _hanaConnStr;
        private readonly ILogger<NfSaidaWmsController> _logger;

        public NfSaidaWmsController(
            ServiceLayerClient serviceLayerClient,
            IConfiguration configuration,
            ILogger<NfSaidaWmsController> logger)
        {
            _serviceLayerClient = serviceLayerClient;
            _hanaConnStr = configuration.GetConnectionString("HanaConn")
                ?? throw new InvalidOperationException("ConnectionString 'HanaConn' n\u00e3o configurada.");
            _logger = logger;
        }

        [HttpGet("criar")]
        [HttpGet("~/api/wms/retorno-saida")]
        [HttpGet("~/api/wms/retornoOrdemSaida")]
        [HttpGet("~/api/wms/faturamento-saida/efetivarnota")]
        public IActionResult InformacoesEndpoint()
        {
            return Content(RenderSwaggerWmsPage(), "text/html; charset=utf-8");
        }

        [HttpGet("vincular-lotes-pedido")]
        [HttpGet("~/api/wms/pedido-saida/vincular-lotes")]
        [HttpGet("~/api/wms/faturamento-saida/vincularlotespedido")]
        [HttpGet("~/api/wms/faturamento-saida/criar-draft-nf")]
        public IActionResult InformacoesEndpointVincularLotes()
        {
            return Content(RenderSwaggerVincularLotesPage(), "text/html; charset=utf-8");
        }

        [HttpPost("criar")]
        [HttpPost("~/api/wms/retorno-saida")]
        [HttpPost("~/api/wms/retornoOrdemSaida")]
        [HttpPost("~/api/wms/faturamento-saida/efetivarnota")]
        public async Task<IActionResult> Criar([FromBody] WmsRetornoOrdemSaidaRequest request, CancellationToken ct)
        {
            if (request == null)
            {
                return await ResponderComLogAsync(
                    StatusCodes.Status400BadRequest,
                    null,
                    new { codigo = "99", descricao = "JSON inv\u00e1lido." },
                    "ERRO - JSON inv\u00e1lido.",
                    null);
            }

            try
            {
                var criar = await _serviceLayerClient.CriarNfSaidaDePedidoVendaAsync(request, ct);

                if (!criar.ok)
                {
                    var erro = new
                    {
                        codigo = "99",
                        descricao = string.IsNullOrWhiteSpace(criar.error)
                            ? "Erro interno ao processar retorno de sa\u00edda."
                            : criar.error
                    };

                    return await ResponderComLogAsync(
                        StatusCodes.Status500InternalServerError,
                        request,
                        erro,
                        string.IsNullOrWhiteSpace(criar.error)
                            ? "ERRO - Erro interno ao processar retorno de sa\u00edda."
                            : $"ERRO - {criar.error}",
                        null);
                }

                var sucesso = new
                {
                    codigo = "00",
                    descricao = "Nota fiscal de sa\u00edda criada com sucesso."
                };

                return await ResponderComLogAsync(
                    StatusCodes.Status200OK,
                    request,
                    sucesso,
                    "OK - Nota fiscal de sa\u00edda criada com sucesso.",
                    criar.docEntryNota > 0 ? criar.docEntryNota.ToString() : null);
            }
            catch
            {
                var erro = new
                {
                    codigo = "99",
                    descricao = "Erro interno ao processar retorno de sa\u00edda."
                };

                return await ResponderComLogAsync(
                    StatusCodes.Status500InternalServerError,
                    request,
                    erro,
                    "ERRO - Erro interno ao processar retorno de sa\u00edda.",
                    null);
            }
        }

        [HttpPost("vincular-lotes-pedido")]
        [HttpPost("~/api/wms/pedido-saida/vincular-lotes")]
        [HttpPost("~/api/wms/faturamento-saida/vincularlotespedido")]
        [HttpPost("~/api/wms/faturamento-saida/criar-draft-nf")]
        public async Task<IActionResult> VincularLotesPedido([FromBody] WmsRetornoOrdemSaidaRequest request, CancellationToken ct)
        {
            const string metodoLog = "POST /api/wms/faturamento-saida/criar-draft-nf";

            if (request == null)
            {
                return await ResponderComLogAsync(
                    StatusCodes.Status400BadRequest,
                    null,
                    new { codigo = "99", descricao = "JSON inválido." },
                    "ERRO - JSON inválido.",
                    null,
                    metodoLog);
            }

            try
            {
                var draft = await _serviceLayerClient.CriarDraftNfSaidaDePedidoVendaAsync(request, ct);

                if (!draft.ok)
                {
                    var erro = new
                    {
                        codigo = "99",
                        descricao = string.IsNullOrWhiteSpace(draft.error)
                            ? "Erro interno ao criar esboço de NF de saída com lotes."
                            : draft.error,
                        retornoSap = string.IsNullOrWhiteSpace(draft.body)
                            ? null
                            : draft.body
                    };

                    return await ResponderComLogAsync(
                        StatusCodes.Status500InternalServerError,
                        request,
                        erro,
                        string.IsNullOrWhiteSpace(draft.error)
                            ? "ERRO - Erro interno ao criar esboço de NF de saída com lotes."
                            : $"ERRO - {draft.error}",
                        draft.docEntryDraft > 0 ? draft.docEntryDraft.ToString() : null,
                        metodoLog);
                }

                var sucesso = new
                {
                    codigo = "00",
                    descricao = "Esboço de NF de saída criado com lotes vinculados com sucesso.",
                    docEntryDraft = draft.docEntryDraft,
                    docNumDraft = draft.docNumDraft,
                    retornoSap = draft.body
                };

                return await ResponderComLogAsync(
                    StatusCodes.Status200OK,
                    request,
                    sucesso,
                    "OK - Esboço de NF de saída criado com lotes vinculados com sucesso.",
                    draft.docEntryDraft > 0 ? draft.docEntryDraft.ToString() : null,
                    metodoLog);
            }
            catch (Exception ex)
            {
                var erro = new
                {
                    codigo = "99",
                    descricao = "Erro interno ao criar esboço de NF de saída com lotes.",
                    detalhe = ex.Message
                };

                return await ResponderComLogAsync(
                    StatusCodes.Status500InternalServerError,
                    request,
                    erro,
                    $"ERRO - Erro interno ao criar esboço de NF de saída com lotes. {ex.Message}",
                    null,
                    metodoLog);
            }
        }

        private static string RenderSwaggerWmsPage()
        {
            return RenderSwaggerWmsPage(
                "Swagger WMS NF Saida - Portal Helpdesk TI",
                "Swagger WMS NF Sa&iacute;da",
                "Teste e documenta&ccedil;&atilde;o do endpoint de retorno WMS para cria&ccedil;&atilde;o de NF de sa&iacute;da.",
                "/api/wms/nota-fiscal-saida/criar",
                """
{
  "NUM_ORD_SAI": 320689,
  "COD_PROPRIET": 100,
  "FLAG_DIVERGENCIA": "N",
  "VOLUMES_TOTAL": 1,
  "OS_CANCELADA": "N",
  "LISTA_ITENS": [],
  "LISTA_VOLUMES": []
}
""",
                "A API cria a NF de sa&iacute;da no SAP com base no pedido de venda, preservando cliente, condi&ccedil;&atilde;o de pagamento, utiliza&ccedil;&atilde;o e demais refer&ecirc;ncias do pedido.");
        }

        private static string RenderSwaggerVincularLotesPage()
        {
            return RenderSwaggerWmsPage(
                "Swagger WMS Vinculo de Lotes - Portal Helpdesk TI",
                "Swagger WMS Draft NF Sa&iacute;da",
                "Teste e documenta&ccedil;&atilde;o do endpoint de retorno WMS para criar esbo&ccedil;o de NF de sa&iacute;da com lotes.",
                "/api/wms/faturamento-saida/criar-draft-nf",
                """
{
  "NUM_ORD_SAI": 320689,
  "COD_PROPRIET": 100,
  "FLAG_DIVERGENCIA": "N",
  "PESO_TOTAL": 1,
  "VOLUMES_TOTAL": 1,
  "OS_CANCELADA": "N",
  "LISTA_ITENS": [
    {
      "COD_PRODUTO": "CODIGO_DO_ITEM",
      "QTDE_EMBAL": 1,
      "QTDE_ATENDIDA": 1,
      "LISTA_LOTES": [
        {
          "NUM_LOTE": "LOTE_DO_WMS",
          "DATA_VALIDADE": "2026-12-31",
          "DATA_FABRICACAO": "2026-01-01",
          "QTDE_ATENDIDA": 1
        }
      ]
    }
  ],
  "LISTA_VOLUMES": []
}
""",
                "A API localiza o pedido de venda no SAP e cria um esbo&ccedil;o de NF de sa&iacute;da em <code>Drafts</code>, com os lotes informados pelo WMS nas linhas do documento.");
        }

        private static string RenderSwaggerWmsPage(
            string pageTitle,
            string heading,
            string description,
            string endpoint,
            string samplePayload,
            string endpointNote)
        {
            var html = """
<!doctype html>
<html lang="pt-BR">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>__PAGE_TITLE__</title>
    <link rel="icon" type="image/x-icon" href="/images/LogoHelp_512x512.png">
    <link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.min.css">
    <link rel="stylesheet" href="/css/site.css">
    <style>
        body {
            padding-top: 80px;
            min-height: 100vh;
            background: var(--bg);
            color: var(--text);
        }

        .swagger-nav {
            height: 56px;
            background: #022743;
            z-index: 1030;
        }

        .swagger-page {
            max-width: 1180px;
            margin: 0 auto;
            padding: 0 1rem 2rem;
        }

        .swagger-title {
            display: flex;
            align-items: flex-start;
            justify-content: space-between;
            gap: 1rem;
            margin-bottom: 1rem;
        }

        .swagger-title h1 {
            color: var(--heading-fg);
            font-size: 1.65rem;
            margin: 0 0 .35rem;
            font-weight: 700;
        }

        .swagger-title p {
            color: var(--muted);
            margin: 0;
        }

        .portal-card {
            background: var(--surface);
            color: var(--text);
            border: 1px solid var(--border);
            border-radius: .75rem;
            box-shadow: 0 8px 20px rgba(15,23,42,.06);
            overflow: hidden;
        }

        html.dark .portal-card,
        [data-bs-theme="dark"] .portal-card {
            box-shadow: 0 8px 24px rgba(0,0,0,.45);
        }

        .endpoint-strip {
            display: flex;
            align-items: center;
            gap: .75rem;
            flex-wrap: wrap;
            padding: 1rem 1.25rem;
            border-bottom: 1px solid var(--border);
            background: var(--surface-alt);
        }

        .method {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            min-width: 64px;
            height: 30px;
            border-radius: .45rem;
            background: var(--accent);
            color: var(--accent-contrast);
            font-weight: 800;
            letter-spacing: 0;
        }

        code,
        pre,
        textarea {
            background: var(--surface-alt);
            color: var(--text);
            border: 1px solid var(--border);
            border-radius: .5rem;
        }

        code {
            padding: .18rem .45rem;
            font-size: .9em;
        }

        .tester {
            display: grid;
            grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
            gap: 1rem;
            padding: 1.25rem;
        }

        .panel-title {
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: .75rem;
            margin-bottom: .65rem;
        }

        label {
            color: var(--label-fg);
            font-weight: 700;
            margin: 0;
        }

        textarea {
            box-sizing: border-box;
            width: 100%;
            min-height: 430px;
            padding: .9rem;
            font: 13px Consolas, "Liberation Mono", monospace;
            resize: vertical;
            outline: none;
        }

        textarea:focus {
            border-color: var(--primary-hover);
            box-shadow: 0 0 0 .2rem rgba(0, 85, 165, .16);
        }

        pre {
            min-height: 430px;
            margin: 0;
            padding: .9rem;
            overflow: auto;
            font: 13px Consolas, "Liberation Mono", monospace;
            white-space: pre-wrap;
        }

        .btn-portal {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            gap: .5rem;
            min-height: 38px;
            margin-top: .75rem;
            border: 0;
            border-radius: .5rem;
            padding: .5rem .85rem;
            background: var(--primary);
            color: var(--primary-contrast);
            font-weight: 700;
            cursor: pointer;
        }

        .btn-portal:hover {
            background: var(--primary-hover);
        }

        .btn-portal:disabled {
            opacity: .7;
            cursor: wait;
        }

        .btn-light-nav {
            color: #fff;
            border: 0;
            background: transparent;
            border-radius: .5rem;
            padding: .375rem .6rem;
            text-decoration: none;
        }

        .btn-light-nav:hover,
        .btn-light-nav:focus {
            color: #fff;
            background: rgba(255,255,255,.12);
        }

        .status {
            margin: .75rem 0 .65rem;
            font-weight: 700;
        }

        .ok { color: #047857; }
        .erro { color: #b91c1c; }

        html.dark .ok,
        [data-bs-theme="dark"] .ok { color: #34d399; }

        html.dark .erro,
        [data-bs-theme="dark"] .erro { color: #fca5a5; }

        .endpoint-note {
            padding: 0 1.25rem 1.25rem;
            color: var(--muted);
        }

        @media (max-width: 860px) {
            body { padding-top: 72px; }
            .swagger-title { flex-direction: column; }
            .tester { grid-template-columns: 1fr; }
            textarea, pre { min-height: 320px; }
        }
    </style>
    <script>
        (function () {
            try {
                var stored = localStorage.getItem('theme');
                var prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
                var theme = stored || (prefersDark ? 'dark' : 'light');
                document.documentElement.setAttribute('data-bs-theme', theme);
                document.documentElement.classList.toggle('dark', theme === 'dark');
            } catch (e) {}
        })();
    </script>
</head>
<body>
    <nav class="navbar navbar-dark swagger-nav fixed-top">
        <div class="container-fluid">
            <a class="navbar-brand d-flex align-items-center" href="/Inicio/Index">
                <img src="/images/LogoBranco.png" alt="Logo BRW" style="height:35px;">
            </a>
            <div class="d-flex align-items-center gap-2">
                <a class="btn-light-nav d-inline-flex align-items-center gap-2" href="/Integracoes/Index">
                    <span aria-hidden="true">&larr;</span>
                    <span>Integra&ccedil;&otilde;es</span>
                </a>
                <button id="themeToggle" class="btn-light-nav" type="button" aria-label="Alternar tema">Dark</button>
            </div>
        </div>
    </nav>

    <main class="swagger-page">
        <div class="swagger-title">
            <div>
                <h1>__HEADING__</h1>
                <p>__DESCRIPTION__</p>
            </div>
        </div>

        <section class="portal-card">
            <div class="endpoint-strip">
                <span class="method">POST</span>
                <code>__ENDPOINT__</code>
            </div>

            <div class="tester">
                <div>
                    <div class="panel-title">
                        <label for="payload">JSON da requisi&ccedil;&atilde;o</label>
                    </div>
                    <textarea id="payload" spellcheck="false">__SAMPLE_PAYLOAD__</textarea>
                    <button id="executar" class="btn-portal" type="button">Executar integra&ccedil;&atilde;o</button>
                </div>
                <div>
                    <div class="panel-title">
                        <label for="retorno">Retorno</label>
                    </div>
                    <div id="status" class="status">Aguardando execu&ccedil;&atilde;o.</div>
                    <pre id="retorno">{}</pre>
                </div>
            </div>

            <div class="endpoint-note">
                __ENDPOINT_NOTE__
            </div>
        </section>
    </main>
    <script>
        function applyTheme(theme) {
            document.documentElement.setAttribute('data-bs-theme', theme);
            document.documentElement.classList.toggle('dark', theme === 'dark');
            localStorage.setItem('theme', theme);
            const toggle = document.getElementById('themeToggle');
            if (toggle) toggle.textContent = theme === 'dark' ? 'Light' : 'Dark';
        }

        applyTheme(document.documentElement.getAttribute('data-bs-theme') || 'light');

        document.getElementById('themeToggle')?.addEventListener('click', () => {
            const current = document.documentElement.getAttribute('data-bs-theme') || 'light';
            applyTheme(current === 'dark' ? 'light' : 'dark');
        });

        const btn = document.getElementById('executar');
        const payload = document.getElementById('payload');
        const statusEl = document.getElementById('status');
        const retorno = document.getElementById('retorno');

        btn.addEventListener('click', async () => {
            let body;
            statusEl.className = 'status';
            retorno.textContent = '{}';

            try {
                body = JSON.stringify(JSON.parse(payload.value));
            } catch (err) {
                statusEl.textContent = 'JSON inv\u00e1lido.';
                statusEl.className = 'status erro';
                retorno.textContent = String(err);
                return;
            }

            btn.disabled = true;
            btn.textContent = 'Executando...';
            statusEl.textContent = 'Enviando para o endpoint...';

            try {
                const response = await fetch('__ENDPOINT__', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body
                });

                const text = await response.text();
                let formatted = text;

                try {
                    formatted = JSON.stringify(JSON.parse(text), null, 2);
                } catch {
                    formatted = text || '(sem corpo)';
                }

                statusEl.textContent = `HTTP ${response.status} ${response.statusText}`;
                statusEl.className = response.ok ? 'status ok' : 'status erro';
                retorno.textContent = formatted;
            } catch (err) {
                statusEl.textContent = 'Falha ao executar requisi\u00e7\u00e3o.';
                statusEl.className = 'status erro';
                retorno.textContent = String(err);
            } finally {
                btn.disabled = false;
                btn.textContent = 'Executar integra\u00e7\u00e3o';
            }
        });
    </script>
</body>
</html>
""";

            return html
                .Replace("__PAGE_TITLE__", pageTitle)
                .Replace("__HEADING__", heading)
                .Replace("__DESCRIPTION__", description)
                .Replace("__ENDPOINT__", endpoint)
                .Replace("__SAMPLE_PAYLOAD__", samplePayload)
                .Replace("__ENDPOINT_NOTE__", endpointNote);
        }

        private async Task<IActionResult> ResponderComLogAsync(
            int statusCode,
            WmsRetornoOrdemSaidaRequest? request,
            object response,
            string message,
            string? keySap,
            string metodo = "POST /retornoOrdemSaida")
        {
            await RegistrarLogRetornoSapAsync(request, response, message, keySap, metodo);
            return StatusCode(statusCode, response);
        }

        private async Task RegistrarLogRetornoSapAsync(
            WmsRetornoOrdemSaidaRequest? request,
            object response,
            string message,
            string? keySap,
            string metodo)
        {
            try
            {
                const string sql = @"
INSERT INTO ""INTEGRACAO_WMS"".""K33P_LOG_WMS_API""
    (""LOG_TS"", ""METHOD"", ""MESSAGE"", ""KEY_WMS"", ""KEY_SAP"", ""REQUEST_JSON"", ""RESPONSE_JSON"")
VALUES
    (CURRENT_TIMESTAMP, ?, ?, ?, ?, ?, ?)";

                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                };

                var requestJson = JsonSerializer.Serialize(request, options);
                var responseJson = JsonSerializer.Serialize(response, options);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

                using var conn = new OdbcConnection(_hanaConnStr);
                await conn.OpenAsync(timeoutCts.Token);

                using var cmd = new OdbcCommand(sql, conn);
                cmd.CommandTimeout = 10;
                cmd.Parameters.Add("pMethod", OdbcType.NVarChar, 300).Value = metodo;
                cmd.Parameters.Add("pMessage", OdbcType.NVarChar, 5000).Value = Limitar(message, 5000);
                cmd.Parameters.Add("pKeyWms", OdbcType.NVarChar, 200).Value = request?.NumOrdSaida > 0 ? request.NumOrdSaida.ToString() : DBNull.Value;
                cmd.Parameters.Add("pKeySap", OdbcType.NVarChar, 200).Value = !string.IsNullOrWhiteSpace(keySap) ? keySap : request?.NumOrdSaida > 0 ? request.NumOrdSaida.ToString() : DBNull.Value;
                cmd.Parameters.Add("pRequestJson", OdbcType.NText).Value = requestJson;
                cmd.Parameters.Add("pResponseJson", OdbcType.NText).Value = responseJson;

                await cmd.ExecuteNonQueryAsync(timeoutCts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao registrar log de retorno WMS -> SAP para NF de sa\u00edda.");
            }
        }

        private static string Limitar(string valor, int tamanhoMaximo)
        {
            if (string.IsNullOrEmpty(valor) || valor.Length <= tamanhoMaximo)
                return valor;

            return valor[..tamanhoMaximo];
        }
    }
}
