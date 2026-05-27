using System.Data.Odbc;

namespace PortalHelpdeskTI.Services.API
{
    public sealed class APIServices
    {
        private readonly string _hanaConnStr;

        public APIServices(IConfiguration cfg)
        {
            _hanaConnStr = cfg.GetConnectionString("HanaConn")
                ?? throw new InvalidOperationException("ConnectionString 'HanaConn' não configurada.");
        }

        public async Task<List<MunicipioDto>> BuscarMunicipiosAsync(
            string? uf,
            string? q,
            int? absId,
            int top,
            CancellationToken ct)
        {
            // Brasil tem ~5.570 municípios. Deixa margem e evita payload absurdo.
            if (top < 1) top = 1;
            if (top > 6000) top = 6000;

            uf = string.IsNullOrWhiteSpace(uf) ? null : uf.Trim().ToUpperInvariant();
            q = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

            var sql = @"
            SELECT
                T0.""AbsId""   AS ""AbsId"",
                T0.""Name""    AS ""Name"",
                T0.""State""   AS ""State"",
                T0.""Country"" AS ""Country""
            FROM ""OCNT"" T0
            WHERE T0.""Country"" = 'BR'
            ";

            var parms = new List<(OdbcType type, object value)>();

            if (absId.HasValue)
            {
                sql += @" AND T0.""AbsId"" = ? ";
                parms.Add((OdbcType.Int, absId.Value));
            }

            if (!string.IsNullOrWhiteSpace(uf))
            {
                sql += @" AND T0.""State"" = ? ";
                parms.Add((OdbcType.VarChar, uf!));
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                // Busca case-insensitive aproximada
                sql += @" AND UPPER(T0.""Name"") LIKE ? ";
                parms.Add((OdbcType.VarChar, $"%{q!.ToUpperInvariant()}%"));
            }

            sql += @" ORDER BY T0.""State"", T0.""Name"" ";
            sql += @" LIMIT ? ";
            parms.Add((OdbcType.Int, top));

            using var cn = new OdbcConnection(_hanaConnStr);
            await cn.OpenAsync(ct);

            using var cmd = new OdbcCommand(sql, cn);
            foreach (var (type, value) in parms)
            {
                var p = cmd.Parameters.Add("p", type);
                p.Value = value;
            }

            var list = new List<MunicipioDto>();
            using var rd = await cmd.ExecuteReaderAsync(ct);

            var ordAbsId = rd.GetOrdinal("AbsId");
            var ordName = rd.GetOrdinal("Name");
            var ordState = rd.GetOrdinal("State");
            var ordCtry = rd.GetOrdinal("Country");

            while (await rd.ReadAsync(ct))
            {
                list.Add(new MunicipioDto
                {
                    AbsId = rd.GetInt32(ordAbsId),
                    Name = rd.IsDBNull(ordName) ? "" : rd.GetString(ordName),
                    State = rd.IsDBNull(ordState) ? "" : rd.GetString(ordState),
                    Country = rd.IsDBNull(ordCtry) ? "" : rd.GetString(ordCtry)
                });
            }

            return list;
        }
    }

    public sealed class MunicipioDto
    {
        public int AbsId { get; set; }
        public string Name { get; set; } = "";
        public string State { get; set; } = "";
        public string Country { get; set; } = "";
    }
}
