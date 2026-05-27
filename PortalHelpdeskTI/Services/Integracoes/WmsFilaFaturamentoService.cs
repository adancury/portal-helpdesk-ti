using System.Data.Odbc;
using PortalHelpdeskTI.ViewModels.Integracoes;

namespace PortalHelpdeskTI.Services.Integracoes
{
    public class WmsFilaFaturamentoService
    {
        private readonly string _hanaConn;

        public WmsFilaFaturamentoService(IConfiguration config)
        {
            _hanaConn = config.GetConnectionString("HanaConn")
                ?? throw new Exception("ConnectionString HanaConn não configurada.");
        }

        public async Task<List<WmsFilaFaturamentoViewModel>> BuscarPendentesAsync()
        {
            var lista = new List<WmsFilaFaturamentoViewModel>();

            using var conn = new OdbcConnection(_hanaConn);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            SELECT
                F.""Code"",
                IFNULL(F.""U_DocEntry"", 0) AS ""DocEntry"",
                IFNULL(I.""DocNum"", IFNULL(F.""U_DocNum"", 0)) AS ""DocNum"",
                IFNULL(F.""U_TipoEvento"", '') AS ""TipoEvento"",
                IFNULL((
                    SELECT MIN(L1.""BaseEntry"")
                    FROM INV1 L1
                    WHERE L1.""DocEntry"" = F.""U_DocEntry""
                      AND L1.""BaseType"" = 17
                ), IFNULL(F.""U_NumOrdSaida"", 0)) AS ""NumOrdSaida"",
                IFNULL(I.""Serial"", IFNULL(F.""U_NumNf"", 0)) AS ""NumNf"",
                IFNULL(I.""DocTotal"", IFNULL(F.""U_ValorNF"", 0)) AS ""ValorNF"",
                IFNULL(I.""SeriesStr"", IFNULL(F.""U_SerieNF"", '')) AS ""SerieNF"",
                IFNULL(TO_NVARCHAR(I.""DocDate"", 'DD/MM/YYYY'), IFNULL(F.""U_DataFaturamento"", '')) AS ""DataFaturamento"",
                IFNULL(
                    NULLIF(P.""KeyNfe"", ''),
                    IFNULL(
                        NULLIF(H.""KeyNfe"", ''),
                        IFNULL(
                            NULLIF(I.""U_ChaveAcesso"", ''),
                            IFNULL(F.""U_ChaveNf"", '')
                        )
                    )
                ) AS ""ChaveNf"",
                IFNULL(I.""DocNum"", IFNULL(F.""U_NumeroDoc"", 0)) AS ""NumeroDoc"",
                IFNULL(F.""U_Tentativas"", 0) AS ""Tentativas"",
                IFNULL(F.""U_Status"", '') AS ""Status""
            FROM ""SBO_BRW_PRD"".""@BRW_WMS_FILA_FAT"" F
            LEFT JOIN OINV I
                   ON I.""DocEntry"" = F.""U_DocEntry""
            LEFT JOIN (
                SELECT
                    P0.""DocEntry"",
                    MAX(P0.""KeyNfe"") AS ""KeyNfe"",
                    MAX(P0.""BatchId"") AS ""BatchId""
                FROM ""DBInvOne"".""Process"" P0
                WHERE P0.""DocType"" = 13
                GROUP BY P0.""DocEntry""
            ) P
                ON P.""DocEntry"" = F.""U_DocEntry""
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
            WHERE F.""U_Status"" = 'PENDENTE'
              AND IFNULL(I.""CANCELED"", 'N') = 'N'
            ORDER BY F.""U_DataInclusao""";

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new WmsFilaFaturamentoViewModel
                {
                    Code = reader["Code"]?.ToString() ?? "",
                    DocEntry = Convert.ToInt32(reader["DocEntry"]),
                    DocNum = Convert.ToInt32(reader["DocNum"]),
                    TipoEvento = reader["TipoEvento"]?.ToString() ?? "",
                    NumOrdSaida = Convert.ToInt32(reader["NumOrdSaida"]),
                    NumNf = Convert.ToInt32(reader["NumNf"]),
                    ValorNF = Convert.ToDecimal(reader["ValorNF"]),
                    SerieNF = reader["SerieNF"]?.ToString() ?? "",
                    DataFaturamento = reader["DataFaturamento"]?.ToString() ?? "",
                    ChaveNf = reader["ChaveNf"]?.ToString() ?? "",
                    NumeroDoc = Convert.ToInt32(reader["NumeroDoc"]),
                    Tentativas = Convert.ToInt32(reader["Tentativas"]),
                    Status = reader["Status"]?.ToString() ?? ""
                });
            }

            return lista;
        }

        public async Task MarcarProcessadoAsync(string code, string payload, string retorno)
        {
            using var conn = new OdbcConnection(_hanaConn);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE ""SBO_BRW_PRD"".""@BRW_WMS_FILA_FAT""
                SET
                    ""U_Status"" = 'PROCESSADO',
                    ""U_PayloadJson"" = ?,
                    ""U_RetornoDescricao"" = ?,
                    ""U_Erro"" = NULL,
                    ""U_DataProcessamento"" = CURRENT_TIMESTAMP
                WHERE ""Code"" = ?";

            cmd.Parameters.Add("@payload", OdbcType.NVarChar).Value = payload;
            cmd.Parameters.Add("@retorno", OdbcType.NVarChar).Value = retorno;
            cmd.Parameters.Add("@code", OdbcType.NVarChar).Value = code;

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task MarcarErroAsync(string code, string erro)
        {
            using var conn = new OdbcConnection(_hanaConn);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE ""SBO_BRW_PRD"".""@BRW_WMS_FILA_FAT""
                SET
                    ""U_Status"" = 'ERRO',
                    ""U_Erro"" = ?,
                    ""U_DataProcessamento"" = CURRENT_TIMESTAMP
                WHERE ""Code"" = ?";

            cmd.Parameters.Add("@erro", OdbcType.NVarChar).Value = erro;
            cmd.Parameters.Add("@code", OdbcType.NVarChar).Value = code;

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task ManterPendenteAsync(string code, string mensagem)
        {
            using var conn = new OdbcConnection(_hanaConn);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE ""SBO_BRW_PRD"".""@BRW_WMS_FILA_FAT""
                SET
                    ""U_Status"" = 'PENDENTE',
                    ""U_Erro"" = ?,
                    ""U_Tentativas"" = IFNULL(""U_Tentativas"", 0) + 1,
                    ""U_DataProcessamento"" = CURRENT_TIMESTAMP
                WHERE ""Code"" = ?";

            cmd.Parameters.Add("@mensagem", OdbcType.NVarChar).Value = mensagem;
            cmd.Parameters.Add("@code", OdbcType.NVarChar).Value = code;

            await cmd.ExecuteNonQueryAsync();
        }
    }
}
