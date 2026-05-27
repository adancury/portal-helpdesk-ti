using Microsoft.Extensions.Configuration;
using PortalHelpdeskTI.ViewModels.Relatorios;
using System.Data;
using System.Data.Odbc;
using System.Text;

namespace PortalHelpdeskTI.Services.Relatorios
{
    public class CadColaboradorService
    {
        private readonly string _connString;

        public CadColaboradorService(IConfiguration cfg)
        {
            _connString = cfg.GetConnectionString("HanaConn");
        }

        public async Task<DataTablePage> ListarAsync(string? busca, int page = 1, int pageSize = 20)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;

            var sqlPath = Path.Combine("ConsultasSQL", "CadColaborador.sql");
            var baseSql = await File.ReadAllTextAsync(sqlPath);

            using var con = new OdbcConnection(_connString);
            await con.OpenAsync();

            var hasBusca = !string.IsNullOrWhiteSpace(busca);
            var pattern = hasBusca ? $"%{busca!.Trim().ToUpper()}%" : null;

            // 1) Total
            using var countCmd = con.CreateCommand();
            if (hasBusca)
            {
                countCmd.CommandText = $@"
            SELECT COUNT(*)
            FROM ({baseSql}) B
            WHERE UPPER(B.""Nome Completo"") LIKE ?
               OR UPPER(B.""Departamento"") LIKE ?";

                countCmd.Parameters.Add("p1", OdbcType.VarChar).Value = pattern!;
                countCmd.Parameters.Add("p2", OdbcType.VarChar).Value = pattern!;
            }
            else
            {
                countCmd.CommandText = $@"SELECT COUNT(*) FROM ({baseSql}) B";
            }

            var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

            // 2) Página
            var from = ((page - 1) * pageSize) + 1;
            var to = page * pageSize;

            using var cmd = con.CreateCommand();
            if (hasBusca)
            {
                cmd.CommandText = $@"
            SELECT *
            FROM (
                SELECT 
                    B.*,
                    ROW_NUMBER() OVER(ORDER BY ""ID Interno"") AS rn
                FROM ({baseSql}) B
                WHERE UPPER(B.""Nome Completo"") LIKE ?
                   OR UPPER(B.""Departamento"") LIKE ?
            ) T
            WHERE T.rn BETWEEN ? AND ?";

                cmd.Parameters.Add("p1", OdbcType.VarChar).Value = pattern!;
                cmd.Parameters.Add("p2", OdbcType.VarChar).Value = pattern!;
                cmd.Parameters.Add("p3", OdbcType.Int).Value = from;
                cmd.Parameters.Add("p4", OdbcType.Int).Value = to;
            }
            else
            {
                cmd.CommandText = $@"
            SELECT *
            FROM (
                SELECT 
                    B.*,
                    ROW_NUMBER() OVER(ORDER BY ""ID Interno"") AS rn
                FROM ({baseSql}) B
            ) T
            WHERE T.rn BETWEEN ? AND ?";

                cmd.Parameters.Add("p1", OdbcType.Int).Value = from;
                cmd.Parameters.Add("p2", OdbcType.Int).Value = to;
            }

            var dt = new DataTable();
            using (var da = new OdbcDataAdapter((OdbcCommand)cmd))
            {
                da.Fill(dt);
            }

            return new DataTablePage
            {
                Data = dt,
                Total = total,
                Page = page,
                PageSize = pageSize
            };
        }

        public async Task<string> GerarCsvAsync(string? busca)
        {
            // 1) Descobre o total de registros com o mesmo filtro da tela
            var primeiraPagina = await ListarAsync(busca, page: 1, pageSize: 1);
            var total = primeiraPagina.Total;

            DataTable dt;

            if (total <= 1)
            {
                // Se não tem registro ou só 1, já usamos o Data que veio
                dt = primeiraPagina.Data ?? new DataTable();
            }
            else
            {
                // 2) Busca TODOS os registros de uma vez, sem paginação prática
                var todas = await ListarAsync(busca, page: 1, pageSize: total);
                dt = todas.Data ?? new DataTable();
            }

            var sb = new StringBuilder();

            // Cabeçalho
            for (int i = 0; i < dt.Columns.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(Escape(dt.Columns[i].ColumnName));
            }
            sb.AppendLine();

            // Linhas
            foreach (DataRow row in dt.Rows)
            {
                for (int i = 0; i < dt.Columns.Count; i++)
                {
                    if (i > 0) sb.Append(';');

                    var val = row[i] == DBNull.Value ? "" : row[i]?.ToString() ?? "";
                    sb.Append(Escape(val));
                }
                sb.AppendLine();
            }

            return sb.ToString();

            // helper para escapar ; , quebras de linha, etc.
            static string Escape(string input)
            {
                if (input == null) return "";
                // troca quebras por espaço pra não quebrar linhas do CSV
                var s = input.Replace("\r", " ").Replace("\n", " ");
                // sempre entre aspas, dobrando aspas internas
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }
        }
    }
}