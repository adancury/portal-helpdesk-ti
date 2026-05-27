using Dapper;
using Microsoft.Extensions.Configuration;
using PortalHelpdeskTI.Models.IntegracoesSL;
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

public sealed class NexxSalesforceMonitorService
{
    private readonly string _hanaConnStr;

    public NexxSalesforceMonitorService(IConfiguration cfg)
    {
        _hanaConnStr = cfg.GetConnectionString("HanaConn")
            ?? throw new InvalidOperationException("ConnectionString 'HanaConn' não configurada.");
    }

    private OdbcConnection Open() => new(_hanaConnStr);

    public async Task<Dictionary<string, int>> KpisEventsAsync(DateTime? ini, DateTime? fim, CancellationToken ct)
    {
        var sql = new StringBuilder();
        sql.AppendLine(@"SELECT IFNULL(""U_Status"", '') AS ""Status"", COUNT(*) AS ""Qtde""");
        sql.AppendLine(@"FROM ""@NEXX_EVENTS""");
        sql.AppendLine(@"WHERE ""Canceled"" = 'N'");

        var p = new Dapper.DynamicParameters();

        if (ini.HasValue)
        {
            sql.AppendLine(@"  AND ""U_CreatedDate"" >= ?");
            p.Add("p_ini", ini.Value);
        }

        if (fim.HasValue)
        {
            sql.AppendLine(@"  AND ""U_CreatedDate"" < ADD_DAYS(?, 1)");
            p.Add("p_fim", fim.Value);
        }

        sql.AppendLine(@"GROUP BY IFNULL(""U_Status"", '');");

        using var cn = Open();

        var rows = await cn.QueryAsync<(string Status, int Qtde)>(
            new Dapper.CommandDefinition(sql.ToString(), p, cancellationToken: ct));

        var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            var key = (r.Status ?? "").Trim();
            if (key.Length == 0) continue;
            dict[key] = r.Qtde;
        }
        return dict;
    }

    public async Task<(List<NexxEventDto> itens, int total, List<string> statusDisponiveis)> ListEventsAsync(
    DateTime? ini, DateTime? fim, string? status, string? q, int skip, int take, CancellationToken ct)
    {
        // normalizações
        status = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
        q = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        var where = new StringBuilder();
        where.AppendLine(@"""Canceled"" = 'N'");

        var p = new Dapper.DynamicParameters();

        if (ini.HasValue)
        {
            where.AppendLine(@"AND ""U_CreatedDate"" >= ?");
            p.Add("p_ini", ini.Value);
        }

        if (fim.HasValue)
        {
            where.AppendLine(@"AND ""U_CreatedDate"" < ADD_DAYS(?, 1)");
            p.Add("p_fim", fim.Value);
        }

        if (status != null)
        {
            where.AppendLine(@"AND ""U_Status"" = ?");
            p.Add("p_status", status);
        }

        if (q != null)
        {
            where.AppendLine(@"
AND (
    UPPER(IFNULL(""U_EventId"", ''))        LIKE '%' || UPPER(?) || '%' OR
    UPPER(IFNULL(""U_CorrelationId"", ''))  LIKE '%' || UPPER(?) || '%' OR
    UPPER(IFNULL(""U_PrimaryKeys"", ''))    LIKE '%' || UPPER(?) || '%' OR
    UPPER(IFNULL(""U_ErrorMessage"", ''))   LIKE '%' || UPPER(?) || '%'
)");
            // ⚠️ placeholders posicionais: repetir 4x
            p.Add("p_q1", q);
            p.Add("p_q2", q);
            p.Add("p_q3", q);
            p.Add("p_q4", q);
        }

        // TOTAL
        var sqlTotal = $@"
SELECT COUNT(*)
FROM ""@NEXX_EVENTS""
WHERE {where}";

        // LIST
        var sqlList = $@"
SELECT
  ""U_EventId""         AS ""EventId"",
  ""U_ObjectType""      AS ""ObjectType"",
  ""U_TransactionType"" AS ""TransactionType"",
  ""U_PrimaryKeys""     AS ""PrimaryKeys"",
  ""U_EventData""       AS ""EventData"",
  ""U_CreatedDate""     AS ""CreatedDate"",
  ""U_CreatedBy""       AS ""CreatedBy"",
  ""U_Status""          AS ""Status"",
  ""U_ErrorMessage""    AS ""ErrorMessage"",
  ""U_RetryCount""      AS ""RetryCount"",
  ""U_Priority""        AS ""Priority"",
  ""U_ProcessedDate""   AS ""ProcessedDate"",
  ""U_CorrelationId""   AS ""CorrelationId"",
  ""U_ParentEventId""   AS ""ParentEventId""
FROM ""@NEXX_EVENTS""
WHERE {where}
ORDER BY ""U_CreatedDate"" DESC
LIMIT ? OFFSET ?;";

        // status dropdown
        var sqlStatus = @"
SELECT DISTINCT ""U_Status""
FROM ""@NEXX_EVENTS""
WHERE ""Canceled""='N' AND ""U_Status"" IS NOT NULL AND ""U_Status"" <> ''
ORDER BY ""U_Status"";";

        using var cn = Open();

        var total = await cn.ExecuteScalarAsync<int>(
            new Dapper.CommandDefinition(sqlTotal, p, cancellationToken: ct));

        // LIMIT/OFFSET no final (posicional)
        p.Add("p_take", take);
        p.Add("p_skip", skip);

        var itens = (await cn.QueryAsync<NexxEventDto>(
            new Dapper.CommandDefinition(sqlList, p, cancellationToken: ct))).AsList();

        var statusDisp = (await cn.QueryAsync<string>(
            new Dapper.CommandDefinition(sqlStatus, cancellationToken: ct)))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        return (itens, total, statusDisp);
    }


    public async Task<Dictionary<int, int>> KpisLogsAsync(DateTime? ini, DateTime? fim, CancellationToken ct)
    {
        var sql = new StringBuilder();
        sql.AppendLine(@"SELECT IFNULL(""U_NEXX_Status"", 0) AS ""Status"", COUNT(*) AS ""Qtde""");
        sql.AppendLine(@"FROM ""@NEXX_LOG""");
        sql.AppendLine(@"WHERE ""Canceled"" = 'N'");

        var p = new DynamicParameters();

        if (ini.HasValue)
        {
            sql.AppendLine(@"  AND ""U_NEXX_DtInteg"" >= ?");
            p.Add("p_ini", ini.Value);
        }

        if (fim.HasValue)
        {
            sql.AppendLine(@"  AND ""U_NEXX_DtInteg"" < ADD_DAYS(?, 1)");
            p.Add("p_fim", fim.Value);
        }

        sql.AppendLine(@"GROUP BY IFNULL(""U_NEXX_Status"", 0);");

        using var cn = Open();

        var rows = await cn.QueryAsync<(int Status, int Qtde)>(
            new CommandDefinition(sql.ToString(), p, cancellationToken: ct));

        var dict = new Dictionary<int, int>();
        foreach (var r in rows)
            dict[r.Status] = r.Qtde;

        return dict;
    }


    public async Task<(List<NexxLogDto> itens, int total, List<int> statusDisponiveis, List<string> tiposDoc)> ListLogsAsync(
    DateTime? ini, DateTime? fim, int? status, string? tipoDoc, string? q, int skip, int take, CancellationToken ct)
    {
        tipoDoc = string.IsNullOrWhiteSpace(tipoDoc) ? null : tipoDoc.Trim();
        q = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        var where = new StringBuilder();
        where.AppendLine(@"""Canceled"" = 'N'");

        var p = new DynamicParameters();

        if (ini.HasValue)
        {
            where.AppendLine(@"AND ""U_NEXX_DtInteg"" >= ?");
            p.Add("p_ini", ini.Value);
        }

        if (fim.HasValue)
        {
            where.AppendLine(@"AND ""U_NEXX_DtInteg"" < ADD_DAYS(?, 1)");
            p.Add("p_fim", fim.Value);
        }

        if (status.HasValue)
        {
            where.AppendLine(@"AND ""U_NEXX_Status"" = ?");
            p.Add("p_status", status.Value);
        }

        if (tipoDoc != null)
        {
            where.AppendLine(@"AND ""U_NEXX_TipoDoc"" = ?");
            p.Add("p_tipo", tipoDoc);
        }

        if (q != null)
        {
            where.AppendLine(@"
AND (
    UPPER(IFNULL(""U_NEXX_IdDoc"", ''))     LIKE '%' || UPPER(?) || '%' OR
    UPPER(IFNULL(""U_NEXX_IdRet"", ''))     LIKE '%' || UPPER(?) || '%' OR
    UPPER(IFNULL(""U_NEXX_MsgRet"", ''))    LIKE '%' || UPPER(?) || '%' OR
    UPPER(IFNULL(""U_NEXX_IdDocLeg"", ''))  LIKE '%' || UPPER(?) || '%'
)");
            p.Add("p_q1", q);
            p.Add("p_q2", q);
            p.Add("p_q3", q);
            p.Add("p_q4", q);
        }

        var sqlTotal = $@"
SELECT COUNT(*)
FROM ""@NEXX_LOG""
WHERE {where}";

        var sqlList = $@"
SELECT
  ""CreateDate""        AS ""CreateDate"",
  ""CreateTime""        AS ""CreateTime"",
  ""U_NEXX_TipoDoc""    AS ""TipoDocumento"",
  ""U_NEXX_IdDoc""      AS ""IdDocumento"",
  ""U_NEXX_DtRegi""     AS ""DataRegistro"",
  ""U_NEXX_DtInteg""    AS ""DataIntegracao"",
  ""U_NEXX_Status""     AS ""Status"",
  ""U_NEXX_IdRet""      AS ""IdRetorno"",
  ""U_NEXX_MsgRet""     AS ""Mensagem"",
  ""U_NEXX_IdDocLeg""   AS ""IdDocumentoLegado"",
  ""U_NEXX_JsonEnv""    AS ""JsonEnvio"",
  ""U_NEXX_JsonRet""    AS ""JsonRetorno""
FROM ""@NEXX_LOG""
WHERE {where}
ORDER BY ""U_NEXX_DtInteg"" DESC, ""CreateTime"" DESC
LIMIT ? OFFSET ?;";

        const string sqlStatus = @"
SELECT DISTINCT ""U_NEXX_Status""
FROM ""@NEXX_LOG""
WHERE ""Canceled""='N' AND ""U_NEXX_Status"" IS NOT NULL
ORDER BY ""U_NEXX_Status"";";

        const string sqlTipos = @"
SELECT DISTINCT ""U_NEXX_TipoDoc""
FROM ""@NEXX_LOG""
WHERE ""Canceled""='N' AND ""U_NEXX_TipoDoc"" IS NOT NULL AND ""U_NEXX_TipoDoc"" <> ''
ORDER BY ""U_NEXX_TipoDoc"";";

        using var cn = Open();

        var total = await cn.ExecuteScalarAsync<int>(
            new CommandDefinition(sqlTotal, p, cancellationToken: ct));

        p.Add("p_take", take);
        p.Add("p_skip", skip);

        var itens = (await cn.QueryAsync<NexxLogDto>(
            new CommandDefinition(sqlList, p, cancellationToken: ct))).AsList();

        var statusDisp = (await cn.QueryAsync<int>(
            new CommandDefinition(sqlStatus, cancellationToken: ct))).AsList();

        var tipos = (await cn.QueryAsync<string>(
            new CommandDefinition(sqlTipos, cancellationToken: ct)))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        return (itens, total, statusDisp, tipos);
    }


}
