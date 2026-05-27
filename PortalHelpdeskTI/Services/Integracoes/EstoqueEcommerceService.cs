using System.Data.Odbc;
using PortalHelpdeskTI.Models.Integracoes;

namespace PortalHelpdeskTI.Services.Integracoes
{
    public class EstoqueEcommerceService
    {
        private readonly string _connStr;

        public EstoqueEcommerceService(IConfiguration config)
        {
            // Ajuste o nome da connection string conforme seu appsettings.json
            _connStr = config.GetConnectionString("HanaConn")
                ?? throw new InvalidOperationException("ConnectionString 'HanaConn' não configurada.");
        }

        public async Task<List<EstoqueEcommerceDto>> ListarItensDivergentesAsync(string whsCode = "21")
        {
            var lista = new List<EstoqueEcommerceDto>();

            await using var conn = new OdbcConnection(_connStr);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
                                SELECT
                                    OITW.""WhsCode""                                  AS ""WhsCode"",
                                    OITM.""ItemCode""                                AS ""ItemCode"",

                                    TO_INTEGER(COALESCE(OITW.""OnHand"", 0))          AS ""OnHand"",
                                    TO_INTEGER(COALESCE(OITW.""IsCommited"", 0))      AS ""IsCommited"",
                                    TO_INTEGER(
                                        COALESCE(OITW.""OnHand"", 0) - COALESCE(OITW.""IsCommited"", 0)
                                    )                                              AS ""AvailableQuantity"",

                                    TO_INTEGER(COALESCE(OITW.""U_AS_SALDO"", 0))      AS ""LastSentStock""
                                FROM ""OITM"" OITM
                                INNER JOIN ""OITW"" OITW
                                    ON OITM.""ItemCode"" = OITW.""ItemCode""
                                WHERE
                                    OITW.""WhsCode"" = ?
                                    AND OITM.""ItemType"" = 'I'
                                    AND OITM.""validFor"" = 'Y'
                                    AND TO_INTEGER(
                                        COALESCE(OITW.""OnHand"", 0) - COALESCE(OITW.""IsCommited"", 0)
                                    ) <> TO_INTEGER(COALESCE(OITW.""U_AS_SALDO"", 0))
                                    AND COALESCE(OITW.""U_AS_SALDO"", 0) <> 9999
                                ";

            cmd.Parameters.Add("@WhsCode", OdbcType.VarChar).Value = whsCode;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var dto = new EstoqueEcommerceDto
                {
                    ItemCode = rd["ItemCode"]?.ToString() ?? "",
                    WhsCode = rd["WhsCode"]?.ToString() ?? "",
                    OnHand = rd["OnHand"] == DBNull.Value ? 0 : Convert.ToInt32(rd["OnHand"]),
                    IsCommited = rd["IsCommited"] == DBNull.Value ? 0 : Convert.ToInt32(rd["IsCommited"]),
                    AvailableQuantity = rd["AvailableQuantity"] == DBNull.Value ? 0 : Convert.ToInt32(rd["AvailableQuantity"]),
                    LastSentStock = rd["LastSentStock"] == DBNull.Value ? 0 : Convert.ToInt32(rd["LastSentStock"])
                };

                lista.Add(dto);
            }

            return lista;
        }

        public async Task<List<EstoqueEcommerceMultiDepositoDto>> ListarItensDivergentesDoisDepositosAsync(
            string whsCode = "31")
        {
            var rows = new List<(string ItemCode, string WhsCode, int OnHand, int IsCommited, int Available, int LastSent)>();

            await using var conn = new OdbcConnection(_connStr);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            WITH ITENS_DIVERGENTES AS (
                SELECT DISTINCT W0.""ItemCode""
                FROM ""OITW"" W0
                INNER JOIN ""OITM"" I0
                    ON I0.""ItemCode"" = W0.""ItemCode""
                WHERE W0.""WhsCode"" = ?
                  AND I0.""ItemType"" = 'I'
                  AND I0.""validFor"" = 'Y'
                  AND TO_INTEGER(COALESCE(W0.""OnHand"", 0) - COALESCE(W0.""IsCommited"", 0))
                      <> TO_INTEGER(COALESCE(W0.""U_AS_SALDO"", 0))
                  AND COALESCE(W0.""U_AS_SALDO"", 0) <> 9999
            )
            SELECT
                W.""ItemCode""                                  AS ""ItemCode"",
                W.""WhsCode""                                   AS ""WhsCode"",
                TO_INTEGER(COALESCE(W.""OnHand"", 0))            AS ""OnHand"",
                TO_INTEGER(COALESCE(W.""IsCommited"", 0))        AS ""IsCommited"",
                TO_INTEGER(COALESCE(W.""OnHand"", 0) - COALESCE(W.""IsCommited"", 0)) AS ""AvailableQuantity"",
                TO_INTEGER(COALESCE(W.""U_AS_SALDO"", 0))        AS ""LastSentStock""
            FROM ""OITW"" W
            INNER JOIN ITENS_DIVERGENTES D ON D.""ItemCode"" = W.""ItemCode""
            WHERE W.""WhsCode"" = ?
            ORDER BY W.""ItemCode"", W.""WhsCode"";
            ";

            cmd.Parameters.Add("@WhsCodeCte", OdbcType.VarChar).Value = whsCode;
            cmd.Parameters.Add("@WhsCodeFinal", OdbcType.VarChar).Value = whsCode;

            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                rows.Add((
                    ItemCode: rd["ItemCode"]?.ToString() ?? "",
                    WhsCode: rd["WhsCode"]?.ToString() ?? "",
                    OnHand: rd["OnHand"] == DBNull.Value ? 0 : Convert.ToInt32(rd["OnHand"]),
                    IsCommited: rd["IsCommited"] == DBNull.Value ? 0 : Convert.ToInt32(rd["IsCommited"]),
                    Available: rd["AvailableQuantity"] == DBNull.Value ? 0 : Convert.ToInt32(rd["AvailableQuantity"]),
                    LastSent: rd["LastSentStock"] == DBNull.Value ? 0 : Convert.ToInt32(rd["LastSentStock"])
                ));
            }

            // Agrupa por item e organiza os dois depósitos dentro
            var result = rows
                .GroupBy(r => r.ItemCode)
                .Select(g => new EstoqueEcommerceMultiDepositoDto
                {
                    ItemCode = g.Key,
                    Depositos = g.Select(x => new EstoqueEcommercePorDepositoDto
                    {
                        WhsCode = x.WhsCode,
                        OnHand = x.OnHand,
                        IsCommited = x.IsCommited,
                        AvailableQuantity = x.Available,
                        LastSentStock = x.LastSent
                    })
                    .OrderBy(x => x.WhsCode)
                    .ToList()
                })
                .ToList();

            return result;
        }

    }
}
