using System.Data;
using System.Data.Odbc;
using PortalHelpdeskTI.ViewModels.IntegracoesWms;

namespace PortalHelpdeskTI.Services.Integracoes
{
    public sealed class IntegracoesWmsService
    {
        private readonly string _hanaConnStr;

        public static readonly string[] MetodosEnvio =
        {
            "POST /cadastrarProduto",
            "POST /cadastrarPessoa",
            "POST /enviarOrdemSaida",
            "POST /enviarCancelamentoOrdemSaida",
            "POST /enviarOrdemEntrada",
            "POST /enviarEstoque",
            "POST /enviarFaturamentoOrdemSaida"
        };

        private const string LogWorkerTable = @"""INTEGRACAO_WMS"".""K33P_LOG_WMS_WORKER""";
        private const string LogApiTable = @"""INTEGRACAO_WMS"".""K33P_LOG_WMS_API""";
        private const string MetodoFilaFaturamento = "POST /enviarFaturamentoOrdemSaida";

        private static readonly string FonteEnviosComFilaFaturamento = $@"
            (
                SELECT
                    LOG_TS,
                    METHOD,
                    MESSAGE,
                    KEY_WMS,
                    KEY_SAP,
                    REQUEST_JSON,
                    RESPONSE_JSON
                FROM {LogWorkerTable}

                UNION ALL

                SELECT
                    F.""U_DataInclusao"" AS LOG_TS,
                    '{MetodoFilaFaturamento}' AS METHOD,
                    CAST(
                        'Fila faturamento WMS'
                        || ' | Status: ' || IFNULL(CAST(F.""U_Status"" AS NVARCHAR(100)), '')
                        || ' | DocEntry: ' || IFNULL(CAST(F.""U_DocEntry"" AS NVARCHAR(50)), '')
                        || ' | DocNum: ' || IFNULL(CAST(F.""U_DocNum"" AS NVARCHAR(50)), '')
                        || ' | TipoEvento: ' || IFNULL(CAST(F.""U_TipoEvento"" AS NVARCHAR(100)), '')
                        || ' | Tentativas: ' || IFNULL(CAST(F.""U_Tentativas"" AS NVARCHAR(20)), '0')
                        || CASE
                            WHEN IFNULL(CAST(F.""U_Erro"" AS NVARCHAR(5000)), '') <> ''
                            THEN ' | Erro: ' || CAST(F.""U_Erro"" AS NVARCHAR(5000))
                            ELSE ''
                           END
                        || CASE
                            WHEN IFNULL(CAST(F.""U_RetornoDescricao"" AS NVARCHAR(5000)), '') <> ''
                            THEN ' | Retorno: ' || CAST(F.""U_RetornoDescricao"" AS NVARCHAR(5000))
                            ELSE ''
                           END
                    AS NVARCHAR(5000)) AS MESSAGE,
                    CAST(F.""Code"" AS NVARCHAR(200)) AS KEY_WMS,
                    CAST(F.""U_DocEntry"" AS NVARCHAR(200)) AS KEY_SAP,
                    CAST(F.""U_PayloadJson"" AS NVARCHAR(5000)) AS REQUEST_JSON,
                    CAST(
                        CASE
                            WHEN IFNULL(CAST(F.""U_RetornoDescricao"" AS NVARCHAR(5000)), '') <> ''
                                THEN CAST(F.""U_RetornoDescricao"" AS NVARCHAR(5000))
                            ELSE CAST(F.""U_Erro"" AS NVARCHAR(5000))
                        END
                    AS NVARCHAR(5000)) AS RESPONSE_JSON
                FROM ""SBO_BRW_PRD"".""@BRW_WMS_FILA_FAT"" F
                WHERE F.""U_DataInclusao"" IS NOT NULL
            )";

        public IntegracoesWmsService(IConfiguration cfg)
        {
            _hanaConnStr = cfg.GetConnectionString("HanaConn")
                ?? throw new InvalidOperationException("ConnectionString 'HanaConn' não configurada.");
        }

        public async Task<(DataTable rows, int total)> BuscarEnviosAsync(IntegracoesWmsFiltroVm f, CancellationToken ct)
        {
            string? metodo = string.IsNullOrWhiteSpace(f.Metodo) ? null : f.Metodo.Trim();
            if (metodo != null && !MetodosEnvio.Contains(metodo))
                metodo = null;

            return await BuscarGenericoAsync(
                table: FonteEnviosComFilaFaturamento,
                f: f,
                metodo: metodo,
                ct: ct);
        }

        public async Task<(DataTable rows, int total)> BuscarRetornosAsync(IntegracoesWmsFiltroVm f, CancellationToken ct)
        {
            string? metodo = string.IsNullOrWhiteSpace(f.Metodo) ? null : f.Metodo.Trim();

            return await BuscarGenericoAsync(
                table: LogApiTable,
                f: f,
                metodo: metodo,
                ct: ct);
        }

        private async Task<(DataTable rows, int total)> BuscarGenericoAsync(
            string table,
            IntegracoesWmsFiltroVm f,
            string? metodo,
            CancellationToken ct)
        {
            var page = f.Page < 1 ? 1 : f.Page;
            var pageSize = f.PageSize < 10 ? 10 : (f.PageSize > 200 ? 200 : f.PageSize);
            var offset = (page - 1) * pageSize;

            var dataIni = f.DataIni.Date;
            var dataFim = f.DataFim.Date;
            if (dataFim < dataIni) dataFim = dataIni;

            var texto = string.IsNullOrWhiteSpace(f.Texto) ? null : f.Texto.Trim();
            var status = string.IsNullOrWhiteSpace(f.Status) ? null : f.Status.Trim().ToUpperInvariant();

            // ⚠️ Mantive CAST(LOG_TS AS DATE) igual ao seu padrão atual.
            // Se quiser performance melhor depois, trocamos por range de timestamp.
            var where = @"
                WHERE CAST(LOG_TS AS DATE) BETWEEN ? AND ?
                ";

            if (!string.IsNullOrEmpty(metodo))
                where += "  AND METHOD = ?\n";

            if (!string.IsNullOrEmpty(texto))
            {
                // procura no MESSAGE e também nos JSONs (REQUEST/RESPONSE)
                where += @"
                  AND (
                        UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE ?
                     OR UPPER(CAST(REQUEST_JSON AS NVARCHAR(5000))) LIKE ?
                     OR UPPER(CAST(RESPONSE_JSON AS NVARCHAR(5000))) LIKE ?
                  )
                ";
            }

            if (!string.IsNullOrEmpty(status))
            {
                if (status == "OK")
                {
                    where += @"
                    AND (
                        UPPER(CAST(MESSAGE AS NVARCHAR(5000))) = 'OK'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE 'OK%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%SUCESSO%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%HTTP 200%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%HTTP 201%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%CREATED%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%PROCESSADO%'
                    )";
                }

                if (status == "ERRO")
                {
                    where += @"
                    AND (
                        UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%ERRO%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%EXCEPTION%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%FALHA%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%TIMEOUT%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%INVALID%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%INCOMPATIVEL%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%NÃO%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%NAO%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%SEM CORPO%'
                    )";
                }
            }

            // A paginação agora é feita por KEY_SAP, não mais por linha de log.
            // Assim a partial recebe todos os logs da chave SAP selecionada na página
            // e consegue exibir o último log como linha principal + histórico no botão "+".
            var groupKeyExpression = @"
                CASE
                    WHEN KEY_SAP IS NULL OR TRIM(CAST(KEY_SAP AS NVARCHAR(200))) = '' THEN
                        '__SEM_KEY__'
                        || '_' || CAST(LOG_TS AS NVARCHAR(50))
                        || '_' || IFNULL(CAST(KEY_WMS AS NVARCHAR(200)), '')
                        || '_' || IFNULL(CAST(METHOD AS NVARCHAR(300)), '')
                    ELSE TRIM(CAST(KEY_SAP AS NVARCHAR(200)))
                END";

            var sqlCount = $@"
                SELECT COUNT(1) AS TOTAL
                FROM (
                    SELECT {groupKeyExpression} AS GROUP_KEY
                    FROM {table}
                    {where}
                    GROUP BY {groupKeyExpression}
                ) X;
                ";

            var sqlPage = $@"
                WITH BASE AS (
                    SELECT
                        LOG_TS,
                        METHOD,
                        MESSAGE,
                        KEY_WMS,
                        KEY_SAP,
                        REQUEST_JSON,
                        RESPONSE_JSON,
                        {groupKeyExpression} AS GROUP_KEY
                    FROM {table}
                    {where}
                ),
                KEYS_PAGINADAS AS (
                    SELECT
                        GROUP_KEY,
                        MAX(LOG_TS) AS ULTIMO_LOG
                    FROM BASE
                    GROUP BY GROUP_KEY
                    ORDER BY MAX(LOG_TS) DESC
                    LIMIT {pageSize} OFFSET {offset}
                )
                SELECT
                    B.LOG_TS,
                    B.METHOD,
                    B.MESSAGE,
                    B.KEY_WMS,
                    B.KEY_SAP,
                    B.REQUEST_JSON,
                    B.RESPONSE_JSON
                FROM BASE B
                INNER JOIN KEYS_PAGINADAS K
                    ON K.GROUP_KEY = B.GROUP_KEY
                ORDER BY
                    K.ULTIMO_LOG DESC,
                    B.GROUP_KEY,
                    B.LOG_TS DESC;
                ";

            using var conn = new OdbcConnection(_hanaConnStr);
            await conn.OpenAsync(ct);

            int total;
            using (var cmdCount = new OdbcCommand(sqlCount, conn))
            {
                cmdCount.Parameters.Add("pDataIni", OdbcType.Date).Value = dataIni;
                cmdCount.Parameters.Add("pDataFim", OdbcType.Date).Value = dataFim;

                if (!string.IsNullOrEmpty(metodo))
                    cmdCount.Parameters.Add("pMetodo", OdbcType.NVarChar, 200).Value = metodo;

                if (!string.IsNullOrEmpty(texto))
                {
                    var like = "%" + texto.ToUpperInvariant() + "%";
                    cmdCount.Parameters.Add("pTexto1", OdbcType.NVarChar, 5200).Value = like;
                    cmdCount.Parameters.Add("pTexto2", OdbcType.NVarChar, 5200).Value = like;
                    cmdCount.Parameters.Add("pTexto3", OdbcType.NVarChar, 5200).Value = like;
                }

                object? r = await cmdCount.ExecuteScalarAsync(ct);
                total = Convert.ToInt32(r ?? 0);
            }

            var dt = new DataTable();
            using (var cmd = new OdbcCommand(sqlPage, conn))
            {
                cmd.Parameters.Add("pDataIni", OdbcType.Date).Value = dataIni;
                cmd.Parameters.Add("pDataFim", OdbcType.Date).Value = dataFim;

                if (!string.IsNullOrEmpty(metodo))
                    cmd.Parameters.Add("pMetodo", OdbcType.NVarChar, 200).Value = metodo;

                if (!string.IsNullOrEmpty(texto))
                {
                    var like = "%" + texto.ToUpperInvariant() + "%";
                    cmd.Parameters.Add("pTexto1", OdbcType.NVarChar, 5200).Value = like;
                    cmd.Parameters.Add("pTexto2", OdbcType.NVarChar, 5200).Value = like;
                    cmd.Parameters.Add("pTexto3", OdbcType.NVarChar, 5200).Value = like;
                }

                using var da = new OdbcDataAdapter(cmd);
                da.Fill(dt);
            }

            return (dt, total);
        }

        public async Task<DataTable> BuscarLogsAsync(
            IntegracoesWmsFiltroVm f,
            string aba,
            CancellationToken ct)
        {
            aba = (aba ?? "").Trim().ToLowerInvariant();

            // Ajuste os nomes conforme você usa no front (ex.: "envios"/"retornos", "1"/"2", etc.)
            var isRetornos = aba == "retornos" || aba == "retorno" || aba == "api";

            var (rows, _) = isRetornos
                ? await BuscarRetornosAsync(f, ct)
                : await BuscarEnviosAsync(f, ct);

            return rows;
        }

        public async Task<DataTable> BuscarLogsParaExportacaoAsync(
        IntegracoesWmsFiltroVm f,
        string aba,
        int batchSize,
        int maxRows,
        CancellationToken ct)
        {
            aba = (aba ?? "").Trim().ToLowerInvariant();
            var isRetornos = aba == "retornos" || aba == "retorno" || aba == "api";

            string table = isRetornos
                ? LogApiTable
                : FonteEnviosComFilaFaturamento;

            string? metodo = string.IsNullOrWhiteSpace(f.Metodo) ? null : f.Metodo.Trim();
            if (!isRetornos) // só valida contra lista fixa para Envios (se quiser, valide também para retornos)
            {
                if (metodo != null && !MetodosEnvio.Contains(metodo))
                    metodo = null;
            }

            var dataIni = f.DataIni.Date;
            var dataFim = f.DataFim.Date;
            if (dataFim < dataIni) dataFim = dataIni;

            var texto = string.IsNullOrWhiteSpace(f.Texto) ? null : f.Texto.Trim();
            var status = string.IsNullOrWhiteSpace(f.Status) ? null : f.Status.Trim().ToUpperInvariant();

            var where = @"
            WHERE CAST(LOG_TS AS DATE) BETWEEN ? AND ?
        ";

            if (!string.IsNullOrEmpty(metodo))
                where += " AND METHOD = ?\n";

            if (!string.IsNullOrEmpty(texto))
            {
                where += @"
              AND (
                    UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE ?
                 OR UPPER(CAST(REQUEST_JSON AS NVARCHAR(5000))) LIKE ?
                 OR UPPER(CAST(RESPONSE_JSON AS NVARCHAR(5000))) LIKE ?
              )
            ";
            }

            if (!string.IsNullOrEmpty(status))
            {
                if (status == "OK")
                {
                    where += @"
                    AND (
                        UPPER(CAST(MESSAGE AS NVARCHAR(5000))) = 'OK'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE 'OK%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%SUCESSO%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%HTTP 200%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%HTTP 201%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%CREATED%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%PROCESSADO%'
                    )";
                }
                else if (status == "ERRO")
                {
                    where += @"
                    AND (
                        UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%ERRO%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%EXCEPTION%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%FALHA%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%TIMEOUT%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%INVALID%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%INCOMPATIVEL%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%NÃO%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%NAO%'
                        OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%SEM CORPO%'
                    )";
                }
            }

            // SELECT padrão (mesmas colunas da grid)
            string baseSelect = $@"
            SELECT
                LOG_TS,
                METHOD,
                MESSAGE,
                KEY_WMS,
                KEY_SAP,
                REQUEST_JSON,
                RESPONSE_JSON
            FROM {table}
            {where}
            ORDER BY LOG_TS DESC
        ";

            using var conn = new OdbcConnection(_hanaConnStr);
            await conn.OpenAsync(ct);

            var result = new DataTable();
            int offset = 0;
            int totalCarregado = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var sqlBatch = baseSelect + $"\n LIMIT {batchSize} OFFSET {offset};";

                using var cmd = new OdbcCommand(sqlBatch, conn);

                cmd.Parameters.Add("pDataIni", OdbcType.Date).Value = dataIni;
                cmd.Parameters.Add("pDataFim", OdbcType.Date).Value = dataFim;

                if (!string.IsNullOrEmpty(metodo))
                    cmd.Parameters.Add("pMetodo", OdbcType.NVarChar, 200).Value = metodo;

                if (!string.IsNullOrEmpty(texto))
                {
                    var like = "%" + texto.ToUpperInvariant() + "%";
                    cmd.Parameters.Add("pTexto1", OdbcType.NVarChar, 5200).Value = like;
                    cmd.Parameters.Add("pTexto2", OdbcType.NVarChar, 5200).Value = like;
                    cmd.Parameters.Add("pTexto3", OdbcType.NVarChar, 5200).Value = like;
                }

                var batch = new DataTable();
                using (var da = new OdbcDataAdapter(cmd))
                    da.Fill(batch);

                if (batch.Rows.Count == 0)
                    break;

                // inicializa estrutura (colunas) no primeiro batch
                if (result.Columns.Count == 0)
                    result = batch.Clone();

                foreach (DataRow r in batch.Rows)
                {
                    result.ImportRow(r);
                    totalCarregado++;

                    if (maxRows > 0 && totalCarregado >= maxRows)
                        return result;
                }

                offset += batchSize;
            }

            return result;
        }

        public async Task<IntegracoesWmsResumoVm> BuscarResumoAsync(
    IntegracoesWmsFiltroVm f,
    string aba,
    CancellationToken ct)
        {
            aba = (aba ?? "").Trim().ToLowerInvariant();
            bool isRetornos = aba == "retornos" || aba == "retorno" || aba == "api";

            return isRetornos
                ? await BuscarResumoRetornosAsync(f, ct)
                : await BuscarResumoEnviosAsync(f, ct);
        }

        private async Task<IntegracoesWmsResumoVm> BuscarResumoEnviosAsync(
    IntegracoesWmsFiltroVm f,
    CancellationToken ct)
        {
            var dataIni = f.DataIni.Date;
            var dataFim = f.DataFim.Date;
            if (dataFim < dataIni) dataFim = dataIni;

            var table = FonteEnviosComFilaFaturamento;

            var sql = $@"
        WITH BASE AS (
            SELECT
                LOG_TS,
                METHOD,
                MESSAGE,
                CASE
                    WHEN KEY_SAP IS NULL OR TRIM(CAST(KEY_SAP AS NVARCHAR(200))) = '' THEN
                        '__SEM_KEY__'
                        || '_' || CAST(LOG_TS AS NVARCHAR(50))
                        || '_' || IFNULL(CAST(KEY_WMS AS NVARCHAR(200)), '')
                        || '_' || IFNULL(CAST(METHOD AS NVARCHAR(300)), '')
                    ELSE TRIM(CAST(KEY_SAP AS NVARCHAR(200)))
                END AS GROUP_KEY
            FROM {table}
            WHERE CAST(LOG_TS AS DATE) BETWEEN ? AND ?
        ),
        RANKED AS (
            SELECT
                LOG_TS,
                METHOD,
                MESSAGE,
                GROUP_KEY,
                COUNT(1) OVER (PARTITION BY GROUP_KEY) AS TENTATIVAS_GRUPO,
                ROW_NUMBER() OVER (PARTITION BY GROUP_KEY ORDER BY LOG_TS DESC) AS RN
            FROM BASE
        )
        SELECT
            METHOD,
            COUNT(1) AS TOTAL,
            SUM(TENTATIVAS_GRUPO) AS TENTATIVAS,
            SUM(
                CASE
                    WHEN UPPER(CAST(MESSAGE AS NVARCHAR(5000))) = 'OK'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE 'OK%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%SUCESSO%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%HTTP 200%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%HTTP 201%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%CREATED%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%PROCESSADO%'
                    THEN 1 ELSE 0
                END
            ) AS TOTAL_OK,
            SUM(
                CASE
                    WHEN UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%ERRO%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%EXCEPTION%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%FALHA%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%TIMEOUT%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%INVALID%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%INCOMPATIVEL%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%NÃO%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%NAO%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%SEM CORPO%'
                    THEN 1 ELSE 0
                END
            ) AS TOTAL_ERRO
        FROM RANKED
        WHERE RN = 1
        GROUP BY METHOD
    ";

            using var conn = new OdbcConnection(_hanaConnStr);
            await conn.OpenAsync(ct);

            var totais = new Dictionary<string, (int Total, int Ok, int Erro, int Tentativas)>(StringComparer.OrdinalIgnoreCase);

            using (var cmd = new OdbcCommand(sql, conn))
            {
                cmd.Parameters.Add("pDataIni", OdbcType.Date).Value = dataIni;
                cmd.Parameters.Add("pDataFim", OdbcType.Date).Value = dataFim;

                using var rd = await cmd.ExecuteReaderAsync(ct);
                while (await rd.ReadAsync(ct))
                {
                    var method = rd["METHOD"]?.ToString()?.Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(method))
                        continue;

                    var total = rd["TOTAL"] == DBNull.Value ? 0 : Convert.ToInt32(rd["TOTAL"]);
                    var ok = rd["TOTAL_OK"] == DBNull.Value ? 0 : Convert.ToInt32(rd["TOTAL_OK"]);
                    var erro = rd["TOTAL_ERRO"] == DBNull.Value ? 0 : Convert.ToInt32(rd["TOTAL_ERRO"]);
                    var tentativas = rd["TENTATIVAS"] == DBNull.Value ? 0 : Convert.ToInt32(rd["TENTATIVAS"]);

                    totais[method] = (total, ok, erro, tentativas);
                }
            }

            var itens = MetodosEnvio
                .Select(m =>
                {
                    var dados = totais.TryGetValue(m, out var qtd)
                        ? qtd
                        : (0, 0, 0, 0);

                    var neutro = dados.Item1 - dados.Item2 - dados.Item3;
                    if (neutro < 0) neutro = 0;

                    return new IntegracoesWmsResumoItemVm
                    {
                        Chave = m,
                        Titulo = ObterTituloMetodo(m),
                        Quantidade = dados.Item1,
                        Ok = dados.Item2,
                        Erro = dados.Item3,
                        Neutro = neutro,
                        Tentativas = dados.Item4,
                        Tipo = "metodo"
                    };
                })
                .ToList();

            return new IntegracoesWmsResumoVm
            {
                Aba = "envios",
                Itens = itens
            };
        }

        private async Task<IntegracoesWmsResumoVm> BuscarResumoRetornosAsync(
    IntegracoesWmsFiltroVm f,
    CancellationToken ct)
        {
            var dataIni = f.DataIni.Date;
            var dataFim = f.DataFim.Date;
            if (dataFim < dataIni) dataFim = dataIni;

            const string table = LogApiTable;

            var sql = $@"
        WITH BASE AS (
            SELECT
                LOG_TS,
                MESSAGE,
                CASE
                    WHEN KEY_SAP IS NULL OR TRIM(CAST(KEY_SAP AS NVARCHAR(200))) = '' THEN
                        '__SEM_KEY__'
                        || '_' || CAST(LOG_TS AS NVARCHAR(50))
                        || '_' || IFNULL(CAST(KEY_WMS AS NVARCHAR(200)), '')
                        || '_' || IFNULL(CAST(METHOD AS NVARCHAR(300)), '')
                    ELSE TRIM(CAST(KEY_SAP AS NVARCHAR(200)))
                END AS GROUP_KEY
            FROM {table}
            WHERE CAST(LOG_TS AS DATE) BETWEEN ? AND ?
        ),
        RANKED AS (
            SELECT
                LOG_TS,
                MESSAGE,
                GROUP_KEY,
                COUNT(1) OVER (PARTITION BY GROUP_KEY) AS TENTATIVAS_GRUPO,
                ROW_NUMBER() OVER (PARTITION BY GROUP_KEY ORDER BY LOG_TS DESC) AS RN
            FROM BASE
        ),
        CLASSIFICADO AS (
            SELECT
                MESSAGE,
                TENTATIVAS_GRUPO,
                CASE
                    WHEN UPPER(CAST(MESSAGE AS NVARCHAR(5000))) = 'OK'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE 'OK%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%SUCESSO%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%HTTP 200%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%HTTP 201%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%CREATED%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%PROCESSADO%'
                    THEN 1 ELSE 0
                END AS IS_OK,
                CASE
                    WHEN UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%ERRO%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%EXCEPTION%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%FALHA%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%TIMEOUT%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%INVALID%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%INCOMPATIVEL%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%NÃO%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%NAO%'
                      OR UPPER(CAST(MESSAGE AS NVARCHAR(5000))) LIKE '%SEM CORPO%'
                    THEN 1 ELSE 0
                END AS IS_ERRO
            FROM RANKED
            WHERE RN = 1
        )
        SELECT
            COUNT(1) AS TOTAL,
            SUM(TENTATIVAS_GRUPO) AS TENTATIVAS,
            SUM(IS_OK) AS TOTAL_OK,
            SUM(CASE WHEN IS_OK = 1 THEN TENTATIVAS_GRUPO ELSE 0 END) AS TENTATIVAS_OK,
            SUM(IS_ERRO) AS TOTAL_ERRO,
            SUM(CASE WHEN IS_ERRO = 1 THEN TENTATIVAS_GRUPO ELSE 0 END) AS TENTATIVAS_ERRO
        FROM CLASSIFICADO
    ";

            using var conn = new OdbcConnection(_hanaConnStr);
            await conn.OpenAsync(ct);

            int total = 0;
            int ok = 0;
            int erro = 0;
            int tentativas = 0;
            int tentativasOk = 0;
            int tentativasErro = 0;

            using (var cmd = new OdbcCommand(sql, conn))
            {
                cmd.Parameters.Add("pDataIni", OdbcType.Date).Value = dataIni;
                cmd.Parameters.Add("pDataFim", OdbcType.Date).Value = dataFim;

                using var rd = await cmd.ExecuteReaderAsync(ct);
                if (await rd.ReadAsync(ct))
                {
                    total = rd["TOTAL"] == DBNull.Value ? 0 : Convert.ToInt32(rd["TOTAL"]);
                    ok = rd["TOTAL_OK"] == DBNull.Value ? 0 : Convert.ToInt32(rd["TOTAL_OK"]);
                    erro = rd["TOTAL_ERRO"] == DBNull.Value ? 0 : Convert.ToInt32(rd["TOTAL_ERRO"]);
                    tentativas = rd["TENTATIVAS"] == DBNull.Value ? 0 : Convert.ToInt32(rd["TENTATIVAS"]);
                    tentativasOk = rd["TENTATIVAS_OK"] == DBNull.Value ? 0 : Convert.ToInt32(rd["TENTATIVAS_OK"]);
                    tentativasErro = rd["TENTATIVAS_ERRO"] == DBNull.Value ? 0 : Convert.ToInt32(rd["TENTATIVAS_ERRO"]);
                }
            }

            return new IntegracoesWmsResumoVm
            {
                Aba = "retornos",
                Itens = new List<IntegracoesWmsResumoItemVm>
        {
            new()
            {
                Chave = "TODOS",
                Titulo = "Todos",
                Quantidade = total,
                Tentativas = tentativas,
                Tipo = "status"
            },
            new()
            {
                Chave = "OK",
                Titulo = "OK",
                Quantidade = ok,
                Tentativas = tentativasOk,
                Tipo = "status"
            },
            new()
            {
                Chave = "ERRO",
                Titulo = "Erro",
                Quantidade = erro,
                Tentativas = tentativasErro,
                Tipo = "status"
            }
        }
            };
        }

        private static string ObterTituloMetodo(string method)
        {
            if (string.IsNullOrWhiteSpace(method)) return "Método";

            if (method.Contains("cadastrarProduto", StringComparison.OrdinalIgnoreCase))
                return "Cadastrar Produto";

            if (method.Contains("cadastrarPessoa", StringComparison.OrdinalIgnoreCase))
                return "Cadastrar Pessoa";

            if (method.Contains("enviarOrdemSaida", StringComparison.OrdinalIgnoreCase))
                return "Enviar Ordem Saída";

            if (method.Contains("enviarCancelamentoOrdemSaida", StringComparison.OrdinalIgnoreCase))
                return "Cancelar Ordem Saída";

            if (method.Contains("enviarOrdemEntrada", StringComparison.OrdinalIgnoreCase))
                return "Enviar Ordem Entrada";

            if (method.Contains("enviarEstoque", StringComparison.OrdinalIgnoreCase))
                return "Enviar Estoque";

            if (method.Contains("enviarFaturamentoOrdemSaida", StringComparison.OrdinalIgnoreCase))
                return "Enviar Faturamento";

            return method;
        }
    }
}
