// ===============================
// SERVICES/RELATORIOS/RepresentantesVendasService.cs
// ===============================
using Microsoft.AspNetCore.Hosting; // IWebHostEnvironment
using PortalHelpdeskTI.Infrastructure; // IHanaDb (ODBC)
using PortalHelpdeskTI.ViewModels.Relatorios; // DataTablePage
using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;

namespace PortalHelpdeskTI.Services.Relatorios
{
    public class RepresentantesVendasService
    {
        private readonly IHanaDb _hana;                  // ODBC, igual ao RelatorioProdutosService
        private readonly IWebHostEnvironment _env;

        public RepresentantesVendasService(IHanaDb hana, IWebHostEnvironment env)
        {
            _hana = hana;
            _env = env;
        }

        // Export completo (sem paginação)
        public async Task<DataTable> GerarAsync(CancellationToken ct = default)
        {
            var path = ResolveSqlPath("CadRepresentantes.sql");
            var sql = LoadAndSanitizeSql(await File.ReadAllTextAsync(path, ct));
            var dt = await _hana.QueryToDataTableAsync(sql, null, ct);
            return dt;
        }

        // Paginação + busca server-side (padrão DadosProdutos)
        public async Task<DataTablePage> ExecutarPaginadoAsync(
            int page = 1, int pageSize = 20, string? q = null, CancellationToken ct = default)
        {
            // 1) Lê a consulta base do arquivo
            var path = ResolveSqlPath("CadRepresentantes.sql");
            var baseSql = LoadAndSanitizeSql(Encoding.GetEncoding("ISO-8859-1").GetString(await File.ReadAllBytesAsync(path, ct)));

            // 2) Campos pesquisáveis existentes NO SELECT final do CadRepresentantes.sql
            var searchable = new[]
            {
                "CardCode",   // código do representante
                "Nome",       // INITCAP("CardName") AS "Nome"
                "Telefone",
                "LicTradNum",
                "E_Mail",
                "Endereço",   // com acento, igual ao alias do SQL
                "Ativo"
            };

            // 3) WHERE de busca
            var hasSearch = !string.IsNullOrWhiteSpace(q);
            string whereSearch = "";
            if (hasSearch)
            {
                var term = q!.Trim().ToUpper().Replace("'", "''");

                whereSearch = "WHERE " + string.Join(" OR ",
                    searchable.Select(c =>
                        // note: c SEM aspas; aspas são adicionadas aqui
                        $"UPPER(CAST(T.\"{c}\" AS NVARCHAR(255))) LIKE '%' || '{term}' || '%'"
                    )
                );
            }

            // 4) COUNT total (remove ORDER BY terminal se houver)
            var countSource = Regex.Replace(
                baseSql, @"\border\s+by\b[\s\S]*?$",
                "", RegexOptions.IgnoreCase | RegexOptions.Multiline
            ).TrimEnd();
            if (string.IsNullOrWhiteSpace(countSource)) countSource = baseSql;

            var countSql = $@"SELECT COUNT(1) AS TOTAL FROM ({countSource}) T {whereSearch}";
            var countDt = await _hana.QueryToDataTableAsync(countSql, null, ct);
            var total = Convert.ToInt32(countDt.Rows[0]["TOTAL"]);

            // 5) Página/offset
            if (page < 1) page = 1;
            if (pageSize <= 0) pageSize = 200;
            var offset = (page - 1) * pageSize;

            // 6) ORDER BY determinístico
            var hasOrder = Regex.IsMatch(baseSql, @"\border\s+by\b", RegexOptions.IgnoreCase);
            var ordered = hasOrder ? baseSql : $"{baseSql}\nORDER BY 1";

            // 7) Consulta paginada
            var pagedSql = $@"SELECT * FROM ({ordered}) T {whereSearch} LIMIT {pageSize} OFFSET {offset}";
            var dt = await _hana.QueryToDataTableAsync(pagedSql, null, ct);

            // 8) Retorna no formato DataTablePage
            return new DataTablePage
            {
                Data = dt,
                Page = page,
                PageSize = pageSize,
                Total = total,
                Search = q
            };
        }

        // --- helpers (mesmo padrão do RelatorioProdutosService) ---
        private string ResolveSqlPath(string fileName)
        {
            var candidates = new[]
            {
                Path.Combine(_env.ContentRootPath, "ConsultasSQL", fileName),
                Path.Combine(_env.ContentRootPath, "consultasSQL", fileName), // tolera caixa
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
            if (s.Length > 0 && s[0] == '\uFEFF') s = s.TrimStart('\uFEFF'); // remove BOM
            while (s.EndsWith(";")) s = s[..^1].TrimEnd();                  // remove ; final
            if (string.IsNullOrWhiteSpace(s))
                throw new InvalidOperationException("O arquivo SQL está vazio.");
            return s;
        }
    }
}
