using PortalHelpdeskTI.ViewModels.Relatorios;
using System.Data;
using System.Data.Odbc;
using System.Text.RegularExpressions;

namespace PortalHelpdeskTI.Services.Relatorios
{
    public class RelatorioRupturaPrevisaoService
    {
        private readonly string _connStr;
        private readonly IWebHostEnvironment _env;

        public RelatorioRupturaPrevisaoService(IConfiguration cfg, IWebHostEnvironment env)
        {
            _connStr = cfg.GetConnectionString("HanaConn")!;
            _env = env;
        }

        private string LoadSql()
        {
            var path = Path.Combine(_env.ContentRootPath,
                "ConsultasSQL", "RupturaPrevisao.sql");

            // só lê o arquivo, sem replace de :CodItem
            return System.IO.File.ReadAllText(path);
        }

        public async Task<DataTablePage> GerarAsync(string? itemCode)
        {
            using var cn = new OdbcConnection(_connStr);
            await cn.OpenAsync();

            using var cmd = cn.CreateCommand();
            var sql = LoadSql();
            cmd.CommandText = sql;
            cmd.CommandType = CommandType.Text;

            // conta quantos "?" existem no SQL (no seu caso: 3)
            var paramCount = Regex.Matches(sql, @"\?").Count;

            object value = string.IsNullOrWhiteSpace(itemCode)
                ? DBNull.Value
                : itemCode.Trim().ToUpperInvariant();

            // adiciona TODOS os parâmetros com o mesmo valor
            for (int i = 0; i < paramCount; i++)
            {
                var p = cmd.Parameters.Add($"@p{i + 1}", OdbcType.NVarChar);
                p.Value = value;
            }

            var dt = new DataTable();
            using (var da = new OdbcDataAdapter(cmd))
            {
                da.Fill(dt);
            }

            dt.ExtendedProperties["UltimaAtualizacaoPrevisao"] = await ObterUltimaAtualizacaoAsync(cn);

            return new DataTablePage
            {
                Data = dt,
                Total = dt.Rows.Count,
                Page = 1,
                PageSize = 50
            };
        }

        private static async Task<DateTime?> ObterUltimaAtualizacaoAsync(OdbcConnection cn)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandText = @"SELECT MAX(""GeradoEm"") FROM ""Z_RUPTURA_PREV_CONSUMO""";
            cmd.CommandType = CommandType.Text;

            var value = await cmd.ExecuteScalarAsync();
            if (value == null || value == DBNull.Value)
                return null;

            if (value is DateTime dt)
                return dt;

            return DateTime.TryParse(value.ToString(), out var parsed)
                ? parsed
                : null;
        }
    }
}
