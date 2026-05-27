using Microsoft.AspNetCore.Mvc;
using PortalHelpdeskTI.Models.Integracoes;
using PortalHelpdeskTI.Services.Integracoes;

namespace PortalHelpdeskTI.Controllers.Api
{
    [ApiController]
    [Route("api/wms/faturamento-ordem-saida")]
    public sealed class WmsFaturamentoOrdemSaidaController : ControllerBase
    {
        private readonly WmsApiService _wmsApiService;
        private readonly WmsFaturamentoPayloadService _payloadService;

        public WmsFaturamentoOrdemSaidaController(
            WmsApiService wmsApiService,
            WmsFaturamentoPayloadService payloadService)
        {
            _wmsApiService = wmsApiService;
            _payloadService = payloadService;
        }

        [HttpGet("enviar")]
        [HttpGet("~/api/wms/enviarFaturamentoOrdemSaida")]
        public IActionResult InformacoesEndpoint()
        {
            return Content(RenderSwaggerWmsPage(), "text/html; charset=utf-8");
        }

        [HttpPost("enviar")]
        [HttpPost("~/api/wms/enviarFaturamentoOrdemSaida")]
        public async Task<IActionResult> Enviar([FromBody] WmsEnviarFaturamentoManualRequest request, CancellationToken ct)
        {
            if (request == null)
            {
                return BadRequest(new
                {
                    codigo = "99",
                    descricao = "JSON invalido."
                });
            }

            try
            {
                var payload = _payloadService.DeveMontarPorPedido(request)
                    ? await _payloadService.MontarPorPedidoAsync(request.docEntryPedido, request.docNumPedido, ct)
                    : _payloadService.MontarDireto(request);

                var retorno = await _wmsApiService.EnviarAsync(payload);

                return Ok(retorno ?? new WmsEnviarFaturamentoResponse
                {
                    codigo = "00",
                    descricao = "Envio realizado sem corpo de retorno."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    codigo = "99",
                    descricao = "Erro ao enviar faturamento da ordem de saida ao WMS.",
                    detalhe = ex.Message
                });
            }
        }

        [HttpPost("gerar-json")]
        [HttpPost("~/api/wms/enviarFaturamentoOrdemSaida/gerar-json")]
        public async Task<IActionResult> GerarJson([FromBody] WmsEnviarFaturamentoManualRequest request, CancellationToken ct)
        {
            if (request == null)
            {
                return BadRequest(new
                {
                    codigo = "99",
                    descricao = "JSON invalido."
                });
            }

            try
            {
                var payload = _payloadService.DeveMontarPorPedido(request)
                    ? await _payloadService.MontarPorPedidoAsync(request.docEntryPedido, request.docNumPedido, ct)
                    : _payloadService.MontarDireto(request);

                return Ok(payload);
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    codigo = "99",
                    descricao = "Erro ao gerar JSON de faturamento da ordem de saida.",
                    detalhe = ex.Message
                });
            }
        }

        private static string RenderSwaggerWmsPage()
        {
            return """
<!doctype html>
<html lang="pt-BR">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>Swagger WMS Enviar Faturamento - Portal Helpdesk TI</title>
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
                    <span>Integracoes</span>
                </a>
                <button id="themeToggle" class="btn-light-nav" type="button" aria-label="Alternar tema">Dark</button>
            </div>
        </div>
    </nav>

    <main class="swagger-page">
        <div class="swagger-title">
            <div>
                <h1>Swagger WMS Enviar Faturamento</h1>
                <p>Teste manual do endpoint enviarFaturamentoOrdemSaida para envio de dados de faturamento ao WMS.</p>
            </div>
        </div>

        <section class="portal-card">
            <div class="endpoint-strip">
                <span class="method">POST</span>
                <code>/api/wms/enviarFaturamentoOrdemSaida</code>
            </div>

            <div class="tester">
                <div>
                    <div class="panel-title">
                        <label for="payload">JSON da requisicao</label>
                    </div>
                    <textarea id="payload" spellcheck="false">{
  "docEntryPedido": 320689,
  "docNumPedido": null
}</textarea>
                    <button id="gerarJson" class="btn-portal" type="button">Gerar JSON</button>
                    <button id="executar" class="btn-portal" type="button">Executar integracao</button>
                </div>
                <div>
                    <div class="panel-title">
                        <label for="retorno">Retorno</label>
                    </div>
                    <div id="status" class="status">Aguardando execucao.</div>
                    <pre id="retorno">{}</pre>
                </div>
            </div>

            <div class="endpoint-note">
                Informe <code>docEntryPedido</code> ou <code>docNumPedido</code> para o portal buscar a nota fiscal de saida gerada e montar o JSON do WMS. O botao Gerar JSON apenas retorna o payload; Executar integracao envia para <code>https://apiwms.flsoft.com.br/brwims/wms/rest/TSM/enviarFaturamentoOrdemSaida</code>.
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
        const btnGerar = document.getElementById('gerarJson');
        const payload = document.getElementById('payload');
        const statusEl = document.getElementById('status');
        const retorno = document.getElementById('retorno');

        async function executarEndpoint(url, button, loadingText, finalText) {
            let body;
            statusEl.className = 'status';
            retorno.textContent = '{}';

            try {
                body = JSON.stringify(JSON.parse(payload.value));
            } catch (err) {
                statusEl.textContent = 'JSON invalido.';
                statusEl.className = 'status erro';
                retorno.textContent = String(err);
                return;
            }

            btn.disabled = true;
            btnGerar.disabled = true;
            button.textContent = loadingText;
            statusEl.textContent = 'Enviando para o endpoint...';

            try {
                const response = await fetch(url, {
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
                statusEl.textContent = 'Falha ao executar requisicao.';
                statusEl.className = 'status erro';
                retorno.textContent = String(err);
            } finally {
                btn.disabled = false;
                btnGerar.disabled = false;
                button.textContent = finalText;
            }
        }

        btnGerar.addEventListener('click', () => executarEndpoint(
            '/api/wms/enviarFaturamentoOrdemSaida/gerar-json',
            btnGerar,
            'Gerando...',
            'Gerar JSON'));

        btn.addEventListener('click', () => executarEndpoint(
            '/api/wms/enviarFaturamentoOrdemSaida',
            btn,
            'Executando...',
            'Executar integracao'));
    </script>
</body>
</html>
""";
        }
    }
}
