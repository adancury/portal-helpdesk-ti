using PortalHelpdeskTI.Models.Relatorios;
using System.Data.Common;
using System.Data.Odbc;

namespace PortalHelpdeskTI.Services
{
    public class PrevisaoRupturaService
    {
        private readonly string _connStr;

        public PrevisaoRupturaService(IConfiguration cfg)
        {
            _connStr = cfg.GetConnectionString("HanaConn")
                       ?? throw new InvalidOperationException("ConnectionString 'HanaConn' não configurada.");
        }

        public async Task<PrevisaoRupturaPage> BuscarAsync(
            string? itemFiltro,
            string? depositoFiltro,
            string? riscoFiltro,
            int? diasMax)
        {
            var page = new PrevisaoRupturaPage
            {
                ItemFiltro = itemFiltro,
                RiscoFiltro = riscoFiltro,
                DiasMax = diasMax
            };

            var sql = @"
WITH FaturamentoItem AS (
    SELECT
        L.""ItemCode"",
        SUM(L.""LineTotal"") AS ""Faturamento12Meses""
    FROM ""OINV"" H
    INNER JOIN ""INV1"" L
        ON L.""DocEntry"" = H.""DocEntry""
    INNER JOIN ""INV12"" U
        ON U.""DocEntry"" = H.""DocEntry""
    WHERE H.""CANCELED"" = 'N'
      AND U.""MainUsage"" IN (10, 13, 22)
      AND H.""DocDate"" >= ADD_MONTHS(CURRENT_DATE, -12)
      AND L.""ItemCode"" IS NOT NULL
    GROUP BY L.""ItemCode""

    UNION ALL

    SELECT
        L.""ItemCode"",
        -SUM(L.""LineTotal"") AS ""Faturamento12Meses""
    FROM ""ORIN"" H
    INNER JOIN ""RIN1"" L
        ON L.""DocEntry"" = H.""DocEntry""
    INNER JOIN ""RIN12"" U
        ON U.""DocEntry"" = H.""DocEntry""
    WHERE H.""CANCELED"" = 'N'
      AND U.""MainUsage"" IN (10, 13, 22)
      AND H.""DocDate"" >= ADD_MONTHS(CURRENT_DATE, -12)
      AND L.""ItemCode"" IS NOT NULL
    GROUP BY L.""ItemCode""
),
FaturamentoAgrupado AS (
    SELECT
        ""ItemCode"",
        SUM(""Faturamento12Meses"") AS ""Faturamento12Meses""
    FROM FaturamentoItem
    GROUP BY ""ItemCode""
),
CurvaBase AS (
    SELECT
        ""ItemCode"",
        ""Faturamento12Meses"",
        SUM(""Faturamento12Meses"") OVER () AS ""FaturamentoTotal""
    FROM FaturamentoAgrupado
    WHERE ""Faturamento12Meses"" > 0
),
CurvaAcumulada AS (
    SELECT
        ""ItemCode"",
        ""Faturamento12Meses"",
        CASE
            WHEN ""FaturamentoTotal"" <= 0 THEN 100
            ELSE
                SUM(""Faturamento12Meses"") OVER (
                    ORDER BY ""Faturamento12Meses"" DESC, ""ItemCode""
                    ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
                ) / ""FaturamentoTotal"" * 100
        END AS ""PercentualAcumulado""
    FROM CurvaBase
),
CurvaAbc AS (
    SELECT
        ""ItemCode"",
        ""Faturamento12Meses"",
        CASE
            WHEN ""PercentualAcumulado"" <= 50 THEN 'A'
            WHEN ""PercentualAcumulado"" <= 80 THEN 'B'
            ELSE 'C'
        END AS ""CurvaAbc""
    FROM CurvaAcumulada
)
SELECT
    V.""ItemCode"",
    V.""ItemName"",
    CASE CAST(I.""U_CbxForaLinha"" AS NVARCHAR(10))
        WHEN '0' THEN 'Não Promocionar'
        WHEN '1' THEN 'Não'
        WHEN '2' THEN 'Sim Promocionar'
        ELSE 'Não informado'
    END AS ""ForaLinhaStatus"",
    COALESCE(ABC.""CurvaAbc"", 'C') AS ""CurvaAbc"",
    COALESCE(ABC.""Faturamento12Meses"", 0) AS ""Faturamento12Meses"",
    V.""EstoqueAtual"",
    V.""EmTransito"",
    V.""DataProximaCompra"",
    V.""Comprometido"",

    V.""ConsumoHistoricoDia"",
    V.""ConsumoIaDia"",
    V.""ConsumoMedioDia"",

    V.""LeadTimeDias"",

    V.""EstoqueProjetado"",
    V.""EstoqueAtual"",

    V.""DiasAteRuptura"",
    V.""DiasAteRuptura"" AS ""DiasAteRupturaFinal"",

    V.""DataRuptura"",
    V.""DataRuptura""    AS ""DataRupturaFinal"",

    V.""DemandaLeadTime"",
    V.""DemandaLeadTime"" AS ""DemandaLeadTimeRealista"",

    V.""NivelRisco""
FROM ""VW_PREVISAO_RUPTURA"" V
INNER JOIN ""OITM"" I
    ON V.""ItemCode"" = I.""ItemCode""
LEFT JOIN CurvaAbc ABC
    ON ABC.""ItemCode"" = V.""ItemCode""
WHERE 1 = 1
  AND I.""U_CbxForaLinha"" = 1
  AND I.""ItmsGrpCod"" NOT IN (101,102,136,174,144,145,167,168,169)
  AND I.""ItemClass""  NOT IN (1)
  AND I.""MatType"" = 0
  AND I.""SellItem"" = 'Y'";

            var parms = new List<OdbcParameter>();

            if (!string.IsNullOrWhiteSpace(itemFiltro))
            {
                sql += @" AND V.""ItemCode"" = ? ";
                parms.Add(new OdbcParameter("@ItemCode", itemFiltro));
            }

            if (!string.IsNullOrWhiteSpace(depositoFiltro))
            {
                sql += @" AND ""WhsCode"" = ? ";
                parms.Add(new OdbcParameter("@WhsCode", depositoFiltro));
            }

            var riscoNormalizado = riscoFiltro?.Trim().ToUpperInvariant();

            if (!string.IsNullOrWhiteSpace(riscoNormalizado))
            {
                if (riscoNormalizado == "RUPTURADO")
                {
                    sql += @" AND V.""DiasAteRuptura"" IS NOT NULL
              AND V.""DiasAteRuptura"" <= 0 ";
                }
                else
                {
                    sql += @" AND V.""NivelRisco"" = ?
              AND (V.""DiasAteRuptura"" IS NULL OR V.""DiasAteRuptura"" > 0) ";
                    parms.Add(new OdbcParameter("@NivelRisco", riscoFiltro));
                }
            }

            if (diasMax.HasValue)
            {
                sql += @" AND V.""DiasAteRuptura"" IS NOT NULL
              AND V.""DiasAteRuptura"" <= ? ";
                parms.Add(new OdbcParameter("@DiasMax", diasMax.Value));
            }

            sql += @" ORDER BY ""NivelRisco"" DESC, ""DiasAteRuptura"" ASC ";

            using var cn = new OdbcConnection(_connStr);
            await cn.OpenAsync();

            page.UltimaAtualizacao = await ObterUltimaAtualizacaoAsync(cn);

            using var cmd = new OdbcCommand(sql, cn);
            cmd.Parameters.AddRange(parms.ToArray());

            using var rd = await cmd.ExecuteReaderAsync();

            decimal GetDecimalSafe(DbDataReader r, string col)
            {
                int i = r.GetOrdinal(col);
                if (r.IsDBNull(i)) return 0m;
                return r.GetDecimal(i);
            }

            while (await rd.ReadAsync())
            {
                var consumoMedioDia = GetDecimalSafe(rd, "ConsumoMedioDia");
                var leadTimeDias = Convert.ToInt32(rd["LeadTimeDias"]);
                var demandaLeadTime = GetDecimalSafe(rd, "DemandaLeadTime");
                if (demandaLeadTime <= 0 && consumoMedioDia > 0 && leadTimeDias > 0)
                    demandaLeadTime = consumoMedioDia * leadTimeDias;

                var vm = new PrevisaoRupturaVM
                {
                    ItemCode = rd["ItemCode"]?.ToString() ?? "",
                    ItemName = rd["ItemName"]?.ToString() ?? "",
                    ForaLinhaStatus = rd["ForaLinhaStatus"]?.ToString() ?? "",
                    CurvaAbc = rd["CurvaAbc"]?.ToString() ?? "C",
                    Faturamento12Meses = GetDecimalSafe(rd, "Faturamento12Meses"),
                    EstoqueAtual = GetDecimalSafe(rd, "EstoqueAtual"),
                    EmTransito = GetDecimalSafe(rd, "EmTransito"),
                    DataProximaCompra = rd["DataProximaCompra"] == DBNull.Value
                        ? (DateTime?)null
                        : rd.GetDateTime(rd.GetOrdinal("DataProximaCompra")),
                    Comprometido = GetDecimalSafe(rd, "Comprometido"),
                    ConsumoMedioDia = consumoMedioDia,
                    EstoqueProjetado = GetDecimalSafe(rd, "EstoqueProjetado"),
                    DemandaLeadTime = demandaLeadTime,
                    LeadTimeDias = leadTimeDias,
                    NivelRisco = rd["NivelRisco"]?.ToString() ?? ""
                };

                var iDias = rd.GetOrdinal("DiasAteRuptura");
                if (!rd.IsDBNull(iDias))
                    vm.DiasAteRuptura = rd.GetDecimal(iDias);

                var iData = rd.GetOrdinal("DataRuptura");
                if (!rd.IsDBNull(iData))
                    vm.DataRuptura = rd.GetDateTime(iData);

                page.Linhas.Add(vm);
            }

            return page;
        }

        private static async Task<DateTime?> ObterUltimaAtualizacaoAsync(OdbcConnection cn)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandText = @"SELECT MAX(""GeradoEm"") FROM ""Z_RUPTURA_PREV_CONSUMO""";

            var value = await cmd.ExecuteScalarAsync();
            if (value == null || value == DBNull.Value)
                return null;

            if (value is DateTime dt)
                return dt;

            return DateTime.TryParse(value.ToString(), out var parsed)
                ? parsed
                : null;
        }

        public async Task<AnaliseRupturaVM?> BuscarAnalisePorItemAsync(string itemCode)
        {
            if (string.IsNullOrWhiteSpace(itemCode))
                return null;

            var sql = @"
WITH FaturamentoItem AS (
    SELECT
        L.""ItemCode"",
        SUM(L.""LineTotal"") AS ""Faturamento12Meses""
    FROM ""OINV"" H
    INNER JOIN ""INV1"" L
        ON L.""DocEntry"" = H.""DocEntry""
    INNER JOIN ""INV12"" U
        ON U.""DocEntry"" = H.""DocEntry""
    WHERE H.""CANCELED"" = 'N'
      AND U.""MainUsage"" IN (10, 13, 22)
      AND H.""DocDate"" >= ADD_MONTHS(CURRENT_DATE, -12)
      AND L.""ItemCode"" IS NOT NULL
    GROUP BY L.""ItemCode""

    UNION ALL

    SELECT
        L.""ItemCode"",
        -SUM(L.""LineTotal"") AS ""Faturamento12Meses""
    FROM ""ORIN"" H
    INNER JOIN ""RIN1"" L
        ON L.""DocEntry"" = H.""DocEntry""
    INNER JOIN ""RIN12"" U
        ON U.""DocEntry"" = H.""DocEntry""
    WHERE H.""CANCELED"" = 'N'
      AND U.""MainUsage"" IN (10, 13, 22)
      AND H.""DocDate"" >= ADD_MONTHS(CURRENT_DATE, -12)
      AND L.""ItemCode"" IS NOT NULL
    GROUP BY L.""ItemCode""
),
FaturamentoAgrupado AS (
    SELECT
        ""ItemCode"",
        SUM(""Faturamento12Meses"") AS ""Faturamento12Meses""
    FROM FaturamentoItem
    GROUP BY ""ItemCode""
),
CurvaBase AS (
    SELECT
        ""ItemCode"",
        ""Faturamento12Meses"",
        SUM(""Faturamento12Meses"") OVER () AS ""FaturamentoTotal""
    FROM FaturamentoAgrupado
    WHERE ""Faturamento12Meses"" > 0
),
CurvaAcumulada AS (
    SELECT
        ""ItemCode"",
        ""Faturamento12Meses"",
        CASE
            WHEN ""FaturamentoTotal"" <= 0 THEN 100
            ELSE
                SUM(""Faturamento12Meses"") OVER (
                    ORDER BY ""Faturamento12Meses"" DESC, ""ItemCode""
                    ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
                ) / ""FaturamentoTotal"" * 100
        END AS ""PercentualAcumulado""
    FROM CurvaBase
),
CurvaAbc AS (
    SELECT
        ""ItemCode"",
        ""Faturamento12Meses"",
        CASE
            WHEN ""PercentualAcumulado"" <= 50 THEN 'A'
            WHEN ""PercentualAcumulado"" <= 80 THEN 'B'
            ELSE 'C'
        END AS ""CurvaAbc""
    FROM CurvaAcumulada
)
SELECT TOP 1
    ""ItemCode"",
    ""ItemName"",
    ""EstoqueAtual"",
    ""EmTransito"",
    ""Comprometido"",
    ""EstoqueProjetado"",
    ""ConsumoHistoricoDia"",
    ""ConsumoIaDia"",
    ""ConsumoMedioDia"",
    ""LeadTimeDias"",
    ""DiasAteRuptura"",
    ""DataRuptura"",
    ""DemandaLeadTime"",
    ""NivelRisco""
FROM ""VW_PREVISAO_RUPTURA""
WHERE ""ItemCode"" = ?
";

            using var cn = new OdbcConnection(_connStr);
            await cn.OpenAsync();

            using var cmd = new OdbcCommand(sql, cn);
            cmd.Parameters.Add(new OdbcParameter("@ItemCode", itemCode));

            using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
                return null;

            var consumoMedioDia = rd.GetDecimal(rd.GetOrdinal("ConsumoMedioDia"));
            var leadTimeDias = Convert.ToInt32(rd["LeadTimeDias"]);
            var demandaLeadTime = rd.GetDecimal(rd.GetOrdinal("DemandaLeadTime"));
            if (demandaLeadTime <= 0 && consumoMedioDia > 0 && leadTimeDias > 0)
                demandaLeadTime = consumoMedioDia * leadTimeDias;

            var vm = new AnaliseRupturaVM
            {
                ItemCode = rd["ItemCode"]?.ToString() ?? "",
                ItemName = rd["ItemName"]?.ToString() ?? "",
                EstoqueAtual = rd.GetDecimal(rd.GetOrdinal("EstoqueAtual")),
                EmTransito = rd.GetDecimal(rd.GetOrdinal("EmTransito")),
                Comprometido = rd.GetDecimal(rd.GetOrdinal("Comprometido")),
                EstoqueProjetado = rd.GetDecimal(rd.GetOrdinal("EstoqueProjetado")),
                ConsumoHistoricoDia = rd.GetDecimal(rd.GetOrdinal("ConsumoHistoricoDia")),
                ConsumoIaDia = rd.GetDecimal(rd.GetOrdinal("ConsumoIaDia")),
                ConsumoMedioDia = consumoMedioDia,
                LeadTimeDias = leadTimeDias,
                DemandaLeadTime = demandaLeadTime,
                NivelRisco = rd["NivelRisco"]?.ToString() ?? ""
            };

            var iDias = rd.GetOrdinal("DiasAteRuptura");
            if (!rd.IsDBNull(iDias))
                vm.DiasAteRuptura = rd.GetDecimal(iDias);

            var iData = rd.GetOrdinal("DataRuptura");
            if (!rd.IsDBNull(iData))
                vm.DataRuptura = rd.GetDateTime(iData);

            if (vm.NivelRisco == "ALTO")
            {
                vm.MotivoRisco =
                    $"Estoque projetado de {vm.EstoqueProjetado:N0} un não cobre a demanda " +
                    $"no lead time de {vm.LeadTimeDias} dias (˜ {vm.DemandaLeadTime:N0} un).";
            }
            else if (vm.NivelRisco == "MÉDIO")
            {
                vm.MotivoRisco =
                    $"Estoque projetado de {vm.EstoqueProjetado:N0} un está próximo da demanda " +
                    $"no lead time de {vm.LeadTimeDias} dias (˜ {vm.DemandaLeadTime:N0} un).";
            }
            else if (vm.NivelRisco == "BAIXO")
            {
                vm.MotivoRisco =
                    $"Estoque projetado de {vm.EstoqueProjetado:N0} un é confortável frente " +
                    $"à demanda estimada de {vm.DemandaLeadTime:N0} un no período.";
            }
            else
            {
                vm.MotivoRisco = "Não foi possível determinar claramente o motivo do risco.";
            }

            var hist = vm.ConsumoHistoricoDia;
            var ia = vm.ConsumoIaDia;

            if (ia <= 0 && hist <= 0)
            {
                vm.ExplicacaoIa =
                    "Não há histórico suficiente de consumo para gerar uma previsão confiável. " +
                    "A IA precisa de vendas registradas ao longo do tempo para identificar tendência e comportamento.";
            }
            else if (ia > 0 && hist <= 0)
            {
                vm.ExplicacaoIa =
                    $"A IA estimou um consumo médio de {ia:N2} un/dia analisando os últimos registros de venda " +
                    "e identificando o comportamento recente do item, mesmo sem um histórico longo disponível.";
            }
            else if (ia > 0 && hist > 0)
            {
                var fator = hist > 0 ? ia / hist : 0;

                if (fator > 1.1m)
                {
                    vm.ExplicacaoIa =
                        $"O consumo histórico médio desse item é de {hist:N2} un/dia, " +
                        $"mas nos últimos meses o comportamento de vendas ficou mais forte. " +
                        $"Por isso, a IA projetou {ia:N2} un/dia (cerca de {fator:N2}x o histórico), " +
                        "considerando a tendência de crescimento e o aumento de demanda recente.";
                }
                else if (fator < 0.9m)
                {
                    vm.ExplicacaoIa =
                        $"Historicamente o consumo médio é de {hist:N2} un/dia, " +
                        $"porém, nos meses mais recentes o volume de vendas caiu. " +
                        $"A IA entendeu esse comportamento como uma tendência de redução e estimou {ia:N2} un/dia, " +
                        "ajustando a projeção para baixo.";
                }
                else
                {
                    vm.ExplicacaoIa =
                        $"O consumo histórico médio é de {hist:N2} un/dia e a IA estimou {ia:N2} un/dia, " +
                        "pois o comportamento recente é consistente com o histórico, sem grande tendência de alta ou de queda.";
                }
            }
            else
            {
                vm.ExplicacaoIa =
                    $"O consumo histórico médio é de {hist:N2} un/dia, " +
                    "mas nos dados mais recentes a IA não encontrou padrão consistente para projetar o futuro. " +
                    "Nesse caso, o histórico continua sendo a principal referência.";
            }

            return vm;
        }

        public async Task<RupturaResumoVM> ObterResumoAsync()
        {
            const string sql = @"
SELECT
    CASE
        WHEN V.""DiasAteRuptura"" IS NOT NULL
         AND V.""DiasAteRuptura"" <= 0
            THEN 'RUPTURADO'
        ELSE V.""NivelRisco""
    END AS ""NivelRisco"",
    COUNT(*) AS ""Qtde""
FROM ""VW_PREVISAO_RUPTURA"" V
INNER JOIN ""OITM"" I
    ON V.""ItemCode"" = I.""ItemCode""
WHERE 1 = 1
  AND I.""U_CbxForaLinha"" = 1
  AND I.""ItmsGrpCod"" NOT IN (101,102,136,174,144,145,167,168,169)
  AND I.""ItemClass""  NOT IN (1)
  AND I.""MatType"" = 0
  AND I.""SellItem"" = 'Y'
GROUP BY
    CASE
        WHEN V.""DiasAteRuptura"" IS NOT NULL
         AND V.""DiasAteRuptura"" <= 0
            THEN 'RUPTURADO'
        ELSE V.""NivelRisco""
    END;";

            var resumo = new RupturaResumoVM();

            using (var conn = new OdbcConnection(_connStr))
            {
                await conn.OpenAsync();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;

                    using (var rd = await cmd.ExecuteReaderAsync())
                    {
                        while (await rd.ReadAsync())
                        {
                            var nivelObj = rd["NivelRisco"];
                            var qtdeObj = rd["Qtde"];

                            var nivel = (nivelObj == DBNull.Value ? "" : Convert.ToString(nivelObj))!
                                .Trim()
                                .ToUpperInvariant();

                            var qtde = qtdeObj == DBNull.Value ? 0 : Convert.ToInt32(qtdeObj);

                            resumo.TotalItens += qtde;

                            switch (nivel)
                            {
                                case "RUPTURADO":
                                    resumo.QtdeRupturado += qtde;
                                    break;

                                case "ALTO":
                                case "ALTO RISCO":
                                    resumo.QtdeAltoRisco += qtde;
                                    break;

                                case "MEDIO":
                                case "MÉDIO":
                                case "MÉDIO RISCO":
                                    resumo.QtdeMedioRisco += qtde;
                                    break;

                                case "BAIXO":
                                case "BAIXO RISCO":
                                    resumo.QtdeBaixoRisco += qtde;
                                    break;

                                case "SEM RISCO":
                                case "OK":
                                case "SEM RISCO / OK":
                                    resumo.QtdeSemRisco += qtde;
                                    break;

                                default:
                                    resumo.QtdeSemRisco += qtde;
                                    break;
                            }
                        }
                    }
                }
            }

            return resumo;
        }
    }
}
