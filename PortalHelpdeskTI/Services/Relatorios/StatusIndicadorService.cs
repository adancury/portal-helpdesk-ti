using System.Data.Odbc;
using Dapper;
using PortalHelpdeskTI.Views.Relatorios;

namespace PortalHelpdeskTI.Services.Relatorios
{
    public class StatusIndicadorService
    {
        private readonly string _connStr;

        public StatusIndicadorService(IConfiguration cfg)
        {
            _connStr = cfg.GetConnectionString("HanaConn");
        }

        public async Task<List<StatusIndicadorLinhaVM>> BuscarAsync(DateTime? de)
        {
            using var conn = new OdbcConnection(_connStr);
            await conn.OpenAsync();

            string sql = "CALL \"SBO_BRW_PRD\".\"Portal_StatusIndicador\"(?)";

            var param = new
            {
                P_DATEI = (object?)de ?? DBNull.Value
            };

            var dados = await conn.QueryAsync<StatusIndicadorLinhaVM>(sql, param);
            return dados.ToList();
        }
    }
}
