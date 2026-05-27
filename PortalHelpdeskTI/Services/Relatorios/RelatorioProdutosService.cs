namespace PortalHelpdeskTI.Services.Relatorios;

using Microsoft.AspNetCore.Hosting; // IWebHostEnvironment
using PortalHelpdeskTI.Infrastructure;
using PortalHelpdeskTI.ViewModels.Relatorios;
using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;

public class RelatorioProdutosService
{
    private readonly IHanaDb _hana;
    private readonly IWebHostEnvironment _env;

    public RelatorioProdutosService(IHanaDb hana, IWebHostEnvironment env)
    {
        _hana = hana;
        _env = env;
    }

    public async Task<DataTable> BuscarDadosFichaTecnica_FromFileAsync(IEnumerable<string> itemCodes, CancellationToken ct = default)
    {
        var codes = (itemCodes ?? Enumerable.Empty<string>())
                    .Select(c => (c ?? "").Trim())
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

        if (codes.Count == 0) return new DataTable();

        // sanitização simples (somente letras, números, _ - .)
        var re = new Regex(@"[^A-Za-z0-9_\-\.]", RegexOptions.Compiled);
        var inList = string.Join(",", codes.Select(c => $"'{re.Replace(c, "").Replace("'", "''")}'"));

        // 1) Lê o arquivo SQL da pasta ConsultasSQL
        var path = ResolveSqlPath("FichaTecnica.sql");
        var baseSql = LoadAndSanitizeSql(await File.ReadAllTextAsync(path, ct));

        // 2) Encapsula a consulta em subselect e aplica o WHERE IN de forma segura
        var finalSql = $@"
            SELECT *
            FROM ({baseSql}) X
            WHERE X.""ItemCode"" IN ({inList})
            ORDER BY X.""ItemCode"";
            ";

        var dt = await _hana.QueryToDataTableAsync(finalSql, null, ct);
        return dt;
    }


    // --- helpers ---
    private string ResolveSqlPath(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(_env.ContentRootPath, "ConsultasSQL", fileName),
            Path.Combine(AppContext.BaseDirectory, "ConsultasSQL", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "ConsultasSQL", fileName),
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        throw new FileNotFoundException(
            $"Arquivo SQL não encontrado: {fileName}\nTestados:\n- {string.Join("\n- ", candidates)}");
    }

    private static string LoadAndSanitizeSql(string sqlText)
    {
        var s = sqlText.Trim();
        if (s.Length > 0 && s[0] == '\uFEFF') s = s.TrimStart('\uFEFF'); // BOM
        while (s.EndsWith(";")) s = s[..^1].TrimEnd();                  // ; final
        if (string.IsNullOrWhiteSpace(s))
            throw new InvalidOperationException("O arquivo SQL está vazio.");
        return s;
    }

    // --- principal: paginação + busca server-side ---
    public async Task<DataTablePage> ExecutarPaginadoAsync(
        int page = 1, int pageSize = 200, string? q = null, CancellationToken ct = default)
    {
        var path = ResolveSqlPath("DadosProdutos.sql");
        var baseSql = LoadAndSanitizeSql(Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path, ct)));

        var searchable = new[]
        {
            "Código",
            "Nome",
            "Cód.Fornecedor",
            "Nome Fornecedor",
            "Grupo",
            "Sub-Grupo",
            "Linha",
            "Substituto",
            "Cód.Barras (Master)",
            "Cód.Barras (Inner)",
            "Cód.Barras(Embalagem)",
            "Cód. Barras (Prod)"
        };

        // WHERE de busca (sem parâmetros; escapando ')
        var hasSearch = !string.IsNullOrWhiteSpace(q);
        string whereSearch = "";
        if (hasSearch)
        {
            var term = q!.Trim().ToUpper().Replace("'", "''");
            whereSearch = "WHERE " + string.Join(" OR ",
                searchable.Select(c => $"UPPER(CAST(T.\"{c}\" AS NVARCHAR(500))) LIKE '%' || '{term}' || '%'"));
        }

        // COUNT (remove ORDER BY terminal)
        var countSource = Regex.Replace(
            baseSql, @"\border\s+by\b[\s\S]*?$", "",
            RegexOptions.IgnoreCase | RegexOptions.Multiline
        ).TrimEnd();
        if (string.IsNullOrWhiteSpace(countSource)) countSource = baseSql;

        var countSql = $@"SELECT COUNT(1) AS TOTAL FROM ({countSource}) T {whereSearch}";
        var countDt = await _hana.QueryToDataTableAsync(countSql, null, ct);
        var total = Convert.ToInt32(countDt.Rows[0]["TOTAL"]);

        // Página/offset
        if (page < 1) page = 1;
        if (pageSize <= 0) pageSize = 200;
        var offset = (page - 1) * pageSize;

        // ORDER BY determinístico na paginação
        var hasOrder = Regex.IsMatch(baseSql, @"\border\s+by\b", RegexOptions.IgnoreCase);
        var ordered = hasOrder ? baseSql : $"{baseSql}\nORDER BY 1";

        var pagedSql = $@"SELECT * FROM ({ordered}) T {whereSearch} LIMIT {pageSize} OFFSET {offset}";
        var dt = await _hana.QueryToDataTableAsync(pagedSql, null, ct);

        return new DataTablePage
        {
            Data = dt,
            Page = page,
            PageSize = pageSize,
            Total = total,
            Search = q
        };
    }

    // --- export (sem paginação) ---
    public async Task<DataTable> ExecutarAsync(CancellationToken ct = default)
    {
        var path = ResolveSqlPath("DadosProdutos.sql");
        var sql = LoadAndSanitizeSql(await File.ReadAllTextAsync(path, ct));
        var dt = await _hana.QueryToDataTableAsync(sql, null, ct);
        return dt;
    }
    public async Task<DataTable> BuscarDadosFichaTecnicaAsync(IEnumerable<string> itemCodes, CancellationToken ct = default)
    {
        var codes = (itemCodes ?? Enumerable.Empty<string>())
                    .Select(c => (c ?? "").Trim())
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

        if (codes.Count == 0) return new DataTable();

        // sanitiza: só letras/números/_-/.
        var re = new Regex(@"[^A-Za-z0-9_\-\.]", RegexOptions.Compiled);
        var inList = string.Join(",", codes.Select(c => $"'{re.Replace(c, "").Replace("'", "''")}'"));

        var sql = $@"
        SELECT T0.""ItemCode"", T0.""ItemName"", T2.""Name"", T3.""NcmCode"", T0.""SalUnitMsr"",
               T0.""U_ProdCodBarras"", T0.""U_ProdAltura"", T0.""U_ProdLargura"", T0.""U_ProdComprimento"",
               T0.""U_EmbCodBarras"",  T0.""U_EmbAltura"",  T0.""U_EmbLargura"",  T0.""U_EmbComprimento"",
               T0.""U_EmbPeso"", T0.""U_EmbPesoLiq"",
               T0.""U_InnerCodBarras"", T0.""U_QdeInner"", T0.""U_InnerAltura"", T0.""U_InnerLargura"", T0.""U_InnerComprimento"", T0.""U_InnerPeso"", T0.""U_InnerPesoLiq"",
               T0.""U_MasterCodBarras"", T0.""U_QdeMaster"", T0.""U_MasterAltura"", T0.""U_MasterLargura"", T0.""U_MasterComprimento"", T0.""U_MasterPeso"", T0.""U_MasterPesoLiq""
        FROM ""SBO_BRW_PRD"".""OITM"" T0
        INNER JOIN ITM10 T1 ON T0.""ItemCode"" = T1.""ItemCode""
        INNER JOIN OCRY T2 ON T1.""ISOriCntry"" = T2.""Code""
        INNER JOIN ONCM T3 ON T0.""NCMCode"" = T3.""AbsEntry""
        WHERE T0.""ItemCode"" IN ({inList})
        ORDER BY T0.""ItemCode"";
        ";
        // Observação: se quiser, mova este SELECT para ConsultasSQL/FichaTecnica.sql

        var dt = await _hana.QueryToDataTableAsync(sql, null, ct);
        return dt;
    }

}
