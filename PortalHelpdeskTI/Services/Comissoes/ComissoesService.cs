using Dapper;
using Microsoft.EntityFrameworkCore;
using PortalHelpdeskTI.Infrastructure;
using PortalHelpdeskTI.Models.Comissoes;
using System.Data;
using System.Data.Odbc;

namespace PortalHelpdeskTI.Services.Comissoes
{
    public class ComissoesService : IComissoesService
    {
        private readonly string _hanaConn;
        private readonly IHanaDb _hana;
        private readonly AppDbContext _db;

        // =========================================================
        // NOVO: comissão fixa exportação (0,15%)
        // =========================================================
        private const decimal PERCENTUAL_EXPORTACAO = 0.0015m; // 0,15%

        // =========================================================
        // NOVO: comissão fixa especial (FILLIPE / MAGNOLIA) = 16%
        // =========================================================
        private const int SLP_ESPECIAL_MAGNOLIA = 236;
        private const decimal PERCENTUAL_MAGNOLIA = 0.16m; // 16%

        // =========================================================
        // NOVO: comissao fixa Rodrigo Viana
        // =========================================================
        private const int SLP_ESPECIAL_RODRIGO_R_VIANA = 35;

        public ComissoesService(IConfiguration cfg, AppDbContext db, IHanaDb hana)
        {
            _hanaConn = cfg.GetConnectionString("HanaConn")
                ?? throw new InvalidOperationException("ConnectionString 'HanaConn' não encontrada.");
            _db = db;
            _hana = hana;
        }

        // =========================================================
        // TIPOS DE VENDEDOR (regra vem da tabela ComissaoVendedor)
        // =========================================================
        private enum TipoVendedor
        {
            Representante = 1,
            Interno = 2,
            Externo = 3
        }

        private static TipoVendedor ParseTipoVendedor(string? dbValue)
        {
            var v = (dbValue ?? "").Trim().ToUpperInvariant();

            return v switch
            {
                "REPRESENTANTE" or "REP" => TipoVendedor.Representante,
                "INTERNO" or "INT" => TipoVendedor.Interno,
                "EXTERNO" or "EXT" => TipoVendedor.Externo,
                _ => TipoVendedor.Representante
            };
        }

        private static decimal ObterPercentualComissaoPorDesconto(decimal descontoPct, TipoVendedor tipo, int slpCode)
        {
            switch (tipo)
            {
                case TipoVendedor.Interno:
                    if (descontoPct <= 8.0m) return 0.0300m;
                    if (descontoPct <= 12.0m) return 0.0250m;
                    if (descontoPct <= 18.0m) return 0.0200m;
                    if (descontoPct <= 24.0m) return 0.0150m;
                    if (descontoPct <= 27.0m) return 0.0100m;
                    if (descontoPct <= 29.0m) return 0.0070m;
                    return 0.0050m;

                case TipoVendedor.Externo:
                    if (descontoPct <= 8.0m) return 0.0550m;
                    if (descontoPct <= 12.0m) return 0.0500m;
                    if (descontoPct <= 18.0m) return 0.0450m;
                    if (descontoPct <= 24.0m) return 0.0400m;
                    if (descontoPct <= 27.0m) return 0.0350m;
                    if (descontoPct <= 29.0m) return 0.0300m;
                    return 0.0250m;

                case TipoVendedor.Representante:
                default:
                    if (descontoPct <= 8.0m) return 0.0800m;
                    if (descontoPct <= 12.0m) return 0.0700m;
                    if (descontoPct <= 18.0m) return 0.0650m;
                    if (descontoPct <= 24.0m) return 0.0500m;
                    if (descontoPct <= 27.0m) return 0.0450m;
                    if (descontoPct <= 29.0m) return 0.0400m;

                    // Regra especial:
                    // Rodrigo R Viana Representações - SlpCode 35
                    // Para desconto acima de 29%, comissão permanece em 4%
                    if (slpCode == SLP_ESPECIAL_RODRIGO_R_VIANA)
                        return 0.0400m;

                    return 0.0300m;
            }
        }

        // =========================================================
        // REGRA: À VISTA (GroupNum = 20) reduz 3 p.p. do desconto da linha
        // =========================================================
        private static decimal AjustarDescontoEfetivo(decimal descontoPctLinha, int condicaoPagamento)
        {
            if (condicaoPagamento == 20)
            {
                var v = descontoPctLinha - 3.0m;
                return v < 0m ? 0m : v;
            }

            return descontoPctLinha;
        }

        // =========================================================
        // NOVO: identifica vendedor de exportação (OSLP.Memo LIKE '%EXPO%')
        // =========================================================
        private async Task<bool> IsVendedorExportacaoAsync(int slpCode, CancellationToken ct)
        {
            using var conn = new OdbcConnection(_hanaConn);
            await conn.OpenAsync(ct);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT IFNULL(""Memo"", '') AS ""Memo""
                FROM OSLP
                WHERE ""SlpCode"" = ?";

            cmd.Parameters.Add("pSlp", OdbcType.Int).Value = slpCode;

            var memoObj = await cmd.ExecuteScalarAsync(ct);
            var memo = Convert.ToString(memoObj) ?? "";

            return memo.IndexOf("EXPO", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // =========================================================
        // NOVO: valida se vendedor está ATIVO no SAP (OSLP.Active = 'Y')
        // =========================================================
        private async Task<bool> IsVendedorAtivoSapAsync(int slpCode, CancellationToken ct)
        {
            using var conn = new OdbcConnection(_hanaConn);
            await conn.OpenAsync(ct);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT IFNULL(""Active"", 'N') AS ""Active""
                FROM OSLP
                WHERE ""SlpCode"" = ?";

            cmd.Parameters.Add("pSlp", OdbcType.Int).Value = slpCode;

            var activeObj = await cmd.ExecuteScalarAsync(ct);
            var active = Convert.ToString(activeObj) ?? "N";

            return string.Equals(active, "Y", StringComparison.OrdinalIgnoreCase);
        }

        // =========================================================
        // NOVO: retorna lista de SlpCode ativos no SAP (para filtro em massa)
        // =========================================================
        private async Task<HashSet<int>> BuscarSlpCodesAtivosSapAsync(CancellationToken ct)
        {
            const string sql = @"
                SELECT ""SlpCode"" AS SlpCode
                FROM OSLP
                WHERE IFNULL(""Active"", 'N') = 'Y'";

            using var conn = new OdbcConnection(_hanaConn);
            await conn.OpenAsync(ct);

            var rows = await conn.QueryAsync<int>(new CommandDefinition(sql, cancellationToken: ct));
            return rows.ToHashSet();
        }

        // =========================================================
        // VENDEDORES (SAP -> OSLP)
        // =========================================================
        public async Task<List<OsLpRow>> BuscarVendedoresSapAsync(CancellationToken ct)
        {
            const string sql = @"
            SELECT
                ""SlpCode"" AS SlpCode,
                ""SlpName"" AS SlpName,
                ""Active""  AS Active,
                ""Email""   AS E_Mail
            FROM OSLP
            ORDER BY ""SlpName""";

            using var conn = new OdbcConnection(_hanaConn);
            await conn.OpenAsync(ct);

            var rows = await conn.QueryAsync<OsLpRow>(
                new CommandDefinition(sql, cancellationToken: ct)
            );

            return rows.ToList();
        }

        // =========================================================
        // PERÍODO DE APURAÇÃO
        // =========================================================
        private static (DateTime Ini, DateTime Fim) CalcularPeriodoApuracao(string tipoVendedor, int ano, int mes)
        {
            tipoVendedor = (tipoVendedor ?? "").Trim().ToUpperInvariant();

            if (tipoVendedor == "REPRESENTANTE")
            {
                var ini = new DateTime(ano, mes, 1);
                var fim = new DateTime(ano, mes, DateTime.DaysInMonth(ano, mes));
                return (ini, fim);
            }

            var iniBase = new DateTime(ano, mes, 1).AddMonths(-1);
            var ini2 = new DateTime(iniBase.Year, iniBase.Month, 26);
            var fim2 = new DateTime(ano, mes, 25);
            return (ini2, fim2);
        }

        // =========================================================
        // RESUMO
        // =========================================================
        public async Task<ResumoComissaoVm> GerarResumoAsync(int ano, int mes, CancellationToken ct)
        {
            // 1) vendedores ativos no portal
            var vendedoresPortal = await _db.ComissaoVendedores
                .Where(x => x.Ativo && x.ParticipaRelatorio)
                .OrderBy(x => x.SlpName)
                .ToListAsync(ct);

            // 2) vendedores ativos no SAP
            var slpAtivosSap = await BuscarSlpCodesAtivosSapAsync(ct);

            // 3) interseção (somente ativos em ambos)
            var vendedores = vendedoresPortal
                .Where(v => slpAtivosSap.Contains(v.SlpCode))
                .ToList();

            var resumo = new ResumoComissaoVm
            {
                Ano = ano,
                Mes = mes
            };

            foreach (var v in vendedores)
            {
                var (ini, fim) = CalcularPeriodoApuracao(v.TipoVendedor, ano, mes);
                var rel = await GerarRelatorioAsync(v.SlpCode, ini, fim, ct);

                // Descontos do resumo
                var descontos = rel.DescontoCondicionado + rel.DescontosRepresentante;

                // REGRA FINAL:
                // Comissão Bruta = soma das comissões de venda - soma das comissões de devolução
                // Comissão Líquida = Comissão Bruta - descontos
                // IR = 1,5% sobre a Comissão Líquida
                // Valor a Receber = Comissão Líquida - IR
                var valorBruto = rel.ComissaoBruta;
                var valorLiq = valorBruto - descontos;
                var ir = rel.Tributos;
                var valorComissao = valorLiq - ir;

                resumo.Linhas.Add(new ResumoComissaoLinhaVm
                {
                    SlpCode = v.SlpCode,
                    SlpName = v.SlpName,
                    TipoVendedor = v.TipoVendedor,
                    BaseCalculo = v.BaseCalculo,

                    // Mantém campos antigos (se a grid atual usa isso)
                    ReceitaLiquida = rel.ReceitaLiquida,
                    ComissaoBruta = rel.ComissaoBruta,
                    Tributos = rel.Tributos,

                    Descontos = descontos,
                    ComissaoDevolucoes = rel.ComissaoDevolucoes,

                    // Campos do PDF consolidado (colunas do print)
                    Mes = mes,
                    ValorBruto = valorBruto,
                    IR = ir,
                    ValorLiq = valorLiq,
                    ValorComissao = valorComissao
                });

                resumo.DataIni = ini;
                resumo.DataFim = fim;
            }

            return resumo;
        }

        // =========================================================
        // RELATÓRIO
        // =========================================================
        public async Task<RelatorioComissaoVm> GerarRelatorioAsync(int slpCode, DateTime ini, DateTime fim, CancellationToken ct)
        {
            ini = ini.Date;
            fim = fim.Date;

            if (fim < ini)
                throw new ArgumentException("Data final não pode ser menor que data inicial.");

            var vend = await _db.ComissaoVendedores
                .Where(x => x.SlpCode == slpCode && x.Ativo && x.ParticipaRelatorio)
                .Select(x => new { x.SlpCode, x.SlpName, x.Percentual, x.BaseCalculo, x.TipoVendedor, x.DestacarIR })
                .FirstOrDefaultAsync(ct);

            if (vend == null)
                throw new InvalidOperationException($"Vendedor não cadastrado/ativo no Portal para o SlpCode={slpCode}.");

            // ✅ NOVO: valida ATIVO no SAP também
            var ativoSap = await IsVendedorAtivoSapAsync(slpCode, ct);
            if (!ativoSap)
                throw new InvalidOperationException($"Vendedor SlpCode={slpCode} está INATIVO no SAP (OSLP.Active <> 'Y').");

            var tipo = ParseTipoVendedor(vend.TipoVendedor);

            // identifica exportação pelo Memo do SAP
            // ✅ Magnolia (SlpCode 236) deve usar comissão fixa 16% e NÃO deve forçar filtro de MainUsage de exportação
            var isMagnolia = (slpCode == SLP_ESPECIAL_MAGNOLIA);

            var isExportacao = !isMagnolia && await IsVendedorExportacaoAsync(slpCode, ct);

            // ✅ Percentual fixo efetivo (prioridade: Magnolia > Exportação)
            decimal? percentualFixo = isMagnolia ? PERCENTUAL_MAGNOLIA
                                    : (isExportacao ? PERCENTUAL_EXPORTACAO
                                                    : (decimal?)null);
            List<NotaLinhaHanaRow> linhas;

            if (string.Equals(vend.BaseCalculo, "BOLETO", StringComparison.OrdinalIgnoreCase))
                linhas = await BuscarLinhasPorRecebimentoBoletoAsync(slpCode, ini, fim, isExportacao, ct);
            else
                linhas = await BuscarLinhasAsync(slpCode, ini, fim, isExportacao, ct);

            var linhasDev = await BuscarLinhasDevolucaoAsync(slpCode, ini, fim, ct);

            var ajustes = await _db.ComissaoAjustes
                .Where(a => a.SlpCode == slpCode
                         && a.DataIni <= fim
                         && a.DataFim >= ini)
                .ToListAsync(ct);

            var descontoCondicionado = ajustes
                .Where(a => a.Tipo == "DESCONTO_CONDICIONADO")
                .Sum(a => a.Valor);

            var descontoRepresentante = ajustes
                .Where(a => a.Tipo == "DESCONTO_REP_DEVOLUCAO"
                         || a.Tipo == "DESCONTO_REP_FRETE"
                         || a.Tipo == "DESCONTO_REP_OUTROS")
                .Sum(a => a.Valor);

            var vm = new RelatorioComissaoVm
            {
                SlpCode = vend.SlpCode,
                SlpName = vend.SlpName,
                Percentual = vend.Percentual,
                DataIni = ini,
                DataFim = fim,

                Tributos = 0m,
                DescontoCondicionado = descontoCondicionado,
                DescontosRepresentante = descontoRepresentante,

                Observacoes = ajustes
                    .Where(a => !string.IsNullOrWhiteSpace(a.Observacao))
                    .Select(a => a.Observacao!)
                    .ToList()
            };

            // =========================
            // VENDAS (crédito)
            // =========================
            var grupos = linhas
                .GroupBy(x => new { x.DataFaturamento, x.NumeroNF, x.ClienteCodigo, x.ClienteNome })
                .OrderBy(g => g.Key.DataFaturamento)
                .ThenBy(g => g.Key.NumeroNF);

            foreach (var g in grupos)
            {
                var rbv = g.Sum(x => x.RbvLinha);
                var rlv = g.Sum(x => x.RlvLinha);

                var descontoMedioEfetivo = g.Average(x => AjustarDescontoEfetivo(x.DescontoPctLinha, x.CondicaoPagamento));

                decimal comissaoSemArred;

                if (percentualFixo.HasValue)
                {
                    comissaoSemArred = g.Sum(x => x.RlvLinha * percentualFixo.Value);
                }
                else
                {
                    comissaoSemArred = g.Sum(x =>
                    {
                        var descontoEfetivo = AjustarDescontoEfetivo(x.DescontoPctLinha, x.CondicaoPagamento);
                        var percLinha = ObterPercentualComissaoPorDesconto(descontoEfetivo, tipo, slpCode);
                        return x.RlvLinha * percLinha;
                    });
                }

                var comissao = Math.Round(comissaoSemArred, 2, MidpointRounding.AwayFromZero);

                var percEfetivo =
                    rlv == 0 ? 0m :
                    (percentualFixo.HasValue ? percentualFixo.Value : (comissao / rlv));
                vm.Linhas.Add(new RelatorioComissaoLinhaVm
                {
                    Data = g.Key.DataFaturamento,
                    Nf = g.Key.NumeroNF,
                    ClienteCodigo = g.Key.ClienteCodigo,
                    ClienteNome = g.Key.ClienteNome,
                    VendaBruta = rbv,
                    VendaLiquida = rlv,
                    DescontoMedioPct = Math.Round(descontoMedioEfetivo, 2, MidpointRounding.AwayFromZero),
                    Percentual = percEfetivo,
                    Comissao = comissao
                });
            }

            // =========================
            // DEVOLUÇÕES (débito)
            // =========================
            if (linhasDev.Count > 0)
            {
                var gruposDev = linhasDev
                    .GroupBy(x => new { x.DataFaturamento, x.NumeroNF, x.ClienteCodigo, x.ClienteNome })
                    .OrderBy(g => g.Key.DataFaturamento)
                    .ThenBy(g => g.Key.NumeroNF);

                foreach (var g in gruposDev)
                {
                    var rbvDev = g.Sum(x => x.RbvLinha);
                    var rlvDev = g.Sum(x => x.RlvLinha);
                    var descontoMedioEfetivo = g.Average(x => AjustarDescontoEfetivo(x.DescontoPctLinha, x.CondicaoPagamento));

                    decimal comissaoDevSemArred;

                    if (percentualFixo.HasValue)
                    {
                        comissaoDevSemArred = g.Sum(x => x.RlvLinha * percentualFixo.Value);
                    }
                    else
                    {
                        comissaoDevSemArred = g.Sum(x =>
                        {
                            var descontoEfetivo = AjustarDescontoEfetivo(x.DescontoPctLinha, x.CondicaoPagamento);
                            var percLinha = ObterPercentualComissaoPorDesconto(descontoEfetivo, tipo, slpCode);
                            return x.RlvLinha * percLinha;
                        });
                    }

                    var comissaoDev = Math.Round(comissaoDevSemArred, 2, MidpointRounding.AwayFromZero);

                    var percEfetivo =
                        rlvDev == 0 ? 0m :
                        (percentualFixo.HasValue ? percentualFixo.Value : (comissaoDev / rlvDev));
                    vm.Devolucoes.Add(new RelatorioComissaoLinhaVm
                    {
                        Data = g.Key.DataFaturamento,
                        Nf = g.Key.NumeroNF,
                        ClienteCodigo = g.Key.ClienteCodigo,
                        ClienteNome = g.Key.ClienteNome,
                        VendaBruta = rbvDev,
                        VendaLiquida = rlvDev,
                        DescontoMedioPct = Math.Round(descontoMedioEfetivo, 2, MidpointRounding.AwayFromZero),
                        Percentual = percEfetivo,
                        Comissao = comissaoDev
                    });
                }

                vm.ReceitaBrutaDevolucoes = vm.Devolucoes.Sum(x => x.VendaBruta);
                vm.ReceitaLiquidaDevolucoes = vm.Devolucoes.Sum(x => x.VendaLiquida);
                vm.ComissaoDevolucoes = vm.Devolucoes.Sum(x => x.Comissao);
            }

            vm.ReceitaBruta = vm.Linhas.Sum(x => x.VendaBruta);
            vm.ReceitaLiquida = vm.Linhas.Sum(x => x.VendaLiquida);

            // NOVA REGRA:
            // Comissão Bruta = soma das comissões de venda - soma das comissões de devolução
            var comissaoVendas = vm.Linhas.Sum(x => x.Comissao);
            var comissaoDevolucoes = vm.Devolucoes.Sum(x => x.Comissao);
            vm.ComissaoBruta = comissaoVendas - comissaoDevolucoes;

            vm.DescontosItens = ajustes
                .Where(a => a.Tipo.StartsWith("DESCONTO_REP_"))
                .OrderByDescending(a => a.Id)
                .ToList();

            // Tributos (IR) - somente REPRESENTANTE E SOMENTE se DestacarIR = true
            var aplicaIR = (tipo == TipoVendedor.Representante) && vend.DestacarIR;

            if (aplicaIR)
            {
                const decimal IR_ALIQUOTA = 0.015m;

                var tributosAjuste = ajustes
                    .Where(a => a.Tipo == "IR")
                    .Sum(a => a.Valor);

                // REGRA FINAL:
                // IR = 1,5% sobre a comissão bruta
                var baseIr = vm.ComissaoBruta;

                if (baseIr < 0m)
                    baseIr = 0m;

                var irCalculado = Math.Round(baseIr * IR_ALIQUOTA, 2, MidpointRounding.AwayFromZero);

                vm.Tributos = irCalculado + tributosAjuste;
            }
            else
            {
                vm.Tributos = 0m;
            }

            return vm;
        }

        // =========================================================
        // BUSCA VENDAS (OINV/INV1) - FATURAMENTO
        // =========================================================
        private async Task<List<NotaLinhaHanaRow>> BuscarLinhasAsync(int slpCode, DateTime ini, DateTime fim, bool isExportacao, CancellationToken ct)
        {
            var result = new List<NotaLinhaHanaRow>();

            using var conn = new OdbcConnection(_hanaConn);
            await conn.OpenAsync(ct);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            SELECT
                H.""DocDate""      AS ""DataFaturamento"",
                H.""Serial""       AS ""NumeroNF"",
                H.""CardCode""     AS ""ClienteCodigo"",
                H.""CardName""     AS ""ClienteNome"",
                H.""SlpCode""      AS ""SlpCode"",
                H.""GroupNum""     AS ""CondicaoPagamento"",
                L.""LineNum""      AS ""LineNum"",
                L.""LineTotal""    AS ""RLV_Linha"",
                (L.""LineTotal"" + L.""VatSum"") AS ""RBV_Linha"",
                L.""DiscPrcnt""    AS ""DescontoPctLinha"",
                IFNULL(H.""DiscPrcnt"", 0) AS ""DescontoPctGeral"",
                U.""MainUsage""    AS ""MainUsage""
            FROM OINV H
            INNER JOIN INV1  L  ON L.""DocEntry"" = H.""DocEntry""
            INNER JOIN INV12 U  ON U.""DocEntry"" = H.""DocEntry""
            INNER JOIN OSLP  S  ON S.""SlpCode""  = H.""SlpCode""
            WHERE
                H.""CANCELED"" = 'N'
                AND H.""DocDate"" BETWEEN ? AND ?
                AND H.""SlpCode"" = ?
                AND IFNULL(S.""Active"", 'N') = 'Y'
                AND (
                    (? = 1 AND U.""MainUsage"" = '13')
                    OR
                    (? = 0 AND U.""MainUsage"" in ('10', '22'))
                )
                AND IFNULL(H.""SeqCode"", 0) <> 28
            ORDER BY
                H.""DocDate"",
                H.""Serial"",
                L.""LineNum"";";

            cmd.Parameters.Add("pIni", OdbcType.Date).Value = ini;
            cmd.Parameters.Add("pFim", OdbcType.Date).Value = fim;
            cmd.Parameters.Add("pSlp", OdbcType.Int).Value = slpCode;
            cmd.Parameters.Add("pIsExpo1", OdbcType.Int).Value = isExportacao ? 1 : 0;
            cmd.Parameters.Add("pIsExpo2", OdbcType.Int).Value = isExportacao ? 1 : 0;

            using var rd = await cmd.ExecuteReaderAsync(ct);

            while (await rd.ReadAsync(ct))
            {
                var rlv = rd.IsDBNull(7) ? 0m : Convert.ToDecimal(rd.GetValue(7));
                var rbv = rd.IsDBNull(8) ? 0m : Convert.ToDecimal(rd.GetValue(8));
                var descLinhaPct = rd.IsDBNull(9) ? 0m : Convert.ToDecimal(rd.GetValue(9));
                var descGeralPct = rd.IsDBNull(10) ? 0m : Convert.ToDecimal(rd.GetValue(10));
                var groupNum = rd.IsDBNull(5) ? 0 : Convert.ToInt32(rd.GetValue(5));

                if (descGeralPct > 0m && rlv != 0m)
                {
                    var valorDescGeral = Math.Round(rlv * (descGeralPct / 100m), 2, MidpointRounding.AwayFromZero);
                    rlv -= valorDescGeral;
                    rbv -= valorDescGeral;
                }

                result.Add(new NotaLinhaHanaRow
                {
                    DataFaturamento = rd.GetDateTime(0).Date,
                    NumeroNF = Convert.ToInt32(rd.GetValue(1)),
                    ClienteCodigo = Convert.ToString(rd.GetValue(2)) ?? "",
                    ClienteNome = Convert.ToString(rd.GetValue(3)) ?? "",
                    RlvLinha = rlv,
                    RbvLinha = rbv,
                    DescontoPctLinha = descLinhaPct,
                    CondicaoPagamento = groupNum
                });
            }

            return result;
        }

        // =========================================================
        // BUSCA VENDAS POR BOLETO (INV6 por vencimento)
        // REGRA: comissão sobre (parcela - imposto) e imposto só na 1ª parcela
        // =========================================================
        private async Task<List<NotaLinhaHanaRow>> BuscarLinhasPorRecebimentoBoletoAsync(int slpCode, DateTime ini, DateTime fim, bool isExportacao, CancellationToken ct)
        {
            var result = new List<NotaLinhaHanaRow>();

            using var conn = new OdbcConnection(_hanaConn);
            await conn.OpenAsync(ct);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT
                    P.""DueDate""       AS ""DataVencimento"",
                    H.""Serial""        AS ""NumeroNF"",
                    H.""CardCode""      AS ""ClienteCodigo"",
                    H.""CardName""      AS ""ClienteNome"",
                    H.""GroupNum""      AS ""CondicaoPagamento"",
                    L.""LineNum""       AS ""LineNum"",

                    -- RLV (liquido): rateia a BASE da parcela nas linhas liquidas
                    CASE
                      WHEN (IFNULL(H.""DocTotal"",0) - IFNULL(H.""VatSum"",0)) <= 0 THEN 0
                      ELSE
                        L.""LineTotal"" *
                        (
                          CASE
                            WHEN P.""InstlmntID"" = 1 THEN
                                 CASE
                                   WHEN (P.""InsTotal"" - IFNULL(H.""VatSum"",0)) < 0 THEN 0
                                   ELSE (P.""InsTotal"" - IFNULL(H.""VatSum"",0))
                                 END
                            ELSE P.""InsTotal""
                          END
                          / (IFNULL(H.""DocTotal"",0) - IFNULL(H.""VatSum"",0))
                        )
                    END AS ""RLV_Linha"",

                    -- RBV (bruto): opcional (mantém padrão)
                    CASE
                      WHEN IFNULL(H.""DocTotal"",0) <= 0 THEN 0
                      ELSE
                        (L.""LineTotal"" + L.""VatSum"") *
                        (
                          CASE
                            WHEN P.""InstlmntID"" = 1 THEN
                                 CASE
                                   WHEN (P.""InsTotal"" - IFNULL(H.""VatSum"",0)) < 0 THEN 0
                                   ELSE (P.""InsTotal"" - IFNULL(H.""VatSum"",0))
                                 END
                            ELSE P.""InsTotal""
                          END
                          / IFNULL(H.""DocTotal"",0)
                        )
                    END AS ""RBV_Linha"",

                    L.""DiscPrcnt""     AS ""DescontoPctLinha""
                FROM INV6 P
                JOIN OINV H  ON H.""DocEntry"" = P.""DocEntry""
                JOIN INV1 L  ON L.""DocEntry"" = H.""DocEntry""
                JOIN INV12 U ON U.""DocEntry"" = H.""DocEntry""
                JOIN OSLP S  ON S.""SlpCode""  = H.""SlpCode""
                WHERE
                    H.""CANCELED"" = 'N'
                    AND P.""DueDate"" BETWEEN ? AND ?
                    AND H.""SlpCode"" = ?
                    AND IFNULL(S.""Active"", 'N') = 'Y'
                    AND (
                        (? = 1 AND U.""MainUsage"" = '13')
                        OR
                        (? = 0 AND U.""MainUsage"" = '10')
                    )
                    AND IFNULL(H.""SeqCode"", 0) <> 28
                ORDER BY
                    P.""DueDate"", H.""Serial"", P.""InstlmntID"", L.""LineNum"";";

            cmd.Parameters.Add("pIni", OdbcType.Date).Value = ini.Date;
            cmd.Parameters.Add("pFim", OdbcType.Date).Value = fim.Date;
            cmd.Parameters.Add("pSlp", OdbcType.Int).Value = slpCode;
            cmd.Parameters.Add("pIsExpo1", OdbcType.Int).Value = isExportacao ? 1 : 0;
            cmd.Parameters.Add("pIsExpo2", OdbcType.Int).Value = isExportacao ? 1 : 0;

            using var rd = await cmd.ExecuteReaderAsync(ct);

            while (await rd.ReadAsync(ct))
            {
                result.Add(new NotaLinhaHanaRow
                {
                    DataFaturamento = rd.GetDateTime(0).Date, // vencimento
                    NumeroNF = Convert.ToInt32(rd.GetValue(1)),
                    ClienteCodigo = Convert.ToString(rd.GetValue(2)) ?? "",
                    ClienteNome = Convert.ToString(rd.GetValue(3)) ?? "",
                    CondicaoPagamento = rd.IsDBNull(4) ? 0 : Convert.ToInt32(rd.GetValue(4)),
                    RlvLinha = rd.IsDBNull(6) ? 0m : Convert.ToDecimal(rd.GetValue(6)),
                    RbvLinha = rd.IsDBNull(7) ? 0m : Convert.ToDecimal(rd.GetValue(7)),
                    DescontoPctLinha = rd.IsDBNull(8) ? 0m : Convert.ToDecimal(rd.GetValue(8))
                });
            }

            return result;
        }

        // =========================================================
        // BUSCA DEVOLUÇÕES (ORIN/RIN1)
        // =========================================================
        private async Task<List<NotaLinhaHanaRow>> BuscarLinhasDevolucaoAsync(int slpCode, DateTime ini, DateTime fim, CancellationToken ct)
        {
            var result = new List<NotaLinhaHanaRow>();

            using var conn = new OdbcConnection(_hanaConn);
            await conn.OpenAsync(ct);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT
                    H.""DocDate""   AS ""DataDevolucao"",
                    H.""Serial""    AS ""NumeroDev"",
                    H.""CardCode""  AS ""ClienteCodigo"",
                    H.""CardName""  AS ""ClienteNome"",
                    H.""GroupNum""  AS ""CondicaoPagamento"",
                    COALESCE(
                        NULLIF(H.""SlpCode"", 0),
                        INV.""SlpCode"",
                        DLN.""SlpCode"",
                        INV_RR.""SlpCode""
                    ) AS ""SlpCodeEfetivo"",
                    L.""LineNum""   AS ""LineNum"",
                    L.""LineTotal"" AS ""RLV_Linha"",
                    (L.""LineTotal"" + L.""VatSum"") AS ""RBV_Linha"",
                    L.""DiscPrcnt"" AS ""DescontoPctLinha"",
                    IFNULL(H.""DiscPrcnt"", 0) AS ""DescontoPctGeral""
                FROM ORIN H
                JOIN RIN1 L ON L.""DocEntry"" = H.""DocEntry""

                LEFT JOIN OINV INV
                    ON L.""BaseType"" = 13
                   AND INV.""DocEntry"" = L.""BaseEntry""

                LEFT JOIN ODLN DLN
                    ON L.""BaseType"" = 15
                   AND DLN.""DocEntry"" = L.""BaseEntry""

                LEFT JOIN ORRR RR
                    ON L.""BaseType"" = 234000031
                   AND RR.""DocEntry"" = L.""BaseEntry""

                LEFT JOIN RRR1 RR1
                    ON RR1.""DocEntry"" = RR.""DocEntry""
                   AND RR1.""LineNum"" = L.""BaseLine""

                LEFT JOIN OINV INV_RR
                    ON RR1.""BaseType"" = 13
                   AND INV_RR.""DocEntry"" = RR1.""BaseEntry""

                -- Vendedor ativo SAP (considera o SlpCode efetivo)
                JOIN OSLP S
                    ON S.""SlpCode"" = COALESCE(
                        NULLIF(H.""SlpCode"", 0),
                        INV.""SlpCode"",
                        DLN.""SlpCode"",
                        INV_RR.""SlpCode""
                    )
                WHERE
                    H.""CANCELED"" = 'N'
                    AND H.""DocDate"" BETWEEN ? AND ?
                    AND COALESCE(
                        NULLIF(H.""SlpCode"", 0),
                        INV.""SlpCode"",
                        DLN.""SlpCode"",
                        INV_RR.""SlpCode""
                    ) = ?
                    AND IFNULL(S.""Active"", 'N') = 'Y'
                    AND (L.""BaseType"" IN (13, 15, 234000031))
                ORDER BY
                    H.""DocDate"", H.""Serial"", L.""LineNum"";";

            cmd.Parameters.Add("pIni", OdbcType.Date).Value = ini.Date;
            cmd.Parameters.Add("pFim", OdbcType.Date).Value = fim.Date;
            cmd.Parameters.Add("pSlp", OdbcType.Int).Value = slpCode;

            using var rd = await cmd.ExecuteReaderAsync(ct);

            while (await rd.ReadAsync(ct))
            {
                var rlv = rd.IsDBNull(7) ? 0m : Convert.ToDecimal(rd.GetValue(7));
                var rbv = rd.IsDBNull(8) ? 0m : Convert.ToDecimal(rd.GetValue(8));
                var descLinhaPct = rd.IsDBNull(9) ? 0m : Convert.ToDecimal(rd.GetValue(9));
                var descGeralPct = rd.IsDBNull(10) ? 0m : Convert.ToDecimal(rd.GetValue(10));
                var groupNum = rd.IsDBNull(4) ? 0 : Convert.ToInt32(rd.GetValue(4));

                if (descGeralPct > 0m && rlv != 0m)
                {
                    var valorDescGeral = Math.Round(rlv * (descGeralPct / 100m), 2, MidpointRounding.AwayFromZero);
                    rlv -= valorDescGeral;
                    rbv -= valorDescGeral;
                }

                result.Add(new NotaLinhaHanaRow
                {
                    DataFaturamento = rd.GetDateTime(0).Date,
                    NumeroNF = Convert.ToInt32(rd.GetValue(1)),
                    ClienteCodigo = Convert.ToString(rd.GetValue(2)) ?? "",
                    ClienteNome = Convert.ToString(rd.GetValue(3)) ?? "",
                    RlvLinha = rlv,
                    RbvLinha = rbv,
                    DescontoPctLinha = descLinhaPct,
                    CondicaoPagamento = groupNum
                });
            }

            return result;
        }

        // =========================================================
        // DTO interno (linha de documento)
        // =========================================================
        private sealed class NotaLinhaHanaRow
        {
            public DateTime DataFaturamento { get; set; }
            public int NumeroNF { get; set; }
            public string ClienteCodigo { get; set; } = "";
            public string ClienteNome { get; set; } = "";
            public decimal RlvLinha { get; set; }
            public decimal RbvLinha { get; set; }
            public decimal DescontoPctLinha { get; set; }
            public int CondicaoPagamento { get; set; } // GroupNum
        }
    }
}
