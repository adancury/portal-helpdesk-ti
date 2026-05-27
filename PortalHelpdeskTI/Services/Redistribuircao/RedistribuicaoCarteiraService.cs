using System.Data;
using System.Data.Odbc;
using PortalHelpdeskTI.ViewModels.Redistribuicao;
using ClosedXML.Excel;

namespace PortalHelpdeskTI.Services;

public class RedistribuicaoCarteiraService
{
    private readonly IConfiguration _cfg;

    public RedistribuicaoCarteiraService(IConfiguration cfg)
    {
        _cfg = cfg;
    }

    private OdbcConnection CreateConnection()
    {
        var cs = _cfg.GetConnectionString("HanaConn");
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("ConnectionString 'HanaConn' não encontrada em appsettings.json.");

        return new OdbcConnection(cs);
    }

    public async Task<RedistribuicaoCarteiraResultadoVm> BuscarAsync(RedistribuicaoCarteiraFiltroVm filtros, CancellationToken ct)
    {
        var sql = BuildSql(filtros);

        var result = new RedistribuicaoCarteiraResultadoVm { Filtros = filtros };

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        using var cmd = new OdbcCommand(sql, conn)
        {
            CommandType = CommandType.Text
        };

        using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var item = new ClienteRedistribuicaoVm
            {
                CodPN = reader["CodPN"]?.ToString() ?? "",
                NomeFantasia = reader["NomeFantasia"]?.ToString(),
                RazaoSocial = reader["RazaoSocial"]?.ToString(),
                Cnpj = reader["CNPJ"]?.ToString(),
                Estado = reader["Estado"]?.ToString(),

                CadastroPendente = reader["CadastroPendente"]?.ToString() ?? "N",
                MotivoCadastroPendente = reader["MotivoCadastroPendente"]?.ToString(),

                UltimaVenda = reader["UltimaVenda"] is DBNull ? null : Convert.ToDateTime(reader["UltimaVenda"]),
                Status = reader["Status"]?.ToString() ?? "",
                KpiTkm = reader["KpiTkm"] is DBNull ? 0m : Convert.ToDecimal(reader["KpiTkm"]),

                SlpCodeAtual = reader["SlpCodeAtual"] is DBNull ? null : Convert.ToInt32(reader["SlpCodeAtual"]),
                VendedorAtualNome = FormatarNomeVendedor(reader["VendedorAtualNome"]?.ToString()),
                Ativo = reader["Ativo"]?.ToString()
            };
            var nomeAtual = FormatarNomeVendedor(reader["VendedorAtualNome"]?.ToString());
            item.VendedorAtualNome = string.IsNullOrWhiteSpace(nomeAtual) ? "Sem vendedor" : nomeAtual;

            if (string.Equals(item.CadastroPendente, "Y", StringComparison.OrdinalIgnoreCase))
                result.Pendentes.Add(item);
            else
                result.Elegiveis.Add(item);
        }

        return result;
    }

    private static string BuildSql(RedistribuicaoCarteiraFiltroVm f)
    {
        var mesesLead = f.MesesLead;
        var janelaTkm = f.JanelaTkmMeses;

        // ✅ Regra centralizada no VM
        var diasInativo = Math.Max(30, Math.Min(3650, f.DiasInatividadeCalculado));

        var filtroAtivos = f.IncluirSomenteAtivos ? "AND C.\"validFor\" = 'Y'" : "";

        return $@"
    WITH BP AS (
        SELECT
            C.""CardCode"",
            C.""CardName""      AS ""NomeFantasia"",
            C.""CardFName""     AS ""RazaoSocial"",
            C.""LicTradNum""    AS ""CNPJ"",
            C.""CreateDate""    AS ""DataCadastro"",
            IFNULL(C.""U_SegVendedor"", C.""SlpCode"") AS ""SlpCodeAtual"",
            C.""validFor""      AS ""Ativo"",
            C.""ShipToDef"",
            C.""BillToDef""
        FROM ""SBO_BRW_PRD"".""OCRD"" C
        WHERE C.""CardType"" = 'C'
        {filtroAtivos}
    ),
    ADDR AS (
        SELECT B.""CardCode"", A.""State"" AS ""UF"", 1 AS ""prio""
        FROM BP B
        JOIN ""SBO_BRW_PRD"".""CRD1"" A
          ON A.""CardCode"" = B.""CardCode""
         AND A.""AdresType"" = 'S'
         AND A.""Address"" = B.""ShipToDef""
        WHERE IFNULL(A.""State"",'') <> ''

        UNION ALL

        SELECT B.""CardCode"", A.""State"" AS ""UF"", 2 AS ""prio""
        FROM BP B
        JOIN ""SBO_BRW_PRD"".""CRD1"" A
          ON A.""CardCode"" = B.""CardCode""
         AND A.""AdresType"" = 'B'
         AND A.""Address"" = B.""BillToDef""
        WHERE IFNULL(A.""State"",'') <> ''

        UNION ALL

        SELECT A.""CardCode"", A.""State"" AS ""UF"", 3 AS ""prio""
        FROM ""SBO_BRW_PRD"".""CRD1"" A
        WHERE A.""AdresType"" = 'S'
          AND IFNULL(A.""State"",'') <> ''

        UNION ALL

        SELECT A.""CardCode"", A.""State"" AS ""UF"", 4 AS ""prio""
        FROM ""SBO_BRW_PRD"".""CRD1"" A
        WHERE A.""AdresType"" = 'B'
          AND IFNULL(A.""State"",'') <> ''
    ),
    ADDR_ONE AS (
        SELECT
            ""CardCode"",
            UPPER(TRIM(""UF"")) AS ""UF""
        FROM (
            SELECT
                ""CardCode"",
                ""UF"",
                ROW_NUMBER() OVER (PARTITION BY ""CardCode"" ORDER BY ""prio"") AS rn
            FROM ADDR
        )
        WHERE rn = 1
    ),
    INV_12M AS (
        SELECT
            I.""CardCode"",
            MAX(I.""DocDate"") AS ""UltimaVenda"",
            COUNT(*)            AS ""QtdNotas12M"",
            SUM(I.""DocTotal"") AS ""Total12M""
        FROM ""SBO_BRW_PRD"".""OINV"" I
        WHERE I.""CANCELED"" = 'N'
          AND I.""DocDate"" >= ADD_MONTHS(CURRENT_DATE, -{janelaTkm})
        GROUP BY I.""CardCode""
    ),
    INV_ALL AS (
        SELECT
            I.""CardCode"",
            MAX(I.""DocDate"") AS ""UltimaVendaAllTime""
        FROM ""SBO_BRW_PRD"".""OINV"" I
        WHERE I.""CANCELED"" = 'N'
        GROUP BY I.""CardCode""
    ),
    INV_ALL_TKM AS (
        SELECT
            I.""CardCode"",
            COUNT(*)            AS ""QtdNotasAll"",
            SUM(I.""DocTotal"") AS ""TotalAll""
        FROM ""SBO_BRW_PRD"".""OINV"" I
        WHERE I.""CANCELED"" = 'N'
        GROUP BY I.""CardCode""
    )
    SELECT
        B.""CardCode"" AS CodPN,
        B.""NomeFantasia"",
        B.""RazaoSocial"",
        B.""CNPJ"",
        A1.""UF"" AS Estado,

        CASE
          WHEN A1.""UF"" IS NULL OR LENGTH(TRIM(A1.""UF"")) = 0 THEN 'Y'
          ELSE 'N'
        END AS CadastroPendente,

        CASE
          WHEN A1.""UF"" IS NULL OR LENGTH(TRIM(A1.""UF"")) = 0 THEN 'EnderecoSemUF'
          ELSE NULL
        END AS MotivoCadastroPendente,

        IA.""UltimaVendaAllTime"" AS UltimaVenda,

        CASE
          WHEN IA.""UltimaVendaAllTime"" IS NULL
               AND B.""DataCadastro"" <= ADD_MONTHS(CURRENT_DATE, -{mesesLead})
            THEN 'Lead'
          WHEN IA.""UltimaVendaAllTime"" IS NOT NULL
               AND IA.""UltimaVendaAllTime"" < ADD_DAYS(CURRENT_DATE, -{diasInativo})
            THEN 'Inativo'
          ELSE 'Ativo'
        END AS Status,

        CASE
          WHEN IA.""UltimaVendaAllTime"" IS NULL THEN 0
          WHEN IFNULL(I12.""QtdNotas12M"", 0) > 0 THEN ROUND(I12.""Total12M"" / NULLIF(I12.""QtdNotas12M"", 0), 2)
          WHEN IFNULL(IALL.""QtdNotasAll"", 0) > 0 THEN ROUND(IALL.""TotalAll"" / NULLIF(IALL.""QtdNotasAll"", 0), 2)
          ELSE 0
        END AS KpiTkm,

        B.""SlpCodeAtual"",
        SA.""SlpName"" AS VendedorAtualNome,
        B.""Ativo""
    FROM BP B
    LEFT JOIN ADDR_ONE A1 ON A1.""CardCode"" = B.""CardCode""
    LEFT JOIN INV_12M I12 ON I12.""CardCode"" = B.""CardCode""
    LEFT JOIN INV_ALL IA  ON IA.""CardCode"" = B.""CardCode""
    LEFT JOIN INV_ALL_TKM IALL ON IALL.""CardCode"" = B.""CardCode""
    LEFT JOIN ""SBO_BRW_PRD"".""OSLP"" SA ON SA.""SlpCode"" = B.""SlpCodeAtual""
    WHERE
    (
      (IA.""UltimaVendaAllTime"" IS NULL AND B.""DataCadastro"" <= ADD_MONTHS(CURRENT_DATE, -{mesesLead}))
      OR
      (IA.""UltimaVendaAllTime"" IS NOT NULL AND IA.""UltimaVendaAllTime"" < ADD_DAYS(CURRENT_DATE, -{diasInativo}))
    )
    ORDER BY
      CadastroPendente DESC,
      Estado,
      KpiTkm DESC
    ";
    }
    public async Task<List<VendedorVm>> BuscarVendedoresAsync(CancellationToken ct)
    {
        const string sql = @"
        SELECT
            S.""SlpCode"",
            S.""SlpName"",
            S.""Email""
        FROM ""SBO_BRW_PRD"".""OSLP"" S
        WHERE S.""Locked"" = 'N'
          AND IFNULL(S.""Email"", '') <> ''
          AND LOWER(S.""Email"") LIKE 'vendas%@brwsuprimentos.com.br'
          AND S.""Active"" = 'Y'
        ORDER BY S.""SlpCode"";
        ";

        var list = new List<VendedorVm>();

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        using var cmd = new OdbcCommand(sql, conn);
        using var rd = await cmd.ExecuteReaderAsync(ct);

        while (await rd.ReadAsync(ct))
        {
            list.Add(new VendedorVm
            {
                SlpCode = Convert.ToInt32(rd["SlpCode"]),
                SlpName = FormatarNomeVendedor(rd["SlpName"]?.ToString()),
                Email = rd["Email"]?.ToString() ?? ""
            });
        }

        return list;
    }

    public (List<ClienteRedistribuicaoSimuladoVm> simulacao, List<ResumoVendedorRedistribuicaoVm> resumo)
        SimularRedistribuicao(List<ClienteRedistribuicaoVm> elegiveis, List<VendedorVm> vendedores)
    {
        if (vendedores == null || vendedores.Count == 0)
            throw new InvalidOperationException("Nenhum vendedor encontrado para simulação.");

        elegiveis ??= new();

        foreach (var c in elegiveis)
            c.Estado = string.IsNullOrWhiteSpace(c.Estado) ? "SEM_UF" : c.Estado.Trim().ToUpperInvariant();

        var carga = vendedores.ToDictionary(v => v.SlpCode, _ => 0m);
        var qtdClientes = vendedores.ToDictionary(v => v.SlpCode, _ => 0);
        var qtdUf = vendedores.ToDictionary(v => v.SlpCode, _ => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

        var grupos = elegiveis
            .GroupBy(c => c.Estado!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.KpiTkm).ToList(), StringComparer.OrdinalIgnoreCase);

        var nomeVendedor = vendedores.ToDictionary(v => v.SlpCode, v => FormatarNomeVendedor(v.SlpName));

        var sim = new List<ClienteRedistribuicaoSimuladoVm>(elegiveis.Count);

        foreach (var (uf, lista) in grupos)
        {
            foreach (var cli in lista)
            {
                var peso = cli.Status.Equals("Lead", StringComparison.OrdinalIgnoreCase) ? 0m : cli.KpiTkm;

                // candidatos = todos menos o vendedor atual (quando possível)
                var vendedoresCandidatos = vendedores
                    .Where(v => !cli.SlpCodeAtual.HasValue || v.SlpCode != cli.SlpCodeAtual.Value)
                    .ToList();

                // fallback: se por algum motivo não sobrou ninguém (ex.: só 1 vendedor na lista), volta para todos
                if (vendedoresCandidatos.Count == 0)
                    vendedoresCandidatos = vendedores;

                int escolhido = vendedoresCandidatos
                    .Select(v =>
                    {
                        qtdUf[v.SlpCode].TryGetValue(uf, out var countUf);
                        return new
                        {
                            v.SlpCode,
                            qtd = qtdClientes[v.SlpCode],
                            carga = carga[v.SlpCode],
                            countUf
                        };
                    })
                    .OrderBy(x => x.qtd)       // 1) QUANTIDADE
                    .ThenBy(x => x.carga)      // 2) PESO (TkM)
                    .ThenBy(x => x.countUf)    // 3) DIVERSIDADE por UF
                    .ThenBy(x => HashEstavel(cli.CodPN, x.SlpCode))
                    .First()
                    .SlpCode;


                sim.Add(new ClienteRedistribuicaoSimuladoVm
                {
                    CodPN = cli.CodPN,
                    NomeFantasia = cli.NomeFantasia,
                    RazaoSocial = cli.RazaoSocial,
                    Cnpj = cli.Cnpj,
                    Estado = uf,
                    CadastroPendente = cli.CadastroPendente,
                    MotivoCadastroPendente = cli.MotivoCadastroPendente,
                    UltimaVenda = cli.UltimaVenda,
                    Status = cli.Status,
                    KpiTkm = cli.KpiTkm,
                    SlpCodeAtual = cli.SlpCodeAtual,
                    VendedorAtualNome = cli.VendedorAtualNome,   // ✅ AQUI
                    Ativo = cli.Ativo,
                    SlpCodeNovo = escolhido,
                    VendedorNovoNome = nomeVendedor.TryGetValue(escolhido, out var n) ? n : null
                });

                qtdClientes[escolhido] += 1;
                carga[escolhido] += peso;

                if (!qtdUf[escolhido].TryGetValue(uf, out var nuf)) nuf = 0;
                qtdUf[escolhido][uf] = nuf + 1;
            }
        }

        // Mapa vendedor por SlpCode (evita First() em loop)
        var vendedorPorCodigo = vendedores.ToDictionary(v => v.SlpCode, v => v);

        var resumo = sim
            .GroupBy(x => x.SlpCodeNovo)
            .Select(g =>
            {
                var ufs = g.Select(x => x.Estado ?? "SEM_UF").Distinct(StringComparer.OrdinalIgnoreCase).Count();
                var soma = g.Where(x => !string.Equals(x.Status, "Lead", StringComparison.OrdinalIgnoreCase))
                            .Sum(x => x.KpiTkm);

                var vendedor = vendedorPorCodigo[g.Key];

                return new ResumoVendedorRedistribuicaoVm
                {
                    SlpCode = g.Key,
                    SlpName = FormatarNomeVendedor(vendedor.SlpName),
                    Email = vendedor.Email,
                    QtdClientes = g.Count(),
                    SomaKpiTkm = soma,
                    QtdUFs = ufs
                };
            })
            // ✅ ORDEM: vendas1..vendas30
            .OrderBy(x => OrdemEmailVendas(x.Email))
            .ThenBy(x => x.SlpCode)
            .ToList();

        return (sim, resumo);
    }

    private static string FormatarNomeVendedor(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
            return string.Empty;

        var partes = nome
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.ToLowerInvariant())
            .ToList();

        if (partes.Count == 1)
            return Capitalizar(partes[0]);

        var primeiro = Capitalizar(partes.First());
        var ultimo = Capitalizar(partes.Last());

        return $"{primeiro} {ultimo}";
    }

    private static string Capitalizar(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return texto;

        return char.ToUpperInvariant(texto[0]) + texto.Substring(1);
    }

    public byte[] GerarExcelResumo(List<ResumoVendedorRedistribuicaoVm> resumo, RedistribuicaoCarteiraFiltroVm filtros)
    {
        resumo ??= new();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Resumo");

        // Cabeçalho
        ws.Cell(1, 1).Value = "Resumo da Redistribuição (Simulação)";
        ws.Range(1, 1, 1, 5).Merge().Style.Font.SetBold().Font.SetFontSize(14);

        // Parâmetros
        ws.Cell(2, 1).Value = $"Lead (meses): {filtros.MesesLead} | Inativo (meses): {filtros.MesesInativo} | Janela TkM (meses): {filtros.JanelaTkmMeses}";
        ws.Range(2, 1, 2, 5).Merge().Style.Font.SetFontSize(10).Font.SetFontColor(XLColor.Gray);

        // Tabela
        var row = 4;

        ws.Cell(row, 1).Value = "Vendedor";
        ws.Cell(row, 2).Value = "Email";
        ws.Cell(row, 3).Value = "SlpCode";
        ws.Cell(row, 4).Value = "Contagem de CÓD";
        ws.Cell(row, 5).Value = "Soma de TKT MÉDIO";

        ws.Range(row, 1, row, 5).Style.Font.SetBold();
        ws.Range(row, 1, row, 5).Style.Fill.SetBackgroundColor(XLColor.LightGray);

        row++;

        // ✅ ORDEM: vendas1..vendas30
        foreach (var r in resumo
            .OrderBy(x => OrdemEmailVendas(x.Email))
            .ThenBy(x => x.SlpCode))
        {
            ws.Cell(row, 1).Value = r.SlpName;
            ws.Cell(row, 2).Value = r.Email;
            ws.Cell(row, 3).Value = r.SlpCode;
            ws.Cell(row, 4).Value = r.QtdClientes;
            ws.Cell(row, 5).Value = r.SomaKpiTkm;
            row++;
        }

        // ✅ TOTAL GERAL (como no modal/print)
        var totalQtd = resumo.Sum(x => x.QtdClientes);
        var totalSoma = resumo.Sum(x => x.SomaKpiTkm);

        ws.Cell(row, 1).Value = "Total Geral";
        ws.Range(row, 1, row, 3).Merge();
        ws.Cell(row, 4).Value = totalQtd;
        ws.Cell(row, 5).Value = totalSoma;

        ws.Range(row, 1, row, 5).Style.Font.SetBold();
        ws.Range(row, 1, row, 5).Style.Fill.SetBackgroundColor(XLColor.LightBlue);

        // Formatação
        ws.Column(3).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
        ws.Column(4).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

        // Se quiser com "R$" no Excel, troque para: "R$ #,##0.00"
        ws.Column(5).Style.NumberFormat.Format = "\"R$\" #,##0.00";
        ws.Cell(row, 5).Style.NumberFormat.Format = "\"R$\" #,##0.00";

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // vendas1..vendas30 (se não tiver número vai pro fim)
    private static int OrdemEmailVendas(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return int.MaxValue;

        email = email.Trim().ToLowerInvariant();
        var user = email.Split('@')[0];

        var digits = new string(user.Where(char.IsDigit).ToArray());
        if (int.TryParse(digits, out var n)) return n;

        return int.MaxValue;
    }
    /*public byte[] GerarExcelCarteira(RedistribuicaoCarteiraResultadoVm vm, RedistribuicaoCarteiraFiltroVm filtros)
    {
        using var wb = new XLWorkbook();

        // Aba 1: Carteira (principal)
        var ws = wb.Worksheets.Add("Carteira");

        ws.Cell(1, 1).Value = "Carteira - Elegíveis + Simulação";
        ws.Range(1, 1, 1, 10).Merge().Style.Font.SetBold().Font.SetFontSize(14);

        ws.Cell(2, 1).Value = $"Lead (meses): {filtros.MesesLead} | Inativo (meses): {filtros.MesesInativo} | Janela TkM (meses): {filtros.JanelaTkmMeses}";
        ws.Range(2, 1, 2, 10).Merge().Style.Font.SetFontSize(10).Font.SetFontColor(XLColor.Gray);

        var row = 4;

        // Cabeçalho
        ws.Cell(row, 1).Value = "CodPN";
        ws.Cell(row, 2).Value = "Nome";
        ws.Cell(row, 3).Value = "UF";
        ws.Cell(row, 4).Value = "Status";
        ws.Cell(row, 5).Value = "Kpi TkM";
        ws.Cell(row, 6).Value = "Vendedor Atual (Slp)";
        ws.Cell(row, 7).Value = "Slp Novo";
        ws.Cell(row, 8).Value = "Novo Vendedor";
        ws.Cell(row, 9).Value = "Ativo";
        ws.Cell(row, 10).Value = "Última Venda";

        ws.Range(row, 1, row, 10).Style.Font.SetBold();
        ws.Range(row, 1, row, 10).Style.Fill.SetBackgroundColor(XLColor.LightGray);
        row++;

        var temSimulacao = vm.Simulacao != null && vm.Simulacao.Count > 0;

        if (temSimulacao)
        {
            foreach (var c in vm.Simulacao)
            {
                ws.Cell(row, 1).Value = c.CodPN;
                ws.Cell(row, 2).Value = c.NomeFantasia;
                ws.Cell(row, 3).Value = c.Estado;
                ws.Cell(row, 4).Value = c.Status;
                ws.Cell(row, 5).Value = c.KpiTkm;
                ws.Cell(row, 6).Value = c.SlpCodeAtual;
                ws.Cell(row, 7).Value = c.SlpCodeNovo;
                ws.Cell(row, 8).Value = c.VendedorNovoNome;
                ws.Cell(row, 9).Value = c.Ativo;
                ws.Cell(row, 10).Value = c.UltimaVenda.HasValue
                    ? c.UltimaVenda.Value.ToString("dd/MM/yyyy")
                    : "Sem Vendas";
                row++;
            }
        }
        else
        {
            foreach (var c in vm.Elegiveis)
            {
                ws.Cell(row, 1).Value = c.CodPN;
                ws.Cell(row, 2).Value = c.NomeFantasia;
                ws.Cell(row, 3).Value = c.Estado;
                ws.Cell(row, 4).Value = c.Status;
                ws.Cell(row, 5).Value = c.KpiTkm;
                ws.Cell(row, 6).Value = c.SlpCodeAtual;
                ws.Cell(row, 7).Value = "";   // sem simulação
                ws.Cell(row, 8).Value = "";
                ws.Cell(row, 9).Value = c.Ativo;
                ws.Cell(row, 10).Value = c.UltimaVenda?.ToString("yyyy-MM-dd");
                row++;
            }
        }

        // Formatação: TkM como moeda
        ws.Column(5).Style.NumberFormat.Format = "\"R$\" #,##0.00";
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }*/

    public byte[] GerarExcelCarteira(RedistribuicaoCarteiraResultadoVm vm, RedistribuicaoCarteiraFiltroVm filtros)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Carteira");

        var temSimulacao = vm.Simulacao != null && vm.Simulacao.Count > 0;

        var totalCols = temSimulacao ? 13 : 11;

        ws.Cell(1, 1).Value = "Carteira - Elegíveis";
        ws.Range(1, 1, 1, totalCols).Merge().Style.Font.SetBold().Font.SetFontSize(14);

        ws.Cell(2, 1).Value =
            $"Lead (meses): {filtros.MesesLead} | Inativo (dias): {filtros.DiasInativo} | Janela TkM (meses): {filtros.JanelaTkmMeses}";
        ws.Range(2, 1, 2, totalCols).Merge().Style.Font.SetFontSize(10).Font.SetFontColor(XLColor.Gray);

        var row = 4;
        var col = 1;

        ws.Cell(row, col++).Value = "CodPN";
        ws.Cell(row, col++).Value = "Nome Fantasia";
        ws.Cell(row, col++).Value = "Razão Social";
        ws.Cell(row, col++).Value = "CNPJ";
        ws.Cell(row, col++).Value = "UF";
        ws.Cell(row, col++).Value = "Status";
        ws.Cell(row, col++).Value = "Kpi TkM";
        ws.Cell(row, col++).Value = "Slp Atual";
        ws.Cell(row, col++).Value = "Vendedor Atual";
        ws.Cell(row, col++).Value = "Ativo";
        ws.Cell(row, col++).Value = "Última Venda";

        if (temSimulacao)
        {
            ws.Cell(row, col++).Value = "Slp Novo";
            ws.Cell(row, col++).Value = "Novo Vendedor";
        }

        ws.Range(row, 1, row, totalCols).Style.Font.SetBold();
        ws.Range(row, 1, row, totalCols).Style.Fill.SetBackgroundColor(XLColor.LightGray);

        row++;

        if (temSimulacao)
        {
            foreach (var c in vm.Simulacao)
            {
                col = 1;
                ws.Cell(row, col++).Value = c.CodPN;
                ws.Cell(row, col++).Value = c.NomeFantasia;
                ws.Cell(row, col++).Value = c.RazaoSocial;
                ws.Cell(row, col++).Value = c.Cnpj;
                ws.Cell(row, col++).Value = c.Estado;
                ws.Cell(row, col++).Value = c.Status;
                ws.Cell(row, col++).Value = c.KpiTkm;
                ws.Cell(row, col++).Value = c.SlpCodeAtual;
                ws.Cell(row, col++).Value = c.VendedorAtualNome;
                ws.Cell(row, col++).Value = c.Ativo;
                ws.Cell(row, col++).Value = c.UltimaVenda.HasValue
                    ? c.UltimaVenda.Value.ToString("dd/MM/yyyy")
                    : "Sem vendas";
                ws.Cell(row, col++).Value = c.SlpCodeNovo;
                ws.Cell(row, col++).Value = c.VendedorNovoNome;
                row++;
            }
        }
        else
        {
            foreach (var c in vm.Elegiveis)
            {
                col = 1;
                ws.Cell(row, col++).Value = c.CodPN;
                ws.Cell(row, col++).Value = c.NomeFantasia;
                ws.Cell(row, col++).Value = c.RazaoSocial;
                ws.Cell(row, col++).Value = c.Cnpj;
                ws.Cell(row, col++).Value = c.Estado;
                ws.Cell(row, col++).Value = c.Status;
                ws.Cell(row, col++).Value = c.KpiTkm;
                ws.Cell(row, col++).Value = c.SlpCodeAtual;
                ws.Cell(row, col++).Value = c.VendedorAtualNome;
                ws.Cell(row, col++).Value = c.Ativo;
                ws.Cell(row, col++).Value = c.UltimaVenda.HasValue
                    ? c.UltimaVenda.Value.ToString("dd/MM/yyyy")
                    : "Sem vendas";
                row++;
            }
        }

        ws.Column(7).Style.NumberFormat.Format = "\"R$\" #,##0.00";
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<List<(string CodPN, int SlpCodeNovo, bool Ok, string? Erro)>> AtualizarSegVendedorEmLoteAsync(
    IEnumerable<(string CodPN, int SlpCodeNovo)> itens,
    CancellationToken ct)
    {
        var lista = itens
            .Where(x => !string.IsNullOrWhiteSpace(x.CodPN) && x.SlpCodeNovo > 0)
            .Select(x => (CodPN: x.CodPN.Trim().ToUpperInvariant(), x.SlpCodeNovo))
            .GroupBy(x => x.CodPN)
            .Select(g => g.Last())
            .ToList();

        var retorno = new List<(string CodPN, int SlpCodeNovo, bool Ok, string? Erro)>();

        if (lista.Count == 0)
            return retorno;

        const int tamanhoLote = 500;

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        using var tx = conn.BeginTransaction();

        try
        {
            for (int i = 0; i < lista.Count; i += tamanhoLote)
            {
                ct.ThrowIfCancellationRequested();

                var lote = lista.Skip(i).Take(tamanhoLote).ToList();

                var valores = string.Join(" UNION ALL ",
                    lote.Select(x => $@"SELECT '{EscapeSql(x.CodPN)}' AS ""CardCode"", {x.SlpCodeNovo} AS ""SlpCodeNovo"" FROM DUMMY"));

                // 1) Buscar quais realmente precisam atualizar
                var sqlSelect = $@"
WITH NOVOS AS (
    {valores}
)
SELECT
    T.""CardCode"",
    N.""SlpCodeNovo""
FROM ""SBO_BRW_PRD"".""OCRD"" T
JOIN NOVOS N
    ON N.""CardCode"" = T.""CardCode""
WHERE IFNULL(T.""U_SegVendedor"", -1) <> N.""SlpCodeNovo""
";

                var alteradosNoLote = new List<(string CodPN, int SlpCodeNovo)>();

                using (var cmdSelect = new OdbcCommand(sqlSelect, conn, tx))
                {
                    cmdSelect.CommandType = CommandType.Text;

                    using var reader = await cmdSelect.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct))
                    {
                        alteradosNoLote.Add((
                            reader["CardCode"]?.ToString() ?? "",
                            Convert.ToInt32(reader["SlpCodeNovo"])
                        ));
                    }
                }

                if (alteradosNoLote.Count == 0)
                    continue;

                // 2) Atualizar somente os que realmente precisam
                var sqlUpdate = new System.Text.StringBuilder();
                sqlUpdate.AppendLine(@"UPDATE ""SBO_BRW_PRD"".""OCRD""");
                sqlUpdate.AppendLine(@"SET ""U_SegVendedor"" = CASE ""CardCode""");

                foreach (var item in alteradosNoLote)
                    sqlUpdate.AppendLine($@"    WHEN '{EscapeSql(item.CodPN)}' THEN {item.SlpCodeNovo}");

                sqlUpdate.AppendLine(@"    ELSE ""U_SegVendedor""");
                sqlUpdate.AppendLine(@"END");
                sqlUpdate.AppendLine($@"WHERE ""CardCode"" IN ({string.Join(", ", alteradosNoLote.Select(x => $"'{EscapeSql(x.CodPN)}'"))})");

                using (var cmdUpdate = new OdbcCommand(sqlUpdate.ToString(), conn, tx))
                {
                    cmdUpdate.CommandType = CommandType.Text;
                    await cmdUpdate.ExecuteNonQueryAsync(ct);
                }

                // 3) Retornar exatamente os que foram alterados
                retorno.AddRange(alteradosNoLote.Select(x => (
                    x.CodPN,
                    x.SlpCodeNovo,
                    true,
                    (string?)null
                )));
            }

            tx.Commit();
        }
        catch (Exception ex)
        {
            try { tx.Rollback(); } catch { }

            retorno.Clear();
            retorno.AddRange(lista.Select(x => (
                x.CodPN,
                x.SlpCodeNovo,
                false,
                ex.Message
            )));
        }

        return retorno;
    }
    private static string EscapeSql(string value)
    {
        return (value ?? string.Empty).Replace("'", "''");
    }

    /*HELPRES*/
    private static int HashEstavel(string codpn, int slpCode)
    {
        unchecked
        {
            int h = 17;
            foreach (var ch in codpn ?? "")
                h = (h * 31) + ch;
            h = (h * 31) + slpCode;
            return h;
        }
    }
    /*FIM HELPES*/
}
