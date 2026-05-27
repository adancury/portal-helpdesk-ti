using Microsoft.Extensions.Configuration;
using PortalHelpdeskTI.Models.Relatorios;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Odbc;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace PortalHelpdeskTI.Services.Relatorios
{
    public class DashboardSavingComprasService
    {
        private readonly string _connStr;

        public DashboardSavingComprasService(IConfiguration config)
        {
            // Ajuste o nome da connection string conforme o que você já usa
            _connStr = config.GetConnectionString("HanaConn")
                      ?? throw new InvalidOperationException("Connection string 'HanaConn' não encontrada.");
        }

        /// <summary>
        /// Gera o resumo do período (cards + grid paginada) para o Dashboard de Saving de Compras.
        /// </summary>
        public async Task<DashboardSavingComprasVM> GerarResumoPeriodoAsync(
    DateTime dataDe,
    DateTime dataAte,
    string[]? filtrosDepartamentos = null,
    string? equipeResponsavel = null,
    int pagina = 1,
    string? termoBusca = null)
        {
            // Normaliza o filtro da equipe responsável (grid)
            var eq = (equipeResponsavel ?? string.Empty).Trim();
            var eqNorm =
                eq.Equals("Compras", StringComparison.OrdinalIgnoreCase) ? "Compras" :
                eq.Equals("Importacao", StringComparison.OrdinalIgnoreCase) ? "Importacao" :
                eq.Equals("Importação", StringComparison.OrdinalIgnoreCase) ? "Importacao" :
                "Todos";

            var vm = new DashboardSavingComprasVM
            {
                DataDe = dataDe,
                DataAte = dataAte,
                DepartamentosSelecionados = filtrosDepartamentos ?? Array.Empty<string>(),
                PaginaAtual = pagina < 1 ? 1 : pagina,
                TamanhoPagina = 30,
                TermoBusca = termoBusca,
                EquipeResponsavelSelecionada = eqNorm
            };

            using (var conn = new OdbcConnection(_connStr))
            {
                await conn.OpenAsync();

                // Sincroniza SOMENTE na primeira página e sem termo de busca
                if (vm.PaginaAtual == 1 && string.IsNullOrWhiteSpace(vm.TermoBusca))
                {
                    await SincronizarProcessosAPartirDeOPRQAsync(conn, dataDe, dataAte);
                }

                // 1) Carrega todos os processos do período (sem paginação ainda)
                var processos = await CarregarProcessosAsync(
                    conn,
                    dataDe,
                    dataAte,
                    vm.DepartamentosSelecionados,
                    vm.EquipeResponsavelSelecionada
                );

                if (processos.Count == 0)
                {
                    vm.TotalRegistros = 0;
                    vm.TotalPaginas = 0;
                    vm.Linhas = new List<ProcessoCompraLinhaVM>();
                    return vm;
                }

                // 2) Aplica busca em memória (pedido, solicitação, fornecedor)
                IEnumerable<ProcessoCompraResumo> query = processos;

                if (!string.IsNullOrWhiteSpace(vm.TermoBusca))
                {
                    var termo = vm.TermoBusca.Trim();

                    query = query.Where(p =>
                        // Pedido de compra (OPOR.DocNum)
                        (p.OPORDocNum.HasValue &&
                         p.OPORDocNum.Value.ToString().Contains(termo, StringComparison.OrdinalIgnoreCase))
                        ||
                        // Nº da solicitação (OPRQ.DocNum)
                        p.OPRQDocNum.ToString().Contains(termo, StringComparison.OrdinalIgnoreCase)
                        ||
                        // Fornecedor vencedor - código
                        (!string.IsNullOrWhiteSpace(p.CardCodeVencedor) &&
                         p.CardCodeVencedor.IndexOf(termo, StringComparison.OrdinalIgnoreCase) >= 0)
                        ||
                        // Fornecedor vencedor - nome
                        (!string.IsNullOrWhiteSpace(p.CardNameVencedor) &&
                         p.CardNameVencedor.IndexOf(termo, StringComparison.OrdinalIgnoreCase) >= 0)
                        ||
                        // Solicitante
                        (!string.IsNullOrWhiteSpace(p.Solicitante) &&
                         p.Solicitante.IndexOf(termo, StringComparison.OrdinalIgnoreCase) >= 0)
                        ||
                        // Departamento (OUDP) na busca
                        (!string.IsNullOrWhiteSpace(p.Departamento) &&
                         p.Departamento.IndexOf(termo, StringComparison.OrdinalIgnoreCase) >= 0)
                    );
                }

                // 3) Total filtrado (antes da paginação)
                vm.TotalRegistros = query.Count();

                if (vm.TotalRegistros == 0)
                {
                    vm.TotalPaginas = 0;
                    vm.Linhas = new List<ProcessoCompraLinhaVM>();
                    vm.TotalComprasPeriodo = 0m;
                    vm.TotalSavingPeriodo = 0m;
                    vm.SavingMedioPercentual = 0m;
                    vm.QtdProcessos = 0;
                    vm.QtdFornecedoresEnvolvidos = 0;
                    return vm;
                }

                // 4) Carrega cotações concorrentes apenas dos processos filtrados
                var idsFiltrados = query.Select(p => p.ProcessoId).ToList();
                var concorrentesFiltrados = await CarregarConcorrentesAsync(conn, idsFiltrados);

                var concorrentesPorProcessoFull = concorrentesFiltrados
                    .GroupBy(c => c.ProcessoCompraId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // 5) Cálculo dos cards (considera TODOS os itens filtrados)
                decimal totalComprasFull = 0m;
                decimal totalSavingFull = 0m;
                var listaSavingPercentualFull = new List<decimal>();
                var fornecedoresFull = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                int processosSemCotacao = 0;

                foreach (var proc in query)
                {
                    if (!string.IsNullOrWhiteSpace(proc.CardCodeVencedor))
                        fornecedoresFull.Add(proc.CardCodeVencedor.Trim());

                    decimal? valorBase = null;
                    bool temConcorrentes = false;

                    if (concorrentesPorProcessoFull.TryGetValue(proc.ProcessoId, out var listaConc))
                    {
                        temConcorrentes = listaConc.Count > 0;

                        if (temConcorrentes)
                            valorBase = listaConc.Min(c => c.ValorCotacaoTotal);

                        foreach (var cc in listaConc)
                        {
                            if (!string.IsNullOrWhiteSpace(cc.CardCodeFornecedor))
                                fornecedoresFull.Add(cc.CardCodeFornecedor.Trim());
                        }
                    }

                    if (!temConcorrentes)
                        processosSemCotacao++;

                    if (proc.ValorVencedor.HasValue)
                        totalComprasFull += proc.ValorVencedor.Value;

                    if (valorBase.HasValue && proc.ValorVencedor.HasValue && valorBase.Value > 0m)
                    {
                        var s = valorBase.Value - proc.ValorVencedor.Value;
                        totalSavingFull += s;

                        var sp = (s / valorBase.Value) * 100m;
                        listaSavingPercentualFull.Add(sp);
                    }
                }

                vm.TotalComprasPeriodo = totalComprasFull;
                vm.TotalSavingPeriodo = totalSavingFull;
                vm.SavingMedioPercentual = listaSavingPercentualFull.Count > 0
                    ? listaSavingPercentualFull.Average()
                    : 0m;

                var savingPorDepartamento = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

                foreach (var proc in query)
                {
                    if (string.IsNullOrWhiteSpace(proc.Departamento))
                        continue;

                    if (!concorrentesPorProcessoFull.TryGetValue(proc.ProcessoId, out var concs))
                        continue;

                    if (concs.Count == 0 || !proc.ValorVencedor.HasValue)
                        continue;

                    var melhorCotacao = concs.Min(c => c.ValorCotacaoTotal);
                    if (melhorCotacao <= 0)
                        continue;

                    var saving = melhorCotacao - proc.ValorVencedor.Value;
                    if (saving <= 0)
                        continue;

                    if (!savingPorDepartamento.ContainsKey(proc.Departamento))
                        savingPorDepartamento[proc.Departamento] = 0m;

                    savingPorDepartamento[proc.Departamento] += saving;
                }

                vm.TopDepartamentosSaving = savingPorDepartamento
                    .OrderByDescending(x => x.Value)
                    .Take(3)
                    .Select(x => new TopDepartamentoSavingVM
                    {
                        Departamento = x.Key,
                        TotalSaving = x.Value,
                        PercentualSobreSaving = vm.TotalSavingPeriodo > 0
                            ? (x.Value / vm.TotalSavingPeriodo) * 100m
                            : 0m
                    }).ToList();

                var gastosPorDepartamento = query
                    .Where(p => !string.IsNullOrWhiteSpace(p.Departamento) && p.ValorVencedor.HasValue)
                    .GroupBy(p => p.Departamento!)
                    .Select(g => new
                    {
                        Departamento = g.Key,
                        Total = g.Sum(x => x.ValorVencedor!.Value)
                    })
                    .OrderByDescending(x => x.Total)
                    .Take(3)
                    .ToList();

                vm.TopDepartamentosGasto = gastosPorDepartamento
                    .Select(x => new TopDepartamentoGastoVM
                    {
                        Departamento = x.Departamento,
                        TotalGasto = x.Total,
                        PercentualSobreTotal = vm.TotalComprasPeriodo > 0
                            ? (x.Total / vm.TotalComprasPeriodo) * 100m
                            : 0m
                    }).ToList();

                vm.QtdProcessos = vm.TotalRegistros;
                vm.QtdFornecedoresEnvolvidos = fornecedoresFull.Count;

                vm.QtdProcessosSemCotacao = processosSemCotacao;
                vm.PercentualProcessosSemCotacao = vm.QtdProcessos > 0
                    ? (processosSemCotacao / (decimal)vm.QtdProcessos) * 100m
                    : 0m;

                vm.QtdProcessosEmCotacao = query.Count(p => p.StatusProcesso.Equals("Em Cotacao", StringComparison.OrdinalIgnoreCase));
                vm.QtdProcessosFechados = query.Count(p => p.StatusProcesso.Equals("Fechado", StringComparison.OrdinalIgnoreCase));

                vm.PercentualEmCotacao = vm.QtdProcessos > 0
                    ? (vm.QtdProcessosEmCotacao / (decimal)vm.QtdProcessos) * 100m
                    : 0m;

                vm.PercentualFechados = vm.QtdProcessos > 0
                    ? (vm.QtdProcessosFechados / (decimal)vm.QtdProcessos) * 100m
                    : 0m;

                // 6) Ordenação + paginação para a GRID
                query = query
                    .OrderByDescending(p => p.DataSolicitacao)
                    .ThenByDescending(p => p.OPRQDocNum);

                vm.TotalPaginas = (int)Math.Ceiling(vm.TotalRegistros / (double)vm.TamanhoPagina);
                if (vm.PaginaAtual > vm.TotalPaginas)
                    vm.PaginaAtual = vm.TotalPaginas;

                var processosPagina = query
                    .Skip((vm.PaginaAtual - 1) * vm.TamanhoPagina)
                    .Take(vm.TamanhoPagina)
                    .ToList();

                // 7) Monta as linhas da GRID
                var linhas = new List<ProcessoCompraLinhaVM>();

                foreach (var proc in processosPagina)
                {
                    if (!concorrentesPorProcessoFull.TryGetValue(proc.ProcessoId, out var concProc))
                        concProc = new List<CotacaoConcorrenteResumo>();

                    decimal? valorBase = concProc.Count > 0 ? concProc.Min(c => c.ValorCotacaoTotal) : null;

                    decimal? savingValor = null;
                    decimal? savingPercentual = null;

                    if (valorBase.HasValue && valorBase.Value > 0m && proc.ValorVencedor.HasValue)
                    {
                        savingValor = valorBase.Value - proc.ValorVencedor.Value;
                        savingPercentual = (savingValor.Value / valorBase.Value) * 100m;
                    }

                    linhas.Add(new ProcessoCompraLinhaVM
                    {
                        ProcessoId = proc.ProcessoId,
                        OPRQDocEntry = proc.OPRQDocEntry,
                        OPRQDocNum = proc.OPRQDocNum,
                        DataSolicitacao = proc.DataSolicitacao,
                        Solicitante = proc.Solicitante,
                        Departamento = proc.Departamento,
                        OPORDocEntry = proc.OPORDocEntry,
                        OPORDocNum = proc.OPORDocNum,
                        DataPedido = proc.DataPedido,
                        CardCodeVencedor = proc.CardCodeVencedor,
                        CardNameVencedor = proc.CardNameVencedor,
                        ValorVencedor = proc.ValorVencedor,
                        ValorBaseConcorrentes = valorBase,
                        SavingValor = savingValor,
                        SavingPercentual = savingPercentual,
                        QtdConcorrentes = concProc.Count,
                        StatusProcesso = proc.StatusProcesso ?? string.Empty
                    });
                }

                vm.Linhas = linhas;

                return vm;
            }
        }

        // --------------------------------------------------------------------
        // Helpers privados
        // --------------------------------------------------------------------

        private async Task<ProcessoCompraResumo?> CarregarProcessoPorIdAsync(
            OdbcConnection conn,
            int processoId)
        {
            string sql =
                "SELECT " +
                "   \"Id\", " +
                "   \"OPRQDocEntry\", \"OPRQDocNum\", \"DataSolicitacao\", " +
                "   \"Solicitante\", \"Departamento\", " +
                "   \"OPORDocEntry\", \"OPORDocNum\", \"DataPedido\", " +
                "   \"CardCodeVencedor\", \"CardNameVencedor\", \"ValorVencedor\", " +
                "   \"StatusProcesso\" " +
                "FROM \"ProcessosCompra\" " +
                "WHERE \"Id\" = ?";

            using (var cmd = new OdbcCommand(sql, conn))
            {
                cmd.Parameters.Add(new OdbcParameter
                {
                    OdbcType = OdbcType.Int,
                    Value = processoId
                });

                using (var rd = await cmd.ExecuteReaderAsync())
                {
                    if (await rd.ReadAsync())
                    {
                        var p = new ProcessoCompraResumo
                        {
                            ProcessoId = rd.GetInt32(rd.GetOrdinal("Id")),
                            OPRQDocEntry = rd.GetInt32(rd.GetOrdinal("OPRQDocEntry")),
                            OPRQDocNum = rd.GetInt32(rd.GetOrdinal("OPRQDocNum")),
                            DataSolicitacao = rd.GetDateTime(rd.GetOrdinal("DataSolicitacao")),
                            Solicitante = GetStringOrNull(rd, "Solicitante"),
                            Departamento = GetStringOrNull(rd, "Departamento"),
                            StatusProcesso = GetStringOrNull(rd, "StatusProcesso") ?? string.Empty
                        };

                        int idx;

                        idx = rd.GetOrdinal("OPORDocEntry");
                        p.OPORDocEntry = rd.IsDBNull(idx) ? (int?)null : rd.GetInt32(idx);

                        idx = rd.GetOrdinal("OPORDocNum");
                        p.OPORDocNum = rd.IsDBNull(idx) ? (int?)null : rd.GetInt32(idx);

                        idx = rd.GetOrdinal("DataPedido");
                        p.DataPedido = rd.IsDBNull(idx) ? (DateTime?)null : rd.GetDateTime(idx);

                        p.CardCodeVencedor = GetStringOrNull(rd, "CardCodeVencedor");
                        p.CardNameVencedor = GetStringOrNull(rd, "CardNameVencedor");

                        idx = rd.GetOrdinal("ValorVencedor");
                        p.ValorVencedor = rd.IsDBNull(idx) ? (decimal?)null : rd.GetDecimal(idx);

                        return p;
                    }
                }
            }

            return null;
        }

        private async Task<List<CotacaoConcorrenteLinhaVM>> CarregarCotacoesDetalhesAsync(
            OdbcConnection conn,
            int processoId)
        {
            var lista = new List<CotacaoConcorrenteLinhaVM>();

            string sql =
                "SELECT " +
                "   \"Id\", \"ProcessoCompraId\", " +
                "   \"CardCodeFornecedor\", \"CardNameFornecedor\", \"ValorCotacaoTotal\", " +
                "   \"PrazoEntregaDias\", \"CondicaoPagamento\", \"Observacoes\", " +
                "   \"CriadoEm\" " +
                "FROM \"CotacoesConcorrentesCompra\" " +
                "WHERE \"ProcessoCompraId\" = ? " +
                "ORDER BY \"ValorCotacaoTotal\" ASC";

            using (var cmd = new OdbcCommand(sql, conn))
            {
                cmd.Parameters.Add(new OdbcParameter
                {
                    OdbcType = OdbcType.Int,
                    Value = processoId
                });

                using (var rd = await cmd.ExecuteReaderAsync())
                {
                    while (await rd.ReadAsync())
                    {
                        var c = new CotacaoConcorrenteLinhaVM
                        {
                            Id = rd.GetInt32(rd.GetOrdinal("Id")),
                            ProcessoCompraId = rd.GetInt32(rd.GetOrdinal("ProcessoCompraId")),
                            CardCodeFornecedor = GetStringOrNull(rd, "CardCodeFornecedor") ?? string.Empty,
                            CardNameFornecedor = GetStringOrNull(rd, "CardNameFornecedor") ?? string.Empty,
                            ValorCotacaoTotal = rd.GetDecimal(rd.GetOrdinal("ValorCotacaoTotal")),
                            PrazoEntregaDias = rd.IsDBNull(rd.GetOrdinal("PrazoEntregaDias"))
                                ? (int?)null
                                : rd.GetInt32(rd.GetOrdinal("PrazoEntregaDias")),
                            CondicaoPagamento = GetStringOrNull(rd, "CondicaoPagamento"),
                            Observacoes = GetStringOrNull(rd, "Observacoes"),
                            CriadoEm = rd.GetDateTime(rd.GetOrdinal("CriadoEm"))
                        };

                        lista.Add(c);
                    }
                }
            }

            return lista;
        }

        private async Task<List<ProcessoCompraResumo>> CarregarProcessosAsync(
    OdbcConnection conn,
    DateTime dataDe,
    DateTime dataAte,
    string[] filtrosDepartamentos,
    string? filtroEquipeResponsavel // "Todos" | "Compras" | "Importacao"
)
        {
            var lista = new List<ProcessoCompraResumo>();

            bool temFiltroDepto = filtrosDepartamentos != null && filtrosDepartamentos.Length > 0;

            // Normaliza o filtro (padrão = Todos)
            var equipe = string.IsNullOrWhiteSpace(filtroEquipeResponsavel)
                ? "Todos"
                : filtroEquipeResponsavel.Trim();

            string sql =
                "SELECT " +
                "   \"Id\", " +
                "   \"OPRQDocEntry\", " +
                "   \"OPRQDocNum\", " +
                "   \"DataSolicitacao\", " +
                "   \"Solicitante\", " +
                "   \"Departamento\", " +
                "   \"EquipeResponsavel\", " + // NOVO
                "   \"OPORDocEntry\", " +
                "   \"OPORDocNum\", " +
                "   \"DataPedido\", " +
                "   \"CardCodeVencedor\", " +
                "   \"CardNameVencedor\", " +
                "   \"ValorVencedor\", " +
                "   \"StatusProcesso\" " +
                "FROM \"ProcessosCompra\" " +
                "WHERE \"DataSolicitacao\" BETWEEN ? AND ? ";

            // NOVO: filtro de linhas da GRID por equipe responsável
            // Se equipe = Todos => não filtra
            sql += " AND ( ? = 'Todos' OR \"EquipeResponsavel\" = ? ) ";

            // Filtro antigo por Departamento (OUDP) permanece funcionando
            if (temFiltroDepto)
            {
                var inParts = new List<string>();
                for (int i = 0; i < filtrosDepartamentos.Length; i++)
                    inParts.Add("?");

                sql += " AND \"Departamento\" IN (" + string.Join(", ", inParts) + ")";
            }

            using (var cmd = new OdbcCommand(sql, conn))
            {
                cmd.Parameters.Add(new OdbcParameter
                {
                    OdbcType = OdbcType.Date,
                    Value = dataDe.Date
                });

                cmd.Parameters.Add(new OdbcParameter
                {
                    OdbcType = OdbcType.Date,
                    Value = dataAte.Date
                });

                // NOVO: parâmetros do filtro de equipe (2x por causa do OR)
                cmd.Parameters.Add(new OdbcParameter
                {
                    OdbcType = OdbcType.NVarChar,
                    Value = equipe
                });

                cmd.Parameters.Add(new OdbcParameter
                {
                    OdbcType = OdbcType.NVarChar,
                    Value = equipe
                });

                if (temFiltroDepto)
                {
                    foreach (var d in filtrosDepartamentos)
                    {
                        cmd.Parameters.Add(new OdbcParameter
                        {
                            OdbcType = OdbcType.NVarChar,
                            Value = (object?)d ?? DBNull.Value
                        });
                    }
                }

                using (var rd = await cmd.ExecuteReaderAsync())
                {
                    while (await rd.ReadAsync())
                    {
                        var p = new ProcessoCompraResumo
                        {
                            ProcessoId = rd.GetInt32(rd.GetOrdinal("Id")),
                            OPRQDocEntry = rd.GetInt32(rd.GetOrdinal("OPRQDocEntry")),
                            OPRQDocNum = rd.GetInt32(rd.GetOrdinal("OPRQDocNum")),
                            DataSolicitacao = rd.GetDateTime(rd.GetOrdinal("DataSolicitacao")),
                            Solicitante = GetStringOrNull(rd, "Solicitante"),
                            Departamento = GetStringOrNull(rd, "Departamento"),
                            // Se você tiver a propriedade no resumo, pode mapear também:
                            // EquipeResponsavel = GetStringOrNull(rd, "EquipeResponsavel"),
                            StatusProcesso = GetStringOrNull(rd, "StatusProcesso") ?? string.Empty
                        };

                        int idx;

                        idx = rd.GetOrdinal("OPORDocEntry");
                        p.OPORDocEntry = rd.IsDBNull(idx) ? (int?)null : rd.GetInt32(idx);

                        idx = rd.GetOrdinal("OPORDocNum");
                        p.OPORDocNum = rd.IsDBNull(idx) ? (int?)null : rd.GetInt32(idx);

                        idx = rd.GetOrdinal("DataPedido");
                        p.DataPedido = rd.IsDBNull(idx) ? (DateTime?)null : rd.GetDateTime(idx);

                        p.CardCodeVencedor = GetStringOrNull(rd, "CardCodeVencedor");
                        p.CardNameVencedor = GetStringOrNull(rd, "CardNameVencedor");

                        idx = rd.GetOrdinal("ValorVencedor");
                        p.ValorVencedor = rd.IsDBNull(idx) ? (decimal?)null : rd.GetDecimal(idx);

                        lista.Add(p);
                    }
                }
            }

            return lista;
        }


        private async Task<List<CotacaoConcorrenteResumo>> CarregarConcorrentesAsync(
            OdbcConnection conn,
            List<int> processosIds)
        {
            var lista = new List<CotacaoConcorrenteResumo>();

            if (processosIds == null || processosIds.Count == 0)
                return lista;

            var inParts = new List<string>();
            for (int i = 0; i < processosIds.Count; i++)
                inParts.Add("?");

            string sql =
                "SELECT " +
                "   \"Id\", " +
                "   \"ProcessoCompraId\", " +
                "   \"CardCodeFornecedor\", " +
                "   \"CardNameFornecedor\", " +
                "   \"ValorCotacaoTotal\" " +
                "FROM \"CotacoesConcorrentesCompra\" " +
                "WHERE \"ProcessoCompraId\" IN (" + string.Join(", ", inParts) + ")";

            using (var cmd = new OdbcCommand(sql, conn))
            {
                foreach (var id in processosIds)
                {
                    cmd.Parameters.Add(new OdbcParameter
                    {
                        OdbcType = OdbcType.Int,
                        Value = id
                    });
                }

                using (var rd = await cmd.ExecuteReaderAsync())
                {
                    while (await rd.ReadAsync())
                    {
                        var c = new CotacaoConcorrenteResumo
                        {
                            Id = rd.GetInt32(rd.GetOrdinal("Id")),
                            ProcessoCompraId = rd.GetInt32(rd.GetOrdinal("ProcessoCompraId")),
                            CardCodeFornecedor = GetStringOrNull(rd, "CardCodeFornecedor") ?? string.Empty,
                            CardNameFornecedor = GetStringOrNull(rd, "CardNameFornecedor") ?? string.Empty,
                            ValorCotacaoTotal = rd.GetDecimal(rd.GetOrdinal("ValorCotacaoTotal"))
                        };

                        lista.Add(c);
                    }
                }
            }

            return lista;
        }

        private static string? GetStringOrNull(IDataRecord rd, string colName)
        {
            int idx = rd.GetOrdinal(colName);
            if (rd.IsDBNull(idx))
                return null;
            return rd.GetString(idx);
        }

        // --------------------------------------------------------------------
        // Classes internas de resumo (para uso dentro do service)
        // --------------------------------------------------------------------

        private class ProcessoCompraResumo
        {
            public int ProcessoId { get; set; }

            public int OPRQDocEntry { get; set; }
            public int OPRQDocNum { get; set; }
            public DateTime DataSolicitacao { get; set; }
            public string? Solicitante { get; set; }
            public string? Departamento { get; set; }

            public int? OPORDocEntry { get; set; }
            public int? OPORDocNum { get; set; }
            public DateTime? DataPedido { get; set; }
            public string? CardCodeVencedor { get; set; }
            public string? CardNameVencedor { get; set; }
            public decimal? ValorVencedor { get; set; }

            public string StatusProcesso { get; set; } = string.Empty;
        }

        private class CotacaoConcorrenteResumo
        {
            public int Id { get; set; }
            public int ProcessoCompraId { get; set; }
            public string CardCodeFornecedor { get; set; } = string.Empty;
            public string CardNameFornecedor { get; set; } = string.Empty;
            public decimal ValorCotacaoTotal { get; set; }
        }

        // --------------------------------------------------------------------
        // Sincronização com OPRQ/OPOR
        // --------------------------------------------------------------------

        private async Task SincronizarProcessosAPartirDeOPRQAsync(
            OdbcConnection conn,
            DateTime dataDe,
            DateTime dataAte)
        {
            string sql =
                "SELECT DISTINCT " +
                "   R.\"DocEntry\"      AS \"OPRQDocEntry\", " +
                "   R.\"DocNum\"        AS \"OPRQDocNum\", " +
                "   R.\"DocDate\"       AS \"DataSolicitacao\", " +
                "   R.\"ReqName\"       AS \"Solicitante\", " +
                "   R.\"UserSign\"      AS \"UserSign\", " +
                "   D.\"Name\"          AS \"Departamento\", " +
                "   P.\"DocEntry\"      AS \"OPORDocEntry\", " +
                "   P.\"DocNum\"        AS \"OPORDocNum\", " +
                "   P.\"DocDate\"       AS \"DataPedido\", " +
                "   P.\"CardCode\"      AS \"CardCodeVencedor\", " +
                "   P.\"CardName\"      AS \"CardNameVencedor\", " +
                "   P.\"DocTotal\"      AS \"ValorVencedor\", " +
                "   P.\"DocCur\"        AS \"Moeda\", " +
                "   L.\"Usage\" AS \"Usage\", " +
                "   CASE WHEN L.\"Usage\" IN(1, 3) THEN 'Importacao' ELSE 'Compras' END AS \"EquipeResponsavel\" " +
                "FROM \"OPRQ\" R " +
                "LEFT JOIN \"POR1\" L " +
                "   ON L.\"BaseType\" = 1470000113 " +
                "  AND L.\"BaseEntry\" = R.\"DocEntry\" " +
                "LEFT JOIN \"OPOR\" P " +
                "   ON P.\"DocEntry\" = L.\"DocEntry\" " +
                "LEFT JOIN \"OUSR\" U " +
                "   ON U.\"USERID\" = R.\"UserSign\" " +
                "LEFT JOIN \"OHEM\" E " +
                "   ON E.\"userId\" = U.\"USERID\" " +
                "LEFT JOIN \"OUDP\" D " +
                "   ON D.\"Code\" = E.\"dept\" " +
                "WHERE R.\"DocDate\" BETWEEN ? AND ? " +
                "  AND R.\"CANCELED\" = 'N'";

            using (var cmd = new OdbcCommand(sql, conn))
            {
                cmd.Parameters.Add(new OdbcParameter
                {
                    OdbcType = OdbcType.Date,
                    Value = dataDe.Date
                });

                cmd.Parameters.Add(new OdbcParameter
                {
                    OdbcType = OdbcType.Date,
                    Value = dataAte.Date
                });

                using (var rd = await cmd.ExecuteReaderAsync())
                {
                    while (await rd.ReadAsync())
                    {
                        int oprqDocEntry = rd.GetInt32(rd.GetOrdinal("OPRQDocEntry"));
                        int oprqDocNum = rd.GetInt32(rd.GetOrdinal("OPRQDocNum"));
                        DateTime dataSol = rd.GetDateTime(rd.GetOrdinal("DataSolicitacao"));
                        string? solicitante = GetStringOrNull(rd, "Solicitante");
                        string? departamento = GetStringOrNull(rd, "Departamento");
                        int? oporDocEntry = null;
                        int? oporDocNum = null;
                        DateTime? dataPed = null;
                        string? cardCode = null;
                        string? cardName = null;
                        decimal? valorVend = null;
                        string? moeda = null;
                        string equipeResponsavel = GetStringOrNull(rd, "EquipeResponsavel") ?? "Compras";
                        int idx;

                        idx = rd.GetOrdinal("OPORDocEntry");
                        if (!rd.IsDBNull(idx))
                            oporDocEntry = rd.GetInt32(idx);

                        idx = rd.GetOrdinal("OPORDocNum");
                        if (!rd.IsDBNull(idx))
                            oporDocNum = rd.GetInt32(idx);

                        idx = rd.GetOrdinal("DataPedido");
                        if (!rd.IsDBNull(idx))
                            dataPed = rd.GetDateTime(idx);

                        cardCode = GetStringOrNull(rd, "CardCodeVencedor");
                        cardName = GetStringOrNull(rd, "CardNameVencedor");

                        idx = rd.GetOrdinal("ValorVencedor");
                        if (!rd.IsDBNull(idx))
                            valorVend = rd.GetDecimal(idx);

                        moeda = GetStringOrNull(rd, "Moeda");

                        string statusProcesso = oporDocEntry.HasValue ? "Fechado" : "Em Cotacao";

                        int? idProcessoExistente = null;

                        string sqlCheck =
                            "SELECT \"Id\" " +
                            "FROM \"ProcessosCompra\" " +
                            "WHERE \"OPRQDocEntry\" = ?";

                        using (var cmdCheck = new OdbcCommand(sqlCheck, conn))
                        {
                            cmdCheck.Parameters.Add(new OdbcParameter
                            {
                                OdbcType = OdbcType.Int,
                                Value = oprqDocEntry
                            });

                            var result = await cmdCheck.ExecuteScalarAsync();
                            if (result != null && result != DBNull.Value)
                                idProcessoExistente = Convert.ToInt32(result);
                        }

                        if (!idProcessoExistente.HasValue)
                        {
                            string sqlInsert =
                                "INSERT INTO \"ProcessosCompra\" " +
                                "(" +
                                "   \"OPRQDocEntry\", \"OPRQDocNum\", \"DataSolicitacao\", " +
                                "   \"Solicitante\", \"Departamento\", \"EquipeResponsavel\", " +
                                "   \"OPORDocEntry\", \"OPORDocNum\", \"CardCodeVencedor\", \"CardNameVencedor\", " +
                                "   \"ValorVencedor\", \"Moeda\", \"DataPedido\", " +
                                "   \"StatusProcesso\", \"CriadoEm\", \"CriadoPorId\", \"Observacoes\" " +
                                ") VALUES " +
                                "(" +
                                "   ?, ?, ?, " +
                                "   ?, ?, ?, " +
                                "   ?, ?, ?, ?, " +
                                "   ?, ?, ?, " +
                                "   ?, CURRENT_TIMESTAMP, NULL, NULL" +
                                ")";

                            using (var cmdInsert = new OdbcCommand(sqlInsert, conn))
                            {
                                cmdInsert.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = oprqDocEntry });
                                cmdInsert.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = oprqDocNum });
                                cmdInsert.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = dataSol.Date });
                                cmdInsert.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = (object?)solicitante ?? DBNull.Value });
                                cmdInsert.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = (object?)departamento ?? DBNull.Value });
                                cmdInsert.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = equipeResponsavel });

                                cmdInsert.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = (object?)oporDocEntry ?? DBNull.Value });
                                cmdInsert.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = (object?)oporDocNum ?? DBNull.Value });
                                cmdInsert.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = (object?)cardCode ?? DBNull.Value });
                                cmdInsert.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = (object?)cardName ?? DBNull.Value });

                                cmdInsert.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Decimal, Value = (object?)valorVend ?? DBNull.Value });
                                cmdInsert.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = (object?)moeda ?? DBNull.Value });
                                cmdInsert.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = (object?)dataPed ?? DBNull.Value });

                                cmdInsert.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = statusProcesso });

                                await cmdInsert.ExecuteNonQueryAsync();
                            }
                        }
                        else
                        {
                            string sqlUpdate =
                                    "UPDATE \"ProcessosCompra\" SET " +
                                    "   \"Solicitante\"       = ?, " +
                                    "   \"Departamento\"      = ?, " +
                                    "   \"EquipeResponsavel\" = ?, " +   // <-- moveu para 3º, casando com o 3º parâmetro
                                    "   \"OPORDocEntry\"      = ?, " +
                                    "   \"OPORDocNum\"        = ?, " +
                                    "   \"CardCodeVencedor\"  = ?, " +
                                    "   \"CardNameVencedor\"  = ?, " +
                                    "   \"ValorVencedor\"     = ?, " +
                                    "   \"Moeda\"             = ?, " +
                                    "   \"DataPedido\"        = ?, " +
                                    "   \"StatusProcesso\"    = ? " +
                                    "WHERE \"Id\" = ?";

                            using (var cmdUpdate = new OdbcCommand(sqlUpdate, conn))
                            {
                                cmdUpdate.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = (object?)solicitante ?? DBNull.Value });   // 1
                                cmdUpdate.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = (object?)departamento ?? DBNull.Value });  // 2
                                cmdUpdate.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = equipeResponsavel });                      // 3

                                cmdUpdate.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = (object?)oporDocEntry ?? DBNull.Value });    // 4
                                cmdUpdate.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = (object?)oporDocNum ?? DBNull.Value });      // 5
                                cmdUpdate.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = (object?)cardCode ?? DBNull.Value });       // 6
                                cmdUpdate.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = (object?)cardName ?? DBNull.Value });       // 7
                                cmdUpdate.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Decimal, Value = (object?)valorVend ?? DBNull.Value });      // 8
                                cmdUpdate.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = (object?)moeda ?? DBNull.Value });          // 9
                                cmdUpdate.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = (object?)dataPed ?? DBNull.Value });        // 10
                                cmdUpdate.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = statusProcesso });                          // 11

                                cmdUpdate.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = idProcessoExistente.Value });                     // 12

                                await cmdUpdate.ExecuteNonQueryAsync();
                            }
                        }
                    }
                }
            }
        }

        // --------------------------------------------------------------------
        // API pública para detalhes / inclusão de cotações concorrentes
        // --------------------------------------------------------------------

        public async Task<ProcessoCompraDetalhesVM?> ObterDetalhesProcessoAsync(int processoId)
        {
            using (var conn = new OdbcConnection(_connStr))
            {
                await conn.OpenAsync();

                var proc = await CarregarProcessoPorIdAsync(conn, processoId);
                if (proc == null)
                    return null;

                var cotacoes = await CarregarCotacoesDetalhesAsync(conn, processoId);

                var itens = await CarregarItensSolicitacaoAsync(conn, proc.OPRQDocEntry);

                var vm = new ProcessoCompraDetalhesVM
                {
                    Processo = new ProcessoCompraLinhaVM
                    {
                        ProcessoId = proc.ProcessoId,
                        OPRQDocEntry = proc.OPRQDocEntry,
                        OPRQDocNum = proc.OPRQDocNum,
                        DataSolicitacao = proc.DataSolicitacao,
                        Solicitante = proc.Solicitante,
                        Departamento = proc.Departamento,
                        OPORDocEntry = proc.OPORDocEntry,
                        OPORDocNum = proc.OPORDocNum,
                        DataPedido = proc.DataPedido,
                        CardCodeVencedor = proc.CardCodeVencedor,
                        CardNameVencedor = proc.CardNameVencedor,
                        ValorVencedor = proc.ValorVencedor,
                        StatusProcesso = proc.StatusProcesso ?? string.Empty
                    },
                    Cotacoes = cotacoes,
                    ItensSolicitacao = itens
                };

                vm.NovaCotacao.ProcessoCompraId = processoId;

                return vm;
            }
        }

        public async Task AdicionarCotacaoConcorrenteAsync(
            CotacaoConcorrenteLinhaVM novaCotacao,
            int? usuarioId)
        {
            using (var conn = new OdbcConnection(_connStr))
            {
                await conn.OpenAsync();

                string sql =
                    "INSERT INTO \"CotacoesConcorrentesCompra\" " +
                    "(" +
                    "   \"ProcessoCompraId\", " +
                    "   \"CardCodeFornecedor\", \"CardNameFornecedor\", \"ValorCotacaoTotal\", " +
                    "   \"PrazoEntregaDias\", \"CondicaoPagamento\", \"Observacoes\", " +
                    "   \"CriadoEm\", \"CriadoPorId\"" +
                    ") VALUES (" +
                    "   ?, ?, ?, ?, ?, ?, ?, CURRENT_TIMESTAMP, ?" +
                    ")";

                using (var cmd = new OdbcCommand(sql, conn))
                {
                    cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = novaCotacao.ProcessoCompraId });
                    cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = (object?)novaCotacao.CardCodeFornecedor ?? DBNull.Value });
                    cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = (object?)novaCotacao.CardNameFornecedor ?? DBNull.Value });
                    cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Decimal, Value = novaCotacao.ValorCotacaoTotal });

                    cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = (object?)novaCotacao.PrazoEntregaDias ?? DBNull.Value });
                    cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = (object?)novaCotacao.CondicaoPagamento ?? DBNull.Value });
                    cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = (object?)novaCotacao.Observacoes ?? DBNull.Value });

                    cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = (object?)usuarioId ?? DBNull.Value });

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        private async Task<List<ItemSolicitacaoVM>> CarregarItensSolicitacaoAsync(OdbcConnection conn, int oprqDocEntry)
        {
            var lista = new List<ItemSolicitacaoVM>();

            // PRQ1: linhas da solicitação
            // OPRQ: cabeçalho (para pegar Comments)
            string sql =
                "SELECT " +
                "   L.\"Dscription\"       AS \"DescricaoItem\", " +         // descrição da linha
                "   L.\"U_TX_DescMatNfse\" AS \"DescNFSe\", " +              // UDF da linha
                "   O.\"Comments\"         AS \"Comentarios\" " +            // comentário do cabeçalho
                "FROM \"PRQ1\" L " +
                "INNER JOIN \"OPRQ\" O ON O.\"DocEntry\" = L.\"DocEntry\" " +
                "WHERE L.\"DocEntry\" = ? " +
                "ORDER BY L.\"LineNum\"";

            using (var cmd = new OdbcCommand(sql, conn))
            {
                cmd.Parameters.Add(new OdbcParameter
                {
                    OdbcType = OdbcType.Int,
                    Value = oprqDocEntry
                });

                using (var rd = await cmd.ExecuteReaderAsync())
                {
                    while (await rd.ReadAsync())
                    {
                        var item = new ItemSolicitacaoVM
                        {
                            Description = GetStringOrNull(rd, "DescricaoItem"),
                            DescricaoNFSe = GetStringOrNull(rd, "DescNFSe"),
                            Comentarios = GetStringOrNull(rd, "Comentarios")
                        };

                        lista.Add(item);
                    }
                }
            }

            return lista;
        }
    }
}
