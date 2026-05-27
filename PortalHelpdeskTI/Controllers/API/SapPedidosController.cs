using Microsoft.AspNetCore.Mvc;
using PortalHelpdeskTI.Services.SAP;

namespace PortalHelpdeskTI.Controllers.Api
{
    [ApiController]
    [Route("api/sap/pedidos")]
    public sealed class SapPedidosController : ControllerBase
    {
        private readonly ServiceLayerClient _sl;

        public SapPedidosController(ServiceLayerClient sl)
        {
            _sl = sl;
        }

        public sealed class AtualizarIndicadorPedidoDto
        {
            public int DocEntry { get; set; }
            public string Status { get; set; } = "";
        }

        [HttpGet("~/api/wms/atualizaIndicadorPedidoVenda")]
        public IActionResult InformacoesEndpointAtualizaIndicadorPedidoVenda()
        {
            const string html = """
<!doctype html>
<html lang="pt-BR">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>Endpoint de atualização de indicador de pedido de venda</title>
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
            <h1>Endpoint de atualização de indicador de pedido de venda</h1>
            <p>Este endereço é uma API usada para atualizar o campo de indicador do pedido de venda no SAP. A atualização não é executada por acesso direto no navegador.</p>
            <p>Para processar a atualização, envie uma requisição <span class="method">POST</span> para <code>/api/wms/atualizaIndicadorPedidoVenda</code> com corpo JSON.</p>
            <pre>{
  "docEntry": 123456,
  "status": "SEPARADO"
}</pre>
            <p>O campo <code>docEntry</code> identifica o pedido de venda no SAP. O campo <code>status</code> é gravado no UDF <code>U_CVA_Indicador</code>.</p>
            <p>Também existe o endpoint equivalente <code>/api/sap/pedidos/indicador</code>, mantido para integrações internas.</p>
        </section>
    </main>
</body>
</html>
""";

            return Content(html, "text/html; charset=utf-8");
        }

        // POST api/wms/atualizaIndicadorPedidoVenda
        [HttpPost("~/api/wms/atualizaIndicadorPedidoVenda")]
        public Task<IActionResult> AtualizaIndicadorPedidoVenda(
            [FromBody] AtualizarIndicadorPedidoDto body,
            CancellationToken ct)
        {
            return AtualizarIndicador(body, ct);
        }

        // POST api/sap/pedidos/indicador
        [HttpPost("indicador")]
        public async Task<IActionResult> AtualizarIndicador(
            [FromBody] AtualizarIndicadorPedidoDto body,
            CancellationToken ct)
        {
            if (body is null)
                return BadRequest(new { ok = false, message = "Body obrigatório." });

            var (ok, err) = await _sl.PatchOrderIndicadorAutoLoginAsync(body.DocEntry, body.Status, ct);

            if (!ok)
                return BadRequest(new { ok = false, message = err, docEntry = body.DocEntry });

            return Ok(new { ok = true, docEntry = body.DocEntry, status = body.Status });
        }
    }
}
