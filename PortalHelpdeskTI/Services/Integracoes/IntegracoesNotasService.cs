using PortalHelpdeskTI.Models;
using System.Data;
using System.Data.Odbc;

namespace PortalHelpdeskTI.Services.Integracoes
{
    public class IntegracoesNotasService
    {
        private readonly string _hanaConnStr;

        public IntegracoesNotasService(IConfiguration configuration)
        {
            _hanaConnStr = configuration.GetConnectionString("HanaConn")
                ?? throw new InvalidOperationException("ConnectionString 'HanaConn' não configurada.");
        }

        public async Task<(List<IntegracoesNotas> Itens, int Total)> BuscarAsync(
            DateTime? dataIni,
            DateTime? dataFim,
            string? statusEnvio,
            string? docNum,
            string? cardCode,
            string? cardName,
            string? emailCliente,
            string? chaveNf,
            bool somenteComErro,
            int pagina = 1,
            int tamanhoPagina = 50)
        {
            return await BuscarInternoAsync(
                dataIni, dataFim, statusEnvio, docNum, cardCode, cardName,
                emailCliente, chaveNf, somenteComErro, pagina, tamanhoPagina, false);
        }

        public async Task<List<IntegracoesNotas>> BuscarParaExportacaoAsync(
            DateTime? dataIni,
            DateTime? dataFim,
            string? statusEnvio,
            string? docNum,
            string? cardCode,
            string? cardName,
            string? emailCliente,
            string? chaveNf,
            bool somenteComErro)
        {
            var (itens, _) = await BuscarInternoAsync(
                dataIni, dataFim, statusEnvio, docNum, cardCode, cardName,
                emailCliente, chaveNf, somenteComErro, 1, 50, true);

            return itens;
        }

        private async Task<(List<IntegracoesNotas> Itens, int Total)> BuscarInternoAsync(
            DateTime? dataIni,
            DateTime? dataFim,
            string? statusEnvio,
            string? docNum,
            string? cardCode,
            string? cardName,
            string? emailCliente,
            string? chaveNf,
            bool somenteComErro,
            int pagina,
            int tamanhoPagina,
            bool exportarTudo)
        {
            var lista = new List<IntegracoesNotas>();
            var where = new List<string> { "1 = 1" };

            if (dataIni.HasValue)
                where.Add($"L.\"SentAt\" >= '{dataIni.Value:yyyy-MM-dd} 00:00:00'");

            if (dataFim.HasValue)
                where.Add($"L.\"SentAt\" <= '{dataFim.Value:yyyy-MM-dd} 23:59:59'");

            if (!string.IsNullOrWhiteSpace(statusEnvio))
                where.Add($"UPPER(IFNULL(L.\"Status\", '')) = UPPER('{Escape(statusEnvio)}')");

            if (!string.IsNullOrWhiteSpace(docNum))
                where.Add($"CAST(IFNULL(L.\"DocNum\", 0) AS NVARCHAR) LIKE '%{Escape(docNum)}%'");

            if (!string.IsNullOrWhiteSpace(cardCode))
                where.Add($"UPPER(IFNULL(I.\"CardCode\", '')) LIKE UPPER('%{Escape(cardCode)}%')");

            if (!string.IsNullOrWhiteSpace(cardName))
                where.Add($"UPPER(IFNULL(I.\"CardName\", '')) LIKE UPPER('%{Escape(cardName)}%')");

            if (!string.IsNullOrWhiteSpace(emailCliente))
                where.Add($"UPPER(IFNULL(C.\"E_Mail\", '')) LIKE UPPER('%{Escape(emailCliente)}%')");

            if (!string.IsNullOrWhiteSpace(chaveNf))
                where.Add($"UPPER(IFNULL(P.\"KeyNfe\", '')) LIKE UPPER('%{Escape(chaveNf)}%')");

            if (somenteComErro)
                where.Add("(UPPER(IFNULL(L.\"Status\", '')) = 'E' OR IFNULL(L.\"Error\", '') <> '')");

            var whereSql = string.Join(" AND ", where);
            var offset = (pagina - 1) * tamanhoPagina;

            var sqlTotal = $@"
                SELECT COUNT(1)
                FROM ""Z_INV_EMAIL_CTRL_CLI"" L
                LEFT JOIN ""OINV"" I
                       ON I.""DocEntry"" = L.""DocEntry""
                LEFT JOIN ""OCRD"" C
                       ON C.""CardCode"" = I.""CardCode""
                LEFT JOIN (
                    SELECT
                        P0.""DocEntry"",
                        MAX(P0.""KeyNfe"") AS ""KeyNfe""
                    FROM ""DBInvOne"".""Process"" P0
                    WHERE P0.""DocType"" = 13
                    GROUP BY P0.""DocEntry""
                ) P
                       ON P.""DocEntry"" = L.""DocEntry""
                WHERE {whereSql};";

                            var sql = $@"
                SELECT
                    IFNULL(L.""DocEntry"", 0) AS ""DocEntry"",
                    IFNULL(L.""DocNum"", 0)   AS ""DocNum"",
                    L.""SentAt""              AS ""SentAt"",

                    CASE
                        WHEN UPPER(IFNULL(L.""Status"", '')) = 'S' THEN 'Sucesso'
                        WHEN UPPER(IFNULL(L.""Status"", '')) = 'E' THEN 'Erro'
                        ELSE IFNULL(L.""Status"", '')
                    END AS ""StatusEnvio"",

                    IFNULL(L.""Error"", '') AS ""ErroEnvio"",

                    IFNULL(I.""CardCode"", '') AS ""CardCode"",
                    IFNULL(I.""CardName"", '') AS ""CardName"",
                    IFNULL(C.""E_Mail"", '')   AS ""EmailCliente"",

                    I.""DocDate""    AS ""DataDocumento"",
                    I.""TaxDate""    AS ""DataLancamento"",
                    I.""DocDueDate"" AS ""DataVencimento"",

                    IFNULL(I.""DocTotal"", 0)    AS ""ValorNota"",
                    IFNULL(I.""Serial"", 0)      AS ""NumNf"",
                    IFNULL(I.""SeriesStr"", '')  AS ""SerieNF"",
                    IFNULL(P.""KeyNfe"", '')     AS ""ChaveNf"",

                    CASE
                        WHEN IFNULL(I.""CANCELED"", 'N') = 'Y' THEN 'Cancelada'
                        WHEN IFNULL(I.""DocStatus"", '') = 'C' THEN 'Fechada'
                        WHEN IFNULL(I.""DocStatus"", '') = 'O' THEN 'Aberta'
                        ELSE IFNULL(I.""DocStatus"", '')
                    END AS ""StatusDocumento"",

                    IFNULL(I.""Comments"", '') AS ""Observacoes"",
                    IFNULL(I.""NumAtCard"", '') AS ""RefCliente""

                FROM ""Z_INV_EMAIL_CTRL_CLI"" L
                LEFT JOIN ""OINV"" I
                       ON I.""DocEntry"" = L.""DocEntry""
                LEFT JOIN ""OCRD"" C
                       ON C.""CardCode"" = I.""CardCode""
                LEFT JOIN (
                    SELECT
                        P0.""DocEntry"",
                        MAX(P0.""KeyNfe"") AS ""KeyNfe""
                    FROM ""DBInvOne"".""Process"" P0
                    WHERE P0.""DocType"" = 13
                    GROUP BY P0.""DocEntry""
                ) P
                       ON P.""DocEntry"" = L.""DocEntry""
                WHERE {whereSql}
                ORDER BY L.""SentAt"" DESC";

            if (!exportarTudo)
                sql += $@" LIMIT {tamanhoPagina} OFFSET {offset}";

            sql += ";";

            using var conn = new OdbcConnection(_hanaConnStr);
            await conn.OpenAsync();

            int total;
            using (var cmdTotal = new OdbcCommand(sqlTotal, conn))
            {
                total = Convert.ToInt32(await cmdTotal.ExecuteScalarAsync());
            }

            using (var cmd = new OdbcCommand(sql, conn))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    lista.Add(new IntegracoesNotas
                    {
                        DocEntry = GetInt(reader, "DocEntry"),
                        DocNum = GetInt(reader, "DocNum"),
                        SentAt = GetDateTimeNullable(reader, "SentAt"),
                        StatusEnvio = GetString(reader, "StatusEnvio"),
                        ErroEnvio = GetString(reader, "ErroEnvio"),

                        CardCode = GetString(reader, "CardCode"),
                        CardName = GetString(reader, "CardName"),
                        EmailCliente = GetString(reader, "EmailCliente"),

                        DataDocumento = GetDateTimeNullable(reader, "DataDocumento"),
                        DataLancamento = GetDateTimeNullable(reader, "DataLancamento"),
                        DataVencimento = GetDateTimeNullable(reader, "DataVencimento"),

                        ValorNota = GetDecimal(reader, "ValorNota"),
                        NumNf = GetInt(reader, "NumNf"),
                        SerieNF = GetString(reader, "SerieNF"),
                        ChaveNf = GetString(reader, "ChaveNf"),

                        StatusDocumento = GetString(reader, "StatusDocumento"),
                        Observacoes = GetString(reader, "Observacoes"),
                        RefCliente = GetString(reader, "RefCliente")
                    });
                }
            }

            return (lista, total);
        }

        public async Task<(bool ok, string? error)> CopiarReferenciasFiscaisAsync(
    int numEsboco,
    int docEntryNotaCriada)
        {
            try
            {
                var sql = $@"
UPDATE ""RIN12""
SET
    ""MainUsage"" = (
        SELECT ""MainUsage""
        FROM ""DRF12""
        WHERE ""DocEntry"" = {numEsboco}
    ),
    ""Incoterms"" = (
        SELECT ""Incoterms""
        FROM ""DRF12""
        WHERE ""DocEntry"" = {numEsboco}
    ),
    ""Carrier"" = (
        SELECT ""Carrier""
        FROM ""DRF12""
        WHERE ""DocEntry"" = {numEsboco}
    )
WHERE ""DocEntry"" = {docEntryNotaCriada}";

                using var conn = new OdbcConnection(_hanaConnStr);
                await conn.OpenAsync();

                using var cmd = new OdbcCommand(sql, conn);
                var linhas = await cmd.ExecuteNonQueryAsync();

                if (linhas == 0)
                    return (false, "Nenhuma linha foi atualizada em RIN12. Verifique se o DocEntry da nota criada existe.");

                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public async Task<(bool ok, string? error, int numEsboco, int docEntryNotaCriada)> LocalizarVinculoFiscalPorOrdemEntradaAsync(long numOrdEntrada)
        {
            if (numOrdEntrada <= 0)
                return (false, "NUM_ORD_ENT inválido.", 0, 0);

            try
            {
                using var conn = new OdbcConnection(_hanaConnStr);
                await conn.OpenAsync();

                var sqlNumEsboco = @"
SELECT TOP 1
    IFNULL(CAST(""KEY_SAP"" AS NVARCHAR(100)), '') AS ""KEY_SAP""
FROM ""INTEGRACAO_WMS"".""K33P_LOG_WMS_API""
WHERE CAST(""KEY_WMS"" AS NVARCHAR(100)) = ?
  AND IFNULL(CAST(""KEY_SAP"" AS NVARCHAR(100)), '') <> ''
ORDER BY ""LOG_TS"" DESC";

                var keySap = "";
                using (var cmd = new OdbcCommand(sqlNumEsboco, conn))
                {
                    cmd.Parameters.Add("pNumOrdEntrada", OdbcType.NVarChar, 100).Value = numOrdEntrada.ToString();
                    var result = await cmd.ExecuteScalarAsync();
                    keySap = result?.ToString()?.Trim() ?? "";
                }

                if (!int.TryParse(keySap, out var numEsboco) || numEsboco <= 0)
                {
                    return (false,
                        $"Não foi possível localizar NumEsboco nos logs WMS para NUM_ORD_ENT {numOrdEntrada}.",
                        0,
                        0);
                }

                var sqlDocEntryNota = @"
SELECT TOP 1
    ""DocEntry""
FROM ""ORIN""
WHERE ""draftKey"" = ?
ORDER BY ""DocEntry"" DESC";

                var docEntryNotaCriada = 0;
                using (var cmd = new OdbcCommand(sqlDocEntryNota, conn))
                {
                    cmd.Parameters.Add("pDraftKey", OdbcType.Int).Value = numEsboco;
                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                        docEntryNotaCriada = Convert.ToInt32(result);
                }

                if (docEntryNotaCriada <= 0)
                {
                    return (false,
                        $"NumEsboco {numEsboco} localizado, mas nenhuma nota criada foi encontrada na ORIN pelo draftKey.",
                        numEsboco,
                        0);
                }

                return (true, null, numEsboco, docEntryNotaCriada);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, 0, 0);
            }
        }
        private static string Escape(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return "";

            return valor.Replace("'", "''").Trim();
        }

        private static string GetString(IDataRecord reader, string campo)
            => reader[campo] == DBNull.Value ? "" : reader[campo]?.ToString() ?? "";

        private static int GetInt(IDataRecord reader, string campo)
            => reader[campo] == DBNull.Value ? 0 : Convert.ToInt32(reader[campo]);

        private static decimal GetDecimal(IDataRecord reader, string campo)
            => reader[campo] == DBNull.Value ? 0 : Convert.ToDecimal(reader[campo]);

        private static DateTime? GetDateTimeNullable(IDataRecord reader, string campo)
            => reader[campo] == DBNull.Value ? null : Convert.ToDateTime(reader[campo]);
    
    }
}
