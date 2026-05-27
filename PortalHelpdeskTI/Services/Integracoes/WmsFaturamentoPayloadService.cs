using System.Data.Odbc;
using PortalHelpdeskTI.Models.Integracoes;

namespace PortalHelpdeskTI.Services.Integracoes
{
    public class WmsFaturamentoPayloadService
    {
        private readonly string _hanaConn;

        public WmsFaturamentoPayloadService(IConfiguration config)
        {
            _hanaConn = config.GetConnectionString("HanaConn")
                ?? throw new Exception("ConnectionString HanaConn nao configurada.");
        }

        public async Task<WmsEnviarFaturamentoRequest> MontarPorPedidoAsync(
            int? docEntryPedido,
            int? docNumPedido,
            CancellationToken ct = default)
        {
            if ((docEntryPedido ?? 0) <= 0 && (docNumPedido ?? 0) <= 0)
                throw new ArgumentException("Informe o DocEntry ou o DocNum do pedido.");

            using var conn = new OdbcConnection(_hanaConn);
            await conn.OpenAsync(ct);

            var pedidoDocEntry = await ResolverPedidoDocEntryAsync(conn, docEntryPedido, docNumPedido, ct);
            if (pedidoDocEntry <= 0)
                throw new InvalidOperationException("Pedido de venda nao localizado.");

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT TOP 1
                    I.""DocEntry"",
                    I.""DocNum"",
                    IFNULL(I.""Serial"", 0) AS ""NumNf"",
                    IFNULL(I.""DocTotal"", 0) AS ""ValorNF"",
                    IFNULL(I.""SeriesStr"", '') AS ""SerieNF"",
                    TO_NVARCHAR(I.""DocDate"", 'DD/MM/YYYY') AS ""DataFaturamento"",
                    IFNULL(
                        NULLIF(P.""KeyNfe"", ''),
                        IFNULL(
                            NULLIF(H.""KeyNfe"", ''),
                            IFNULL(NULLIF(I.""U_ChaveAcesso"", ''), '')
                        )
                    ) AS ""ChaveNf"",
                    I.""DocNum"" AS ""NumeroDoc""
                FROM OINV I
                INNER JOIN INV1 L
                        ON L.""DocEntry"" = I.""DocEntry""
                LEFT JOIN (
                    SELECT
                        P0.""DocEntry"",
                        MAX(P0.""KeyNfe"") AS ""KeyNfe"",
                        MAX(P0.""BatchId"") AS ""BatchId""
                    FROM ""DBInvOne"".""Process"" P0
                    WHERE P0.""DocType"" = 13
                    GROUP BY P0.""DocEntry""
                ) P
                    ON P.""DocEntry"" = I.""DocEntry""
                LEFT JOIN (
                    SELECT
                        H0.""BatchId"",
                        MAX(H0.""KeyNfe"") AS ""KeyNfe""
                    FROM ""DBInvOne"".""ProcessHist"" H0
                    WHERE H0.""StatusId"" = 4
                      AND H0.""ReturnId"" = 100
                      AND IFNULL(H0.""KeyNfe"", '') <> ''
                    GROUP BY H0.""BatchId""
                ) H
                    ON H.""BatchId"" = P.""BatchId""
                WHERE L.""BaseType"" = 17
                  AND L.""BaseEntry"" = ?
                  AND IFNULL(I.""CANCELED"", 'N') = 'N'
                ORDER BY I.""DocEntry"" DESC";
            cmd.Parameters.Add("@pedidoDocEntry", OdbcType.Int).Value = pedidoDocEntry;

            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new InvalidOperationException($"Nenhuma nota fiscal de saida gerada foi localizada para o pedido DocEntry {pedidoDocEntry}.");

            var chaveNf = reader["ChaveNf"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(chaveNf))
                throw new InvalidOperationException($"A nota {reader["NumNf"]} foi localizada, mas a chave NF ainda nao esta disponivel.");

            return new WmsEnviarFaturamentoRequest
            {
                numOrdSaida = pedidoDocEntry,
                numNf = Convert.ToInt32(reader["NumNf"]),
                valorNF = Convert.ToDecimal(reader["ValorNF"]),
                serieNF = reader["SerieNF"]?.ToString() ?? "",
                datafaturamento = reader["DataFaturamento"]?.ToString() ?? "",
                chavenf = chaveNf,
                numeroDoc = Convert.ToInt32(reader["NumeroDoc"])
            };
        }

        public WmsEnviarFaturamentoRequest MontarDireto(WmsEnviarFaturamentoManualRequest request)
        {
            return new WmsEnviarFaturamentoRequest
            {
                numOrdSaida = request.numOrdSaida,
                numNf = request.numNf,
                valorNF = request.valorNF,
                serieNF = request.serieNF,
                datafaturamento = request.datafaturamento,
                chavenf = request.chavenf,
                numeroDoc = request.numeroDoc
            };
        }

        public bool DeveMontarPorPedido(WmsEnviarFaturamentoManualRequest request)
        {
            return (request.docEntryPedido ?? 0) > 0 || (request.docNumPedido ?? 0) > 0;
        }

        private static async Task<int> ResolverPedidoDocEntryAsync(
            OdbcConnection conn,
            int? docEntryPedido,
            int? docNumPedido,
            CancellationToken ct)
        {
            if ((docEntryPedido ?? 0) > 0)
                return docEntryPedido!.Value;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT TOP 1 ""DocEntry""
                FROM ORDR
                WHERE ""DocNum"" = ?
                ORDER BY ""DocEntry"" DESC";
            cmd.Parameters.Add("@docNumPedido", OdbcType.Int).Value = docNumPedido!.Value;

            var result = await cmd.ExecuteScalarAsync(ct);
            return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
        }
    }
}
