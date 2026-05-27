using Microsoft.EntityFrameworkCore;
using PortalHelpdeskTI.Models.Comissoes;
using System.Data;
using System.Data.Odbc;

namespace PortalHelpdeskTI.Services.Comissoes
{
    public class ComissaoVendedorSyncService : IComissaoVendedorSyncService
    {
        private readonly AppDbContext _db;
        private readonly string _hanaConn;

        public ComissaoVendedorSyncService(AppDbContext db, IConfiguration config)
        {
            _db = db;
            _hanaConn = config.GetConnectionString("HanaConn")
                ?? throw new InvalidOperationException("ConnectionString 'HanaConn' não configurada.");
        }

        private class HanaRow
        {
            public int SlpCode { get; set; }
            public string SlpName { get; set; } = "";
            public decimal Percentual { get; set; }
            public bool Ativo { get; set; }
            public string? HanaEmail { get; set; }
        }

        public async Task<(int Inseridos, int Atualizados)> SyncHanaToSqlAsync(CancellationToken ct = default)
        {
            var hana = await BuscarVendedoresNoHanaAsync(ct);

            var slpCodes = hana.Select(x => x.SlpCode).Distinct().ToList();

            var existentes = await _db.ComissaoVendedores
                .Where(x => slpCodes.Contains(x.SlpCode))
                .ToListAsync(ct);

            var map = existentes.ToDictionary(x => x.SlpCode);

            int ins = 0, upd = 0;

            foreach (var h in hana)
            {
                ct.ThrowIfCancellationRequested();

                if (!map.TryGetValue(h.SlpCode, out var row))
                {
                    row = new ComissaoVendedor
                    {
                        SlpCode = h.SlpCode,
                        SlpName = h.SlpName,
                        Percentual = h.Percentual,
                        Ativo = h.Ativo,

                        // defaults editáveis (portal)
                        BaseCalculo = "FATURAMENTO",
                        TipoVendedor = "REPRESENTANTE",

                        // IMPORTANTE: default seguro para passar na CK
                        // (ajuste o nome exato da propriedade conforme seu model)
                        ParticipaRelatorio = h.Ativo ? true : false, // ou false, se preferir começar desligado

                        Email = string.IsNullOrWhiteSpace(h.HanaEmail) ? null : h.HanaEmail.Trim()
                    };

                    // Normalização para respeitar CK_ComissaoVendedor_Ativo_Participa
                    if (!row.Ativo)
                        row.ParticipaRelatorio = false;

                    _db.ComissaoVendedores.Add(row);
                    ins++;
                    continue;
                }

                // Atualiza somente o que é fonte HANA
                row.SlpName = h.SlpName;
                row.Percentual = h.Percentual;

                // Antes: row.Ativo = h.Ativo;
                // Agora: aplique e normalize
                row.Ativo = h.Ativo;
                if (!row.Ativo)
                    row.ParticipaRelatorio = false;

                // Regra do email no sync:
                if (!string.IsNullOrWhiteSpace(h.HanaEmail))
                    row.Email = h.HanaEmail.Trim();

                upd++;
            }

            await _db.SaveChangesAsync(ct);
            return (ins, upd);
        }
        public async Task<bool> TryPushEmailToHanaIfEmptyAsync(int slpCode, string portalEmail, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(portalEmail))
                return false;

            portalEmail = portalEmail.Trim();

            await using var conn = new OdbcConnection(_hanaConn);
            await conn.OpenAsync(ct);

            // 1) Confirma se no HANA está vazio
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT IFNULL(""Email"", '') 
                    FROM OSLP
                    WHERE ""SlpCode"" = ?
                    ";
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = slpCode });

                var current = (await cmd.ExecuteScalarAsync(ct))?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(current))
                    return false; // já tem e-mail no HANA, não sobe
            }

            // 2) Atualiza HANA apenas se ainda estiver vazio (proteção contra corrida)
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    UPDATE OSLP
                    SET ""Email"" = ?
                    WHERE ""SlpCode"" = ?
                      AND IFNULL(""Email"", '') = ''
                    ";
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = portalEmail });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = slpCode });

                var rows = await cmd.ExecuteNonQueryAsync(ct);
                return rows > 0;
            }
        }

        private async Task<List<HanaRow>> BuscarVendedoresNoHanaAsync(CancellationToken ct)
        {
            var list = new List<HanaRow>();

            await using var conn = new OdbcConnection(_hanaConn);
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();

            // ✅ Se você já tem uma query “oficial” do botão Atualizar do relatório,
            // pode substituir aqui, mantendo os aliases iguais:
            cmd.CommandText = @"
                SELECT
                    ""SlpCode"",
                    ""SlpName"",
                    CAST(IFNULL(""Commission"", 0) AS DECIMAL(19,6)) AS ""Percentual"",
                    CASE WHEN IFNULL(""Active"", 'Y') = 'Y' THEN 1 ELSE 0 END AS ""Ativo"",
                    NULLIF(TRIM(IFNULL(""Email"", '')), '') AS ""HanaEmail""
                FROM OSLP
                ";

            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                list.Add(new HanaRow
                {
                    SlpCode = Convert.ToInt32(rd["SlpCode"]),
                    SlpName = Convert.ToString(rd["SlpName"]) ?? "",
                    Percentual = Convert.ToDecimal(rd["Percentual"]),
                    Ativo = Convert.ToInt32(rd["Ativo"]) == 1, // ✅ converte 1/0 em bool
                    HanaEmail = rd["HanaEmail"] == DBNull.Value ? null : Convert.ToString(rd["HanaEmail"])
                });
            }

            return list;
        }
    }
}
