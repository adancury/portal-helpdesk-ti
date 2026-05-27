// Infrastructure/HanaDb.cs
namespace PortalHelpdeskTI.Infrastructure
{
    using System.Data;
    using System.Data.Odbc;
    using System.Collections.Generic;
    using Dapper;                       // <= importante
    using Microsoft.Extensions.Configuration;

    public interface IHanaDb
    {
        Task<DataTable> QueryToDataTableAsync(string sql, object? parameters = null, CancellationToken ct = default);
    }

    public class HanaDb : IHanaDb
    {
        private readonly string _connStr;
        public HanaDb(IConfiguration cfg) => _connStr = cfg.GetConnectionString("HanaConn")!;

        public async Task<DataTable> QueryToDataTableAsync(string sql, object? parameters = null, CancellationToken ct = default)
        {
            using var cn = new OdbcConnection(_connStr);
            await cn.OpenAsync(ct);

            // Se precisar fixar schema, pode descomentar:
            // using (var set = new OdbcCommand("SET SCHEMA SBO_BRW_PRD", cn))
            // {
            //     await set.ExecuteNonQueryAsync(ct);
            // }

            // Usa Dapper em vez de DataTable.Load(reader) para evitar GetSchemaTable/overflow
            var command = new CommandDefinition(
                sql,
                parameters,
                commandTimeout: 180,
                cancellationToken: ct);

            var rows = await cn.QueryAsync(command);

            var dt = new DataTable();
            var schemaInitialized = false;

            foreach (IDictionary<string, object?> row in rows)
            {
                if (!schemaInitialized)
                {
                    foreach (var kv in row)
                    {
                        var colName = kv.Key?.ToString() ?? "COL";

                        // Se já existir, renomeia adicionando _1, _2, _3...
                        var finalName = colName;
                        int suffix = 1;
                        while (dt.Columns.Contains(finalName))
                        {
                            finalName = $"{colName}_{suffix++}";
                        }

                        var colType = kv.Value?.GetType() ?? typeof(string);
                        dt.Columns.Add(finalName, colType);
                    }

                    schemaInitialized = true;
                }


                var dataRow = dt.NewRow();
                foreach (var kv in row)
                {
                    dataRow[kv.Key] = kv.Value ?? DBNull.Value;
                }
                dt.Rows.Add(dataRow);
            }

            return dt;
        }
    }
}
