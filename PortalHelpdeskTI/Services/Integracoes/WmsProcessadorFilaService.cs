using System.Text.Json;
using PortalHelpdeskTI.Models.Integracoes;

namespace PortalHelpdeskTI.Services.Integracoes
{
    public class WmsProcessadorFilaService
    {
        private readonly WmsFilaFaturamentoService _fila;
        private readonly WmsApiService _api;
        private static readonly SemaphoreSlim _lock = new(1, 1);

        public WmsProcessadorFilaService(
            WmsFilaFaturamentoService fila,
            WmsApiService api)
        {
            _fila = fila;
            _api = api;
        }

        public async Task ProcessarAsync()
        {
            if (!await _lock.WaitAsync(0))
                return;

            try
            {
                var pendentes = await _fila.BuscarPendentesAsync();

                foreach (var item in pendentes)
                {
                    try
                    {
                        if (string.Equals(item.TipoEvento, "STATUS_PEDIDO_FATURADO", StringComparison.OrdinalIgnoreCase))
                        {
                            if (item.NumOrdSaida <= 0)
                            {
                                await _fila.MarcarErroAsync(
                                    item.Code,
                                    $"Pedido de venda não localizado para a nota DocEntry {item.DocEntry}.");
                                continue;
                            }

                            var payloadStatus = new WmsAtualizarIndicadorPedidoVendaRequest
                            {
                                DocEntry = item.NumOrdSaida,
                                Status = "10"
                            };

                            var retornoStatus = await _api.AtualizarIndicadorPedidoVendaAsync(payloadStatus);
                            var payloadStatusJson = JsonSerializer.Serialize(payloadStatus);

                            await _fila.MarcarProcessadoAsync(
                                item.Code,
                                payloadStatusJson,
                                string.IsNullOrWhiteSpace(retornoStatus) ? "OK" : retornoStatus);

                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(item.ChaveNf))
                        {
                            await _fila.ManterPendenteAsync(
                                item.Code,
                                $"Chave NF ainda não disponível para a nota {item.NumNf} / DocEntry {item.DocEntry}.");
                            continue;
                        }

                        var payload = new WmsEnviarFaturamentoRequest
                        {
                            numOrdSaida = item.NumOrdSaida,
                            numNf = item.NumNf,
                            valorNF = item.ValorNF,
                            serieNF = item.SerieNF,
                            datafaturamento = item.DataFaturamento,
                            chavenf = item.ChaveNf,
                            numeroDoc = item.NumeroDoc
                        };

                        var retorno = await _api.EnviarAsync(payload);
                        var payloadJson = JsonSerializer.Serialize(payload);

                        await _fila.MarcarProcessadoAsync(
                            item.Code,
                            payloadJson,
                            retorno?.descricao ?? "OK");
                    }
                    catch (Exception ex)
                    {
                        if (string.Equals(item.TipoEvento, "STATUS_PEDIDO_FATURADO", StringComparison.OrdinalIgnoreCase) &&
                            EhErroTransitorioSap(ex.Message))
                        {
                            await _fila.ManterPendenteAsync(item.Code, ex.Message);
                            continue;
                        }

                        await _fila.MarcarErroAsync(item.Code, ex.Message);
                    }
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        private static bool EhErroTransitorioSap(string mensagem)
        {
            return mensagem.Contains("ODBC -2039", StringComparison.OrdinalIgnoreCase) ||
                   mensagem.Contains("Another user or another operation modified data", StringComparison.OrdinalIgnoreCase);
        }
    }
}
