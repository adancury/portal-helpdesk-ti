using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using PortalHelpdeskTI.ViewModels.Relatorios;
using System;
using System.Data;
using System.Data.Odbc;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PortalHelpdeskTI.Services.Relatorios
{
    public class RelatorioRupturasService
    {
        private readonly string _connStr;
        private readonly string _sqlPath;

        public RelatorioRupturasService(
            IConfiguration cfg,
            IWebHostEnvironment env)
        {
            _connStr = cfg.GetConnectionString("HanaConn");

            // ConsultasSQL\RupturasHistorico.sql na raiz do projeto
            _sqlPath = Path.Combine(env.ContentRootPath,
                                    "ConsultasSQL",
                                    "RupturasHistorico.sql");
        }

        private string LoadSql()
        {
            if (!File.Exists(_sqlPath))
                throw new FileNotFoundException(
                    $"Arquivo de consulta SQL não encontrado: {_sqlPath}");

            var sql = File.ReadAllText(_sqlPath, Encoding.UTF8);

            // Troca :CodItem por ? para o ODBC/HANA
            sql = sql.Replace(":CodItem", "?");

            return sql;
        }

        public async Task<DataTablePage> GerarAsync(string? itemCode)
        {
            using var cn = new OdbcConnection(_connStr);
            await cn.OpenAsync();

            using var cmd = cn.CreateCommand();
            var sql = LoadSql(); // faz o Replace(:CodItem -> ?)
            cmd.CommandText = sql;
            cmd.CommandType = CommandType.Text;

            var paramCount = Regex.Matches(sql, @"\?").Count;

            object value;
            if (string.IsNullOrWhiteSpace(itemCode))
                value = DBNull.Value;
            else
                value = itemCode.Trim().ToUpperInvariant();

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

            return new DataTablePage
            {
                Data = dt,
                Total = dt.Rows.Count,
                Page = 1,
                PageSize = 50   // <<< 50 por página
            };
        }
    }
}
