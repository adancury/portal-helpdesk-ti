using Microsoft.AspNetCore.Mvc;
using PortalHelpdeskTI.Models;
using PortalHelpdeskTI.Models.Integracoes;
using PortalHelpdeskTI.Services.SAP;
using System.Data.Odbc;
using System.Text.Json;

namespace PortalHelpdeskTI.Controllers.Api
{
    [ApiController]
    [Route("api/wms/devolucao-nf-saida")]
    public class DevolucaoNfSaidaController : ControllerBase
    {
        private readonly ServiceLayerClient _serviceLayerClient;
        private readonly string _hanaConnStr;
        private readonly ILogger<DevolucaoNfSaidaController> _logger;

        public DevolucaoNfSaidaController(
            ServiceLayerClient serviceLayerClient,
            IConfiguration configuration,
            ILogger<DevolucaoNfSaidaController> logger)
        {
            _serviceLayerClient = serviceLayerClient;
            _hanaConnStr = configuration.GetConnectionString("HanaConn")
                ?? throw new InvalidOperationException("ConnectionString 'HanaConn' não configurada.");
            _logger = logger;
        }

        [HttpGet("efetivar-esboco")]
        [HttpGet("efetivarnota")]
        public IActionResult InformacoesEndpointEfetivacao()
        {
            const string html = """
<!doctype html>
<html lang="pt-BR">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>Endpoint de efetivação de devolução</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 0; background: #f6f8fb; color: #1f2937; }
        main { max-width: 820px; margin: 48px auto; padding: 0 20px; }
        section { background: #fff; border: 1px solid #d9e0ea; border-radius: 8px; padding: 28px; box-shadow: 0 8px 24px rgba(15, 23, 42, .06); }
        h1 { margin: 0 0 12px; font-size: 26px; }
        p { line-height: 1.55; }
        code, pre { background: #eef2f7; border-radius: 6px; }
        code { padding: 2px 6px; }
        pre { padding: 14px; overflow-x: auto; }
        .method { display: inline-block; background: #0f766e; color: #fff; border-radius: 4px; padding: 3px 8px; font-weight: 700; }
    </style>
</head>
<body>
    <main>
        <section>
            <h1>Endpoint de efetivação de devolução de nota fiscal de saída</h1>
            <p>Este endereço é uma API usada para efetivar o esboço de devolução no SAP. A efetivação não é executada por acesso direto no navegador.</p>
            <p>Para processar a efetivação, envie uma requisição <span class="method">POST</span> para <code>/api/wms/devolucao-nf-saida/efetivar-esboco</code> com corpo JSON.</p>
            <pre>{
  "numOrdEntrada": 123456,
  "numEsboco": 123456,
  "tipo": "DEV",
  "codEmitente": "C00001",
  "valorNF": 100.00
}</pre>
            <p>O retorno da API informa <code>codigo</code>, <code>descricao</code>, dados do esboço e detalhes da nota criada quando o processamento é concluído.</p>
        </section>
    </main>
</body>
</html>
""";

            return Content(html, "text/html; charset=utf-8");
        }

        [HttpPost("efetivar-esboco")]
        [HttpPost("efetivarnota")]
        public async Task<IActionResult> EfetivarEsboco([FromBody] EfetivarEsbocoDevNfSaidaRequest request)
        {
            if (request == null)
            {
                return await ResponderComLogAsync(
                    StatusCodes.Status400BadRequest,
                    null,
                    new { codigo = "99", descricao = "JSON inválido." },
                    "ERRO - JSON inválido.");
            }

            if (request.NumEsboco <= 0 && request.NumOrdEntrada > 0)
                request.NumEsboco = ObterNumEsbocoPelaOrdemEntrada(request.NumOrdEntrada);

            if (request.NumEsboco <= 0)
            {
                var response = new
                {
                    codigo = "99",
                    descricao = "NumEsboco não informado. Para o retorno WMS, envie NUM_ORD_ENT para derivar o esboço.",
                    numOrdEntrada = request.NumOrdEntrada
                };

                return await ResponderComLogAsync(
                    StatusCodes.Status400BadRequest,
                    request,
                    response,
                    "ERRO - NumEsboco não informado.");
            }

            if (!string.IsNullOrWhiteSpace(request.Tipo) &&
                !string.Equals(request.Tipo, "DEV", StringComparison.OrdinalIgnoreCase))
            {
                return await ResponderComLogAsync(
                    StatusCodes.Status400BadRequest,
                    request,
                    new { codigo = "99", descricao = "Tipo inválido. Esperado: DEV." },
                    "ERRO - Tipo inválido. Esperado: DEV.");
            }

            try
            {
                var login = await _serviceLayerClient.LoginPadraoAsync();

                if (!login.ok)
                {
                    var response = new
                    {
                        codigo = "99",
                        descricao = "Erro ao fazer login no Service Layer.",
                        detalhe = login.error
                    };

                    return await ResponderComLogAsync(
                        StatusCodes.Status500InternalServerError,
                        request,
                        response,
                        "ERRO - Erro ao fazer login no Service Layer.");
                }

                var requestCt = HttpContext.RequestAborted;
                var draft = await _serviceLayerClient.GetDraftAsync(request.NumEsboco, requestCt);

                if (!draft.ok || string.IsNullOrWhiteSpace(draft.body))
                {
                    var response = new
                    {
                        codigo = "99",
                        descricao = $"Esboço {request.NumEsboco} não encontrado ou erro ao consultar no SAP.",
                        detalhe = draft.error,
                        retornoSap = draft.body
                    };

                    return await ResponderComLogAsync(
                        StatusCodes.Status500InternalServerError,
                        request,
                        response,
                        $"ERRO - Esboço {request.NumEsboco} não encontrado ou erro ao consultar no SAP.");
                }

                using var draftJson = JsonDocument.Parse(draft.body);
                var root = draftJson.RootElement;

                var objType = "";

                if (root.TryGetProperty("DocObjectCode", out var docObj))
                    objType = docObj.GetString() ?? "";

                if (string.IsNullOrWhiteSpace(objType) && root.TryGetProperty("DocObjectCodeEx", out var docObjEx))
                    objType = docObjEx.GetString() ?? "";

                if (objType != "oCreditNotes" && objType != "14")
                {
                    var response = new
                    {
                        codigo = "99",
                        descricao = $"O esboço {request.NumEsboco} não é de Dev. de Nota Fiscal de Saída.",
                        tipoEncontrado = objType
                    };

                    return await ResponderComLogAsync(
                        StatusCodes.Status400BadRequest,
                        request,
                        response,
                        $"ERRO - O esboço {request.NumEsboco} não é de Dev. de Nota Fiscal de Saída.");
                }

                var aplicarLotes = await _serviceLayerClient.AplicarLotesDraftAsync(
                    request.NumEsboco,
                    request.ListaItemOrdemEntrada,
                    draft.body
                );

                if (!aplicarLotes.ok)
                {
                    var response = new
                    {
                        codigo = "99",
                        descricao = "Erro ao aplicar lotes do WMS no esboço SAP antes da efetivação.",
                        detalhe = aplicarLotes.error,
                        retornoSap = aplicarLotes.body
                    };

                    return await ResponderComLogAsync(
                        StatusCodes.Status500InternalServerError,
                        request,
                        response,
                        "ERRO - Erro ao aplicar lotes do WMS no esboço SAP antes da efetivação.");
                }

                var efetivar = await _serviceLayerClient.EfetivarDraftAsync(request.NumEsboco, requestCt);

                if (!efetivar.ok)
                {
                    var response = new
                    {
                        codigo = "99",
                        descricao = "Erro ao efetivar esboço no SAP.",
                        detalhe = efetivar.error,
                        retornoSap = efetivar.body
                    };

                    return await ResponderComLogAsync(
                        StatusCodes.Status500InternalServerError,
                        request,
                        response,
                        "ERRO - Erro ao efetivar esboço no SAP.");
                }

                using var posEfetivacaoCts = CancellationTokenSource.CreateLinkedTokenSource(requestCt);
                posEfetivacaoCts.CancelAfter(TimeSpan.FromSeconds(30));

                var localizarNota = await _serviceLayerClient.LocalizarCreditNoteCriadaAsync(
                    request.NumEsboco,
                    request.CodEmitente,
                    request.ValorNF,
                    posEfetivacaoCts.Token
                );

                if (!localizarNota.ok)
                {
                    var response = new
                    {
                        codigo = "99",
                        descricao = "Esboço efetivado, porém não foi possível localizar a nota criada para copiar as referências fiscais.",
                        detalhe = localizarNota.error,
                        retornoSap = efetivar.body
                    };

                    return await ResponderComLogAsync(
                        StatusCodes.Status500InternalServerError,
                        request,
                        response,
                        "ERRO - Esboço efetivado, porém não foi possível localizar a nota criada para copiar as referências fiscais.");
                }

                var copiarReferencias = await _serviceLayerClient.CopiarReferenciasDraftParaCreditNoteAsync(
                    request.NumEsboco,
                    localizarNota.docEntryCriado,
                    posEfetivacaoCts.Token
                );

                if (!copiarReferencias.ok)
                {
                    var response = new
                    {
                        codigo = "99",
                        descricao = "Esboço efetivado, porém ocorreu erro ao copiar referências fiscais DRF21 -> RIN21.",
                        detalhe = copiarReferencias.error,
                        docEntryNotaCriada = localizarNota.docEntryCriado,
                        retornoSap = efetivar.body
                    };

                    return await ResponderComLogAsync(
                        StatusCodes.Status500InternalServerError,
                        request,
                        response,
                        "ERRO - Esboço efetivado, porém ocorreu erro ao copiar referências fiscais DRF21 -> RIN21.");
                }

                var sucesso = new
                {
                    codigo = "00",
                    descricao = "Esboço de devolução efetivado com sucesso.",
                    numEsboco = request.NumEsboco,
                    numOrdEntrada = request.NumOrdEntrada,
                    docEntryNotaCriada = localizarNota.docEntryCriado,
                    referenciasCopiadas = copiarReferencias.qtdCopiada,
                    retornoSap = efetivar.body
                };

                return await ResponderComLogAsync(
                    StatusCodes.Status200OK,
                    request,
                    sucesso,
                    "OK - Esboço de devolução efetivado com sucesso.");
            }
            catch (Exception ex)
            {
                var response = new
                {
                    codigo = "99",
                    descricao = "Erro interno ao efetivar esboço de devolução.",
                    detalhe = ex.Message
                };

                return await ResponderComLogAsync(
                    StatusCodes.Status500InternalServerError,
                    request,
                    response,
                    "ERRO - Erro interno ao efetivar esboço de devolução.");
            }
        }

        [HttpPost("reprocessar-referencias-fiscais")]
        public async Task<IActionResult> ReprocessarReferenciasFiscais([FromBody] VinculoFiscalManualRequest request)
        {
            if (request == null)
                return BadRequest(new { codigo = "99", descricao = "JSON inválido." });

            if (request.NumEsboco <= 0 || request.DocEntryNotaCriada <= 0)
            {
                return BadRequest(new
                {
                    codigo = "99",
                    descricao = "Informe NumEsboco e DocEntryNotaCriada válidos."
                });
            }

            var copiarReferencias = await _serviceLayerClient.CopiarReferenciasDraftParaCreditNoteAsync(
                request.NumEsboco,
                request.DocEntryNotaCriada
            );

            if (!copiarReferencias.ok)
            {
                return StatusCode(500, new
                {
                    codigo = "99",
                    descricao = "Erro ao reprocessar referências fiscais DRF21 -> RIN21.",
                    detalhe = copiarReferencias.error,
                    numEsboco = request.NumEsboco,
                    docEntryNotaCriada = request.DocEntryNotaCriada
                });
            }

            return Ok(new
            {
                codigo = "00",
                descricao = "Referências fiscais reprocessadas com sucesso.",
                numEsboco = request.NumEsboco,
                docEntryNotaCriada = request.DocEntryNotaCriada,
                referenciasCopiadas = copiarReferencias.qtdCopiada
            });
        }

        private static int ObterNumEsbocoPelaOrdemEntrada(long numOrdEntrada)
        {
            return (int)(numOrdEntrada % 1_000_000);
        }

        private async Task<IActionResult> ResponderComLogAsync(
            int statusCode,
            EfetivarEsbocoDevNfSaidaRequest? request,
            object response,
            string message)
        {
            await RegistrarLogRetornoSapAsync(request, response, message);
            return StatusCode(statusCode, response);
        }

        private async Task RegistrarLogRetornoSapAsync(
            EfetivarEsbocoDevNfSaidaRequest? request,
            object response,
            string message)
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
                cmd.Parameters.Add("pMethod", OdbcType.NVarChar, 300).Value = "POST /efetivarnota";
                cmd.Parameters.Add("pMessage", OdbcType.NVarChar, 5000).Value = Limitar(message, 5000);
                cmd.Parameters.Add("pKeyWms", OdbcType.NVarChar, 200).Value = request?.NumOrdEntrada > 0 ? request.NumOrdEntrada.ToString() : DBNull.Value;
                cmd.Parameters.Add("pKeySap", OdbcType.NVarChar, 200).Value = request?.NumEsboco > 0 ? request.NumEsboco.ToString() : DBNull.Value;
                cmd.Parameters.Add("pRequestJson", OdbcType.NText).Value = requestJson;
                cmd.Parameters.Add("pResponseJson", OdbcType.NText).Value = responseJson;

                await cmd.ExecuteNonQueryAsync(timeoutCts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao registrar log de retorno WMS -> SAP para efetivação de esboço.");
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
