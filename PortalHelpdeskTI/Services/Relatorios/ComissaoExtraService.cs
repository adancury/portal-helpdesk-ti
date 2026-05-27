using System.Data.Odbc;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using PortalHelpdeskTI.Models.Relatorios;

namespace PortalHelpdeskTI.Services.Relatorios
{
    public class ComissaoExtraService
    {
        private readonly IConfiguration _cfg;

        public ComissaoExtraService(IConfiguration cfg)
        {
            _cfg = cfg;
        }

        private string SqlConn => _cfg.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection não configurada.");

        private string HanaConn => _cfg.GetConnectionString("HanaConn")
            ?? throw new InvalidOperationException("ConnectionStrings:HanaConn não configurada.");

        public async Task<ComissaoExtraVm> ObterAsync(int ano, int trimestre, string? tipoVendedor, CancellationToken ct = default)
        {
            // garante linhas de meta (para aparecer todo mundo no grid)
            await EnsureMetasAsync(ano, trimestre, ct);

            await using var sql = new SqlConnection(SqlConn);
            await sql.OpenAsync(ct);

            // Tipos disponíveis p/ filtro
            var tipos = (await sql.QueryAsync<string>(
                new CommandDefinition(
                    @"SELECT DISTINCT TipoVendedor 
                      FROM dbo.ComissaoVendedor
                      WHERE Ativo = 1 AND TipoVendedor IS NOT NULL
                      ORDER BY TipoVendedor", cancellationToken: ct)))
                .ToList();

            var whereTipo = string.IsNullOrWhiteSpace(tipoVendedor) ? "" : " AND TipoVendedor = @TipoVendedor ";

            var linhas = (await sql.QueryAsync<ComissaoExtraLinhaVm>(
                new CommandDefinition(
                    $@"
                    SELECT
                        ComissaoVendedorId,
                        SlpCode,
                        SlpName,
                        TipoVendedor,
                        Ano,
                        Trimestre,
                        MetaValor AS Meta,
                        Realizado,
                        DesvioPct AS DesvioPercentual,
                        DesvioValor,

                        Atingiu_05 AS Atingiu05,
                        Atingiu_07 AS Atingiu07,
                        Atingiu_10 AS Atingiu10,

                        Valor_05 AS Valor05,
                        Valor_07 AS Valor07,
                        Valor_10 AS Valor10,

                        PercentualAplicado,
                        ComissaoExtraValor
                    FROM dbo.vw_ComissaoExtraTrimestre
                    WHERE Ano = @Ano AND Trimestre = @Trimestre
                    {whereTipo}
                    ORDER BY TipoVendedor, SlpName;",
                    new { Ano = ano, Trimestre = trimestre, TipoVendedor = tipoVendedor },
                    cancellationToken: ct)))
                .ToList();

            return new ComissaoExtraVm
            {
                Ano = ano,
                Trimestre = trimestre,
                TipoVendedor = tipoVendedor,
                TiposVendedor = tipos,
                Linhas = linhas
            };
        }

        public async Task SalvarMetaAsync(int vendedorId, int ano, int trimestre, decimal meta, CancellationToken ct = default)
        {
            if (trimestre < 1 || trimestre > 4) throw new ArgumentOutOfRangeException(nameof(trimestre));
            if (meta < 0) meta = 0;

            const string upsert = @"
            MERGE dbo.ComissaoExtraMetaTrimestre AS tgt
            USING (SELECT @VendedorId AS ComissaoVendedorId, @Ano AS Ano, @Trimestre AS Trimestre) AS src
            ON  tgt.ComissaoVendedorId = src.ComissaoVendedorId
            AND tgt.Ano = src.Ano
            AND tgt.Trimestre = src.Trimestre
            WHEN MATCHED THEN
                UPDATE SET MetaValor = @Meta, AtualizadoEm = SYSDATETIME(), Ativo = 1
            WHEN NOT MATCHED THEN
                INSERT (ComissaoVendedorId, Ano, Trimestre, MetaValor, Ativo, CriadoEm)
                VALUES (@VendedorId, @Ano, @Trimestre, @Meta, 1, SYSDATETIME());";

            await using var sql = new SqlConnection(SqlConn);
            await sql.ExecuteAsync(new CommandDefinition(upsert, new { VendedorId = vendedorId, Ano = ano, Trimestre = (byte)trimestre, Meta = meta }, cancellationToken: ct));
        }

        public async Task<int> AtualizarRealizadoSapAsync(int ano, int trimestre, CancellationToken ct = default)
        {
            if (trimestre < 1 || trimestre > 4) throw new ArgumentOutOfRangeException(nameof(trimestre));
            var (dtIni, dtFim) = FiscalQuarterRange(ano, trimestre);

            // 1) consulta HANA (DocTotal - VatSum) vendas - devoluções
            var hanaRows = new List<(int SlpCode, decimal Realizado)>();

            await using (var hana = new OdbcConnection(HanaConn))
            {
                await hana.OpenAsync(ct);

                const string sqlHana = @"
                WITH V AS (
                  SELECT
                    ""SlpCode"",
                    SUM(""DocTotal"" - ""VatSum"") AS ""Vendas""
                  FROM ""OINV""
                  WHERE ""CANCELED"" = 'N'
                    AND ""DocDate"" BETWEEN ? AND ?
                  GROUP BY ""SlpCode""
                ),
                D AS (
                  SELECT
                    ""SlpCode"",
                    SUM(""DocTotal"" - ""VatSum"") AS ""Devol""
                  FROM ""ORIN""
                  WHERE ""CANCELED"" = 'N'
                    AND ""DocDate"" BETWEEN ? AND ?
                  GROUP BY ""SlpCode""
                )
                SELECT
                  COALESCE(V.""SlpCode"", D.""SlpCode"") AS ""SlpCode"",
                  COALESCE(V.""Vendas"", 0) - COALESCE(D.""Devol"", 0) AS ""RealizadoLiquido""
                FROM V
                FULL OUTER JOIN D ON D.""SlpCode"" = V.""SlpCode""
                ORDER BY 1;";

                // ✅ NÃO use object[] aqui. Use DynamicParameters.
                var p = new Dapper.DynamicParameters();
                p.Add("p1", dtIni.Date);
                p.Add("p2", dtFim.Date);
                p.Add("p3", dtIni.Date);
                p.Add("p4", dtFim.Date);

                // Dapper + ODBC + "?" -> ele adiciona parâmetros na ordem que você adicionou
                var res = await hana.QueryAsync(sqlHana, p);

                foreach (var r in res)
                {
                    int slp = (int)r.SlpCode;
                    decimal val = (decimal)r.RealizadoLiquido;
                    hanaRows.Add((slp, val));
                }
            }

            if (hanaRows.Count == 0) return 0;

            // 2) UPSERT no SQL Server (SlpCode -> ComissaoVendedorId)
            const string mergeSql = @"
            MERGE dbo.ComissaoExtraRealizadoTrimestre AS tgt
            USING (
                SELECT
                    v.Id AS ComissaoVendedorId,
                    @Ano AS Ano,
                    @Trimestre AS Trimestre,
                    @Realizado AS RealizadoValor
                FROM dbo.ComissaoVendedor v
                WHERE v.SlpCode = @SlpCode
                  AND v.Ativo = 1
            ) AS src
            ON  tgt.ComissaoVendedorId = src.ComissaoVendedorId
            AND tgt.Ano = src.Ano
            AND tgt.Trimestre = src.Trimestre
            WHEN MATCHED THEN
                UPDATE SET
                    RealizadoValor = src.RealizadoValor,
                    Origem = 'SAP',
                    AtualizadoEm = SYSDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (ComissaoVendedorId, Ano, Trimestre, RealizadoValor, Origem, AtualizadoEm)
                VALUES (src.ComissaoVendedorId, src.Ano, src.Trimestre, src.RealizadoValor, 'SAP', SYSDATETIME());";

            int afetados = 0;

            await using (var sql = new SqlConnection(SqlConn))
            {
                await sql.OpenAsync(ct);
                await using var tx = await sql.BeginTransactionAsync(ct);

                try
                {
                    foreach (var (slpCode, realizado) in hanaRows)
                    {
                        // se o SlpCode não existir em dbo.ComissaoVendedor, não insere (by design)
                        afetados += await sql.ExecuteAsync(
                            new CommandDefinition(
                                mergeSql,
                                new { Ano = ano, Trimestre = (byte)trimestre, SlpCode = slpCode, Realizado = realizado },
                                transaction: tx,
                                cancellationToken: ct));
                    }

                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }

            return afetados;
        }

        private async Task EnsureMetasAsync(int ano, int trimestre, CancellationToken ct)
        {
            const string sql = @"
            INSERT INTO dbo.ComissaoExtraMetaTrimestre (ComissaoVendedorId, Ano, Trimestre, MetaValor, Ativo, CriadoEm)
            SELECT v.Id, @Ano, @Trimestre, 0, 1, SYSDATETIME()
            FROM dbo.ComissaoVendedor v
            WHERE v.Ativo = 1
              AND NOT EXISTS (
                  SELECT 1
                  FROM dbo.ComissaoExtraMetaTrimestre m
                  WHERE m.ComissaoVendedorId = v.Id
                    AND m.Ano = @Ano
                    AND m.Trimestre = @Trimestre
              );";

            await using var sqlConn = new SqlConnection(SqlConn);
            await sqlConn.ExecuteAsync(new CommandDefinition(sql, new { Ano = ano, Trimestre = (byte)trimestre }, cancellationToken: ct));
        }

        private static (DateTime dtIni, DateTime dtFim) FiscalQuarterRange(int anoFiscal, int trimestre)
        {
            if (trimestre < 1 || trimestre > 4) throw new ArgumentOutOfRangeException(nameof(trimestre));

            // AnoFiscal = ano em que começa o ciclo (Agosto)
            // T1: Ago-Out (anoFiscal)
            // T2: Nov-Jan (anoFiscal -> anoFiscal+1)
            // T3: Fev-Abr (anoFiscal+1)
            // T4: Mai-Jul (anoFiscal+1)

            int startYear, startMonth;
            switch (trimestre)
            {
                case 1: startYear = anoFiscal; startMonth = 8; break; // Ago
                case 2: startYear = anoFiscal; startMonth = 11; break; // Nov
                case 3: startYear = anoFiscal + 1; startMonth = 2; break; // Fev
                case 4: startYear = anoFiscal + 1; startMonth = 5; break; // Mai
                default: throw new ArgumentOutOfRangeException(nameof(trimestre));
            }

            var dtIni = new DateTime(startYear, startMonth, 1);
            var dtFim = dtIni.AddMonths(3).AddDays(-1);
            return (dtIni, dtFim);
        }

    }
}
