using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using PortalHelpdeskTI.Services.Integracoes;
using System.IO;
using PortalHelpdeskTI.Models;
using System.Text.Json;

namespace PortalHelpdeskTI.Controllers
{
    public class IntegracoesNotasController : Controller
    {
        private readonly IntegracoesNotasService _service;

        public IntegracoesNotasController(IntegracoesNotasService service)
        {
            _service = service;
        }

        public async Task<IActionResult> Index(
            DateTime? dataIni,
            DateTime? dataFim,
            string? statusEnvio,
            string? docNum,
            string? cardCode,
            string? cardName,
            string? emailCliente,
            string? chaveNf,
            bool somenteComErro = false,
            int pagina = 1,
            int tamanhoPagina = 50)
        {
            if (pagina < 1)
                pagina = 1;

            if (tamanhoPagina <= 0)
                tamanhoPagina = 50;

            var (itens, total) = await _service.BuscarAsync(
                dataIni,
                dataFim,
                statusEnvio,
                docNum,
                cardCode,
                cardName,
                emailCliente,
                chaveNf,
                somenteComErro,
                pagina,
                tamanhoPagina);

            ViewBag.DataIni = dataIni;
            ViewBag.DataFim = dataFim;
            ViewBag.StatusEnvio = statusEnvio;
            ViewBag.DocNum = docNum;
            ViewBag.CardCode = cardCode;
            ViewBag.CardName = cardName;
            ViewBag.EmailCliente = emailCliente;
            ViewBag.ChaveNf = chaveNf;
            ViewBag.SomenteComErro = somenteComErro;

            ViewBag.Pagina = pagina;
            ViewBag.TamanhoPagina = tamanhoPagina;
            ViewBag.Total = total;
            ViewBag.TotalPaginas = (int)Math.Ceiling(total / (double)tamanhoPagina);

            return View(itens);
        }

        [HttpGet]
        public async Task<IActionResult> ExportarExcel(
            DateTime? dataIni,
            DateTime? dataFim,
            string? statusEnvio,
            string? docNum,
            string? cardCode,
            string? cardName,
            string? emailCliente,
            string? chaveNf,
            bool somenteComErro = false)
        {
            var itens = await _service.BuscarParaExportacaoAsync(
                dataIni,
                dataFim,
                statusEnvio,
                docNum,
                cardCode,
                cardName,
                emailCliente,
                chaveNf,
                somenteComErro);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("IntegracoesNotas");

            var linha = 1;

            ws.Cell(linha, 1).Value = "Envio";
            ws.Cell(linha, 2).Value = "Status Envio";
            ws.Cell(linha, 3).Value = "Erro Envio";
            ws.Cell(linha, 4).Value = "DocEntry";
            ws.Cell(linha, 5).Value = "DocNum";
            ws.Cell(linha, 6).Value = "NF";
            ws.Cell(linha, 7).Value = "Série";
            ws.Cell(linha, 8).Value = "Cliente";
            ws.Cell(linha, 9).Value = "Nome Cliente";
            ws.Cell(linha, 10).Value = "E-mail Cliente";
            ws.Cell(linha, 11).Value = "Data Documento";
            ws.Cell(linha, 12).Value = "Data Lançamento";
            ws.Cell(linha, 13).Value = "Data Vencimento";
            ws.Cell(linha, 14).Value = "Valor Nota";
            ws.Cell(linha, 15).Value = "Chave NF-e";
            ws.Cell(linha, 16).Value = "Status Documento";
            ws.Cell(linha, 17).Value = "Observações";
            ws.Cell(linha, 18).Value = "Ref. Cliente";

            var header = ws.Range(linha, 1, linha, 18);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.LightGray;

            linha++;

            foreach (var item in itens)
            {
                ws.Cell(linha, 1).Value = item.SentAt;
                ws.Cell(linha, 1).Style.DateFormat.Format = "dd/MM/yyyy HH:mm:ss";

                ws.Cell(linha, 2).Value = item.StatusEnvio;
                ws.Cell(linha, 3).Value = item.ErroEnvio;
                ws.Cell(linha, 4).Value = item.DocEntry;
                ws.Cell(linha, 5).Value = item.DocNum;
                ws.Cell(linha, 6).Value = item.NumNf;
                ws.Cell(linha, 7).Value = item.SerieNF;
                ws.Cell(linha, 8).Value = item.CardCode;
                ws.Cell(linha, 9).Value = item.CardName;
                ws.Cell(linha, 10).Value = item.EmailCliente;

                ws.Cell(linha, 11).Value = item.DataDocumento;
                ws.Cell(linha, 11).Style.DateFormat.Format = "dd/MM/yyyy";

                ws.Cell(linha, 12).Value = item.DataLancamento;
                ws.Cell(linha, 12).Style.DateFormat.Format = "dd/MM/yyyy";

                ws.Cell(linha, 13).Value = item.DataVencimento;
                ws.Cell(linha, 13).Style.DateFormat.Format = "dd/MM/yyyy";

                ws.Cell(linha, 14).Value = item.ValorNota;
                ws.Cell(linha, 14).Style.NumberFormat.Format = "#,##0.00";

                ws.Cell(linha, 15).Value = item.ChaveNf;
                ws.Cell(linha, 16).Value = item.StatusDocumento;
                ws.Cell(linha, 17).Value = item.Observacoes;
                ws.Cell(linha, 18).Value = item.RefCliente;

                var linhaErro =
                    string.Equals(item.StatusEnvio, "Erro", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.StatusEnvio, "E", StringComparison.OrdinalIgnoreCase) ||
                    !string.IsNullOrWhiteSpace(item.ErroEnvio);

                if (linhaErro)
                {
                    ws.Range(linha, 1, linha, 18).Style.Fill.BackgroundColor = XLColor.LightPink;
                }

                linha++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;

            var nomeArquivo = $"IntegracoesNotas_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                nomeArquivo);
        }

        [HttpPost("reprocessar-vinculo-fiscal")]
        public async Task<IActionResult> ReprocessarVinculoFiscal(
        [FromBody] JsonElement body)
        {
            var req = new VinculoFiscalManualRequest();
            var numOrdEntrada = GetLong(body, "NUM_ORD_ENT");

            req.NumEsboco = GetInt(body, "NumEsboco");
            req.DocEntryNotaCriada = GetInt(body, "DocEntryNotaCriada");

            if (req.NumEsboco <= 0)
                req.NumEsboco = GetInt(body, "NUM_ESBOCO");

            if (req.DocEntryNotaCriada <= 0)
                req.DocEntryNotaCriada = GetInt(body, "DOCENTRY_NOTA_CRIADA");

            if ((req.NumEsboco <= 0 || req.DocEntryNotaCriada <= 0) && numOrdEntrada > 0)
            {
                var vinculo = await _service.LocalizarVinculoFiscalPorOrdemEntradaAsync(numOrdEntrada);

                if (!vinculo.ok)
                {
                    return BadRequest(new
                    {
                        codigo = "99",
                        descricao = "Retorno WMS recebido, mas não foi possível localizar o vínculo fiscal para reprocessamento.",
                        detalhe = vinculo.error,
                        numOrdEntrada,
                        numEsboco = vinculo.numEsboco,
                        docEntryNotaCriada = vinculo.docEntryNotaCriada
                    });
                }

                req.NumEsboco = vinculo.numEsboco;
                req.DocEntryNotaCriada = vinculo.docEntryNotaCriada;
            }

            if (req.NumEsboco <= 0 || req.DocEntryNotaCriada <= 0)
            {
                return BadRequest(new
                {
                    codigo = "99",
                    descricao = "Informe NumEsboco e DocEntryNotaCriada válidos, ou envie NUM_ORD_ENT para localizar o vínculo pelos logs WMS."
                });
            }

            var ret = await _service.CopiarReferenciasFiscaisAsync(
                req.NumEsboco,
                req.DocEntryNotaCriada
            );

            if (!ret.ok)
            {
                return BadRequest(new
                {
                    codigo = "99",
                    descricao = "Erro ao reprocessar vínculo fiscal.",
                    detalhe = ret.error,
                    numEsboco = req.NumEsboco,
                    docEntryNotaCriada = req.DocEntryNotaCriada
                });
            }

            return Ok(new
            {
                codigo = "00",
                descricao = "Vínculo fiscal reprocessado com sucesso.",
                numOrdEntrada = numOrdEntrada > 0 ? (long?)numOrdEntrada : null,
                numEsboco = req.NumEsboco,
                docEntryNotaCriada = req.DocEntryNotaCriada
            });
        }

        [HttpGet]
        public IActionResult VinculoFiscal()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VinculoFiscal(VinculoFiscalManualRequest req)
        {
            if (req.NumEsboco <= 0 || req.DocEntryNotaCriada <= 0)
            {
                ViewBag.Erro = "Informe o número do esboço e o DocEntry da nota criada.";
                return View(req);
            }

            var ret = await _service.CopiarReferenciasFiscaisAsync(
                req.NumEsboco,
                req.DocEntryNotaCriada
            );

            if (!ret.ok)
            {
                ViewBag.Erro = ret.error;
                return View(req);
            }

            ViewBag.Sucesso = "Vínculo fiscal reprocessado com sucesso.";
            return View(req);
        }

        private static int GetInt(JsonElement body, string propertyName)
        {
            if (!TryGetProperty(body, propertyName, out var value))
                return 0;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
                return number;

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
                return number;

            return 0;
        }

        private static long GetLong(JsonElement body, string propertyName)
        {
            if (!TryGetProperty(body, propertyName, out var value))
                return 0;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
                return number;

            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number))
                return number;

            return 0;
        }

        private static bool TryGetProperty(JsonElement body, string propertyName, out JsonElement value)
        {
            value = default;

            if (body.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var property in body.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            return false;
        }
    }
}
