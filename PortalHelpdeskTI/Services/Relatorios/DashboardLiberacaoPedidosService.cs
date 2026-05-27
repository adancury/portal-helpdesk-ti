using PortalHelpdeskTI.Models;
using PortalHelpdeskTI.Models.Relatorios;
using PortalHelpdeskTI.Models.Relatorios.PortalHelpdeskTI.Models.Relatorios;
using System.Data.Common;
using System.Data.Odbc;

namespace PortalHelpdeskTI.Services.Relatorios
{
    public class DashboardLiberacaoPedidosService
    {
        private readonly string _connStr;
        private class ResumoPeriodo
        {
            public int QtdeTotalPedidos { get; set; }

            public int QtdeLiberadosDentroSla { get; set; }
            public int QtdeLiberadosComAtraso { get; set; }
            public int QtdePendentesEmAtraso { get; set; }
            public int QtdeRecusados { get; set; }
            public int QtdePendentes { get; set; }
            public int QtdeSemBloqueio { get; set; }

            public decimal ValorTotalPedidos { get; set; }
            public decimal ValorSemBloqueio { get; set; }
            public decimal ValorLiberadosDentroSla { get; set; }
            public decimal ValorLiberadosComAtraso { get; set; }
            public decimal ValorPendentesEmAtraso { get; set; }
            public decimal ValorPendentes { get; set; }
            public decimal ValorRecusados { get; set; }
            public int QtdeDevolvidos { get; set; }
            public decimal ValorDevolvidos { get; set; }

        }

        private decimal CalcularVariacaoPct(int atual, int anterior)
        {
            if (anterior == 0)
                return 0m;

            return Math.Round(((atual - anterior) / (decimal)anterior) * 100m, 1);
        }

        public DashboardLiberacaoPedidosService(IConfiguration cfg)
        {
            _connStr = cfg.GetConnectionString("HanaConn");
        }

        private async Task<ResumoPeriodo> GerarResumoPeriodoAsync(
            DateTime dataDe,
            DateTime dataAte,
            string[] filtrosDepto,
            bool carregarPedidos,
            DashboardLiberacaoPedidosVM vm,
            string tipoDataBase,
            Dictionary<string, ResumoUsuarioAcumulado>? mapaUsuarios = null)
        {
            var resumo = new ResumoPeriodo();

            using var cn = new OdbcConnection(_connStr);
            await cn.OpenAsync();

            using var cmd = cn.CreateCommand();
            cmd.CommandText = "CALL \"SBO_BRW_PRD\".\"Portal_DashLiberacaoPedidos\"(?, ?, ?)";
            cmd.Parameters.AddWithValue("@P_DataDe", dataDe);
            cmd.Parameters.AddWithValue("@P_DataAte", dataAte);
            cmd.Parameters.AddWithValue("@P_TipoData", tipoDataBase);

            using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var linha = MapearLinha(rd);
                ClassificarSla(linha);

                // ---------- Aplica filtro por departamento ----------
                bool incluir;
                if (filtrosDepto == null || filtrosDepto.Length == 0)
                {
                    incluir = true; // nenhum filtro -> todos
                }
                else
                {
                    bool selCom = filtrosDepto.Contains("COMERCIAL");
                    bool selFin = filtrosDepto.Contains("FINANCEIRO");
                    bool selSemBloq = filtrosDepto.Contains("SEM_BLOQUEIO");

                    bool teveCom = linha.TeveBloqueioComercial;
                    bool teveFin = linha.TeveBloqueioFinanceiro;
                    bool semBloqueio = !teveCom && !teveFin;

                    incluir =
                        (selCom && teveCom) ||
                        (selFin && teveFin) ||
                        (selSemBloq && semBloqueio);
                }

                if (!incluir)
                    continue;

                // Se for o período "principal", preenche a lista de pedidos
                if (carregarPedidos)
                {
                    vm.Pedidos.Add(linha);
                }

                // 🔹 Valor do pedido
                // 🔹 Valor do pedido (DocTotal do ORDR)
                var valorPedido = linha.DocTotal;
                resumo.ValorTotalPedidos += valorPedido;


                // ---------- Regras de contagem dos cards ----------
                bool teveBloqueio = linha.TeveBloqueioComercial || linha.TeveBloqueioFinanceiro;
                bool semBloqueioHistorico = !teveBloqueio;
                bool slaEstourado = linha.SLAHorasTotal > 0 && linha.HorasDecorridas > linha.SLAHorasTotal;

                // Contagem total
                resumo.QtdeTotalPedidos++;

                // Sem bloqueio
                if (semBloqueioHistorico)
                {
                    resumo.QtdeSemBloqueio++;
                    resumo.ValorSemBloqueio += valorPedido;
                }

                // Liberado dentro do SLA
                if (linha.StatusFinal == "LIBERADO" && linha.DentroDoSla && teveBloqueio)
                {
                    resumo.QtdeLiberadosDentroSla++;
                    resumo.ValorLiberadosDentroSla += valorPedido;
                    if (mapaUsuarios != null)
                        IncrementarUsuario(mapaUsuarios, linha.Aprovador, aprovados: 1);
                }

                // Liberados com atraso
                if (linha.StatusFinal == "LIBERADO" && linha.Atrasado && teveBloqueio && linha.SLAHorasTotal > 0)
                {
                    resumo.QtdeLiberadosComAtraso++;
                    resumo.ValorLiberadosComAtraso += valorPedido;
                    if (mapaUsuarios != null)
                        IncrementarUsuario(mapaUsuarios, linha.Aprovador, aprovados: 1);
                }

                // Pendentes em atraso
                if (linha.StatusFinal == "PENDENTE" && teveBloqueio && slaEstourado)
                {
                    resumo.QtdePendentesEmAtraso++;
                    resumo.ValorPendentesEmAtraso += valorPedido;
                }

                // Recusados
                if (linha.StatusFinal == "RECUSADO" && teveBloqueio)
                {
                    resumo.QtdeRecusados++;
                    resumo.ValorRecusados += valorPedido;
                    if (mapaUsuarios != null)
                    {
                        string usuario = !string.IsNullOrWhiteSpace(linha.Aprovador)
                            ? linha.Aprovador
                            : linha.UsuarioCancelou;

                        IncrementarUsuario(mapaUsuarios, usuario, rejeitados: 1);
                    }
                }

                // Devolvidos (novo card) – usa o total da Dev.NF (ORIN.DocTotal)
                if (linha.StatusDocumento == "Devolvido")
                {
                    resumo.QtdeDevolvidos++;

                    // Se por algum motivo vier null da procedure, cai pra 0 pra não estourar
                    var valorDev = linha.ValorDevolvido ?? 0m;
                    resumo.ValorDevolvidos += valorDev;
                }


                // Pendentes (com bloqueio)
                if (linha.StatusFinal == "PENDENTE" && teveBloqueio)
                {
                    resumo.QtdePendentes++;
                    resumo.ValorPendentes += valorPedido;
                }
            }

            return resumo;
        }

        public async Task<DashboardLiberacaoPedidosVM> BuscarAsync(
            DateTime? de = null,
            DateTime? ate = null,
            string tipos = null,
            string tipoDataBase = "Pedido")
        {
            var hoje = DateTime.Today;
            var dataDe = de ?? new DateTime(hoje.Year, hoje.Month, 1);
            var dataAte = ate ?? dataDe.AddMonths(1).AddDays(-1);

            // Normaliza o tipo de data
            if (tipoDataBase != "Pedido" &&
                tipoDataBase != "Faturamento" &&
                tipoDataBase != "Entrega")
            {
                tipoDataBase = "Pedido";
            }

            // Mês anterior (mesma janela)
            var dataDeMesAnterior = dataDe.AddMonths(-1);
            var dataAteMesAnterior = dataAte.AddMonths(-1);

            // Mesmo período do ano anterior
            var dataDeAnoAnterior = dataDe.AddYears(-1);
            var dataAteAnoAnterior = dataAte.AddYears(-1);

            var vm = new DashboardLiberacaoPedidosVM
            {
                DataDe = dataDe,
                DataAte = dataAte,
                TipoDataBase = tipoDataBase
            };

            // Normaliza filtros de departamento
            var filtrosDepto = (tipos ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().ToUpperInvariant())
                .Distinct()
                .ToArray();

            vm.DepartamentosSelecionados = filtrosDepto;
            var mapaUsuarios = new Dictionary<string, ResumoUsuarioAcumulado>(StringComparer.OrdinalIgnoreCase);


            // ---------- Período atual (preenche lista + resumo) ----------
            var resumoAtual = await GerarResumoPeriodoAsync(
                dataDe,
                dataAte,
                filtrosDepto,
                carregarPedidos: true,
                vm: vm,
                tipoDataBase: tipoDataBase,
                mapaUsuarios: mapaUsuarios);

            // ---------- Mês anterior ----------
            var resumoMesAnterior = await GerarResumoPeriodoAsync(
                dataDeMesAnterior,
                dataAteMesAnterior,
                filtrosDepto,
                carregarPedidos: false,
                vm: vm,
                tipoDataBase: tipoDataBase);

            // ---------- Mesmo período do ano anterior ----------
            var resumoAnoAnterior = await GerarResumoPeriodoAsync(
                dataDeAnoAnterior,
                dataAteAnoAnterior,
                filtrosDepto,
                carregarPedidos: false,
                vm: vm,
                tipoDataBase: tipoDataBase);

            // Copia os números atuais para o VM
            vm.QtdeTotalPedidos = resumoAtual.QtdeTotalPedidos;
            vm.QtdeSemBloqueio = resumoAtual.QtdeSemBloqueio;
            vm.QtdeLiberadosDentroSla = resumoAtual.QtdeLiberadosDentroSla;
            vm.QtdeLiberadosComAtraso = resumoAtual.QtdeLiberadosComAtraso;
            vm.QtdePendentesEmAtraso = resumoAtual.QtdePendentesEmAtraso;
            vm.QtdePendentes = resumoAtual.QtdePendentes;
            vm.QtdeRecusados = resumoAtual.QtdeRecusados;

            vm.ValorTotalPedidos = resumoAtual.ValorTotalPedidos;
            vm.ValorSemBloqueio = resumoAtual.ValorSemBloqueio;
            vm.ValorLiberadosDentroSla = resumoAtual.ValorLiberadosDentroSla;
            vm.ValorLiberadosComAtraso = resumoAtual.ValorLiberadosComAtraso;
            vm.ValorPendentesEmAtraso = resumoAtual.ValorPendentesEmAtraso;
            vm.ValorPendentes = resumoAtual.ValorPendentes;
            vm.ValorRecusados = resumoAtual.ValorRecusados;
            vm.QtdeDevolvidos = resumoAtual.QtdeDevolvidos;
            vm.ValorDevolvidos = resumoAtual.ValorDevolvidos;


            // ---------- Calcula as variações em % (sobre quantidade) ----------

            // Total de pedidos
            vm.VarTotalPedidosMesAnteriorPct =
                CalcularVariacaoPct(resumoAtual.QtdeTotalPedidos, resumoMesAnterior.QtdeTotalPedidos);
            vm.VarTotalPedidosAnoAnteriorPct =
                CalcularVariacaoPct(resumoAtual.QtdeTotalPedidos, resumoAnoAnterior.QtdeTotalPedidos);

            // Liberado dentro do SLA
            vm.VarLiberadosDentroSlaMesAnteriorPct =
                CalcularVariacaoPct(resumoAtual.QtdeLiberadosDentroSla, resumoMesAnterior.QtdeLiberadosDentroSla);
            vm.VarLiberadosDentroSlaAnoAnteriorPct =
                CalcularVariacaoPct(resumoAtual.QtdeLiberadosDentroSla, resumoAnoAnterior.QtdeLiberadosDentroSla);

            // Liberados com atraso
            vm.VarLiberadosComAtrasoMesAnteriorPct =
                CalcularVariacaoPct(resumoAtual.QtdeLiberadosComAtraso, resumoMesAnterior.QtdeLiberadosComAtraso);
            vm.VarLiberadosComAtrasoAnoAnteriorPct =
                CalcularVariacaoPct(resumoAtual.QtdeLiberadosComAtraso, resumoAnoAnterior.QtdeLiberadosComAtraso);

            // Sem bloqueio
            vm.VarSemBloqueioMesAnteriorPct =
                CalcularVariacaoPct(resumoAtual.QtdeSemBloqueio, resumoMesAnterior.QtdeSemBloqueio);
            vm.VarSemBloqueioAnoAnteriorPct =
                CalcularVariacaoPct(resumoAtual.QtdeSemBloqueio, resumoAnoAnterior.QtdeSemBloqueio);

            // Pendentes total
            vm.VarPendentesMesAnteriorPct =
                CalcularVariacaoPct(resumoAtual.QtdePendentes, resumoMesAnterior.QtdePendentes);
            vm.VarPendentesAnoAnteriorPct =
                CalcularVariacaoPct(resumoAtual.QtdePendentes, resumoAnoAnterior.QtdePendentes);

            // Pendentes em atraso
            vm.VarPendentesEmAtrasoMesAnteriorPct =
                CalcularVariacaoPct(resumoAtual.QtdePendentesEmAtraso, resumoMesAnterior.QtdePendentesEmAtraso);
            vm.VarPendentesEmAtrasoAnoAnteriorPct =
                CalcularVariacaoPct(resumoAtual.QtdePendentesEmAtraso, resumoAnoAnterior.QtdePendentesEmAtraso);

            // Recusados
            vm.VarRecusadosMesAnteriorPct =
                CalcularVariacaoPct(resumoAtual.QtdeRecusados, resumoMesAnterior.QtdeRecusados);
            vm.VarRecusadosAnoAnteriorPct =
                CalcularVariacaoPct(resumoAtual.QtdeRecusados, resumoAnoAnterior.QtdeRecusados);

            // Devolvidos
            vm.QtdeDevolvidos = resumoAtual.QtdeDevolvidos;
            vm.ValorDevolvidos = resumoAtual.ValorDevolvidos;

            vm.VarDevolvidosMesAnteriorPct =
                CalcularVariacaoPct(resumoAtual.QtdeDevolvidos, resumoMesAnterior.QtdeDevolvidos);
            vm.VarDevolvidosAnoAnteriorPct =
                CalcularVariacaoPct(resumoAtual.QtdeDevolvidos, resumoAnoAnterior.QtdeDevolvidos);

            // ---------- RESUMO POR USUÁRIO (APROVADOS x REJEITADOS) ----------
            // Rejeitado: StatusDocumento = 'Cancelado'  (mesma lógica dos cards).
            // Aprovado: qualquer pedido com Aprovador preenchido que NÃO esteja como Cancelado.
            vm.ResumoPorUsuario = mapaUsuarios
                .Select(kvp => new ResumoLiberacaoUsuarioVM
                {
                    Usuario = kvp.Key,
                    QtdeLiberado = kvp.Value.Aprovados,
                    QtdeRejeitado = kvp.Value.Rejeitados
                })
                .OrderByDescending(r => r.Total)
                .ToList();

            return vm;
        }
        private class ResumoUsuarioAcumulado
        {
            public int Aprovados { get; set; }
            public int Rejeitados { get; set; }
        }
        private static void IncrementarUsuario(Dictionary<string, ResumoUsuarioAcumulado> mapa,
            string aprovador, int aprovados = 0, int rejeitados = 0)
        {
            // Se ainda assim vier vazio, marca como "Usuário não identificado" ou similar.
            if (string.IsNullOrWhiteSpace(aprovador))
                aprovador = "Usuário não identificado";

            if (!mapa.TryGetValue(aprovador, out var acc))
            {
                acc = new ResumoUsuarioAcumulado();
                mapa[aprovador] = acc;
            }

            acc.Aprovados += aprovados;
            acc.Rejeitados += rejeitados;
        }

        private PedidoLiberacaoLinhaVM MapearLinha(DbDataReader rd)
        {
            DateTime? GetDate(string col)
            {
                try
                {
                    var obj = rd[col];
                    return obj == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(obj);
                }
                catch (IndexOutOfRangeException)
                {
                    return null;
                }
            }

            string GetStr(string col)
            {
                try
                {
                    var obj = rd[col];
                    return obj == DBNull.Value ? "" : obj.ToString() ?? "";
                }
                catch (IndexOutOfRangeException)
                {
                    // Se a coluna não existir (ex.: "Aprovador"), não quebra mais
                    return "";
                }
            }

            decimal? GetDecNullable(string col)
            {
                try
                {
                    var obj = rd[col];
                    return obj == DBNull.Value ? (decimal?)null : Convert.ToDecimal(obj);
                }
                catch (IndexOutOfRangeException)
                {
                    return null;
                }
            }

            var docStatus = GetStr("DocStatus");

            // Monta o "Aprovador" a partir das colunas retornadas pela procedure
            var aprovFin = GetStr("AprovadorFinanceiro");
            var aprovCom = GetStr("AprovadorComercial");
            var aprovador = !string.IsNullOrWhiteSpace(aprovFin) ? aprovFin : aprovCom;

            return new PedidoLiberacaoLinhaVM
            {
                DocEntry = Convert.ToInt32(rd["DocEntry"]),
                DocNum = Convert.ToInt32(rd["DocNum"]),
                CardCode = GetStr("CardCode"),
                CardName = GetStr("CardName"),
                DocDate = Convert.ToDateTime(rd["DocDate"]),
                DocTotal = Convert.ToDecimal(rd["DocTotal"]),

                ValorDevolvido = GetDecNullable("ValorDevolvido"),
                DataFaturamento = GetDate("DataFaturamento"),
                DataEntrega = GetDate("DataEntrega"),
                DataDevolucao = GetDate("DataDevolucao"),

                PenComAtual = GetStr("PenComAtual"),
                PenFinAtual = GetStr("PenFinAtual"),
                IndicadorAtual = GetStr("IndicadorAtual"),

                DocStatus = docStatus,
                StatusDocumento = GetStr("StatusDocumento"),

                DataLiberacaoComercial = GetDate("DataLiberacaoComercial"),
                DataLiberacaoFinanceiro = GetDate("DataLiberacaoFinanceiro"),
                TipoBloqueio = GetStr("TipoBloqueio"),

                // Aprovador montado (financeiro > comercial)
                Aprovador = aprovador,

                // Nome do usuário que cancelou (se a coluna existir)
                UsuarioCancelou = GetStr("UsuarioCancelou"),

                // Vem da ORDR
                Canceled = GetStr("CANCELED"),
                ComentariosDevolucao = GetStr("ComentariosDevolucao"),
                SerialNF = GetStr("SerialNF")
            };
        }


        private void ClassificarSla(PedidoLiberacaoLinhaVM p)
        {
            // ---------- Teve bloqueio histórico? ----------
            // Se hoje está Y OU se em algum momento teve liberação (DataLiberacao... != null)
            p.TeveBloqueioComercial =
                p.PenComAtual == "Y" || p.DataLiberacaoComercial.HasValue;

            p.TeveBloqueioFinanceiro =
                p.PenFinAtual == "Y" || p.DataLiberacaoFinanceiro.HasValue;

            // SLA total em horas, considerando bloqueio histórico
            double sla = 0;
            if (p.TeveBloqueioComercial) sla += 24;
            if (p.TeveBloqueioFinanceiro) sla += 48;
            p.SLAHorasTotal = sla;

            // ---------- StatusFinal (ajustado) ----------
            // Teve algum bloqueio em algum momento?
            bool teveBloqueio = p.TeveBloqueioComercial || p.TeveBloqueioFinanceiro;

            // Situação atual de bloqueio (vindo da procedure: "Comercial", "Financeiro",
            // "Comercial e Financeiro", "Sem bloqueio")
            bool semBloqueioAtual = string.Equals(p.TipoBloqueio, "Sem bloqueio", StringComparison.OrdinalIgnoreCase);

            // Documento fechado? (Situação = Fechado)
            bool docFechado = string.Equals(p.DocStatus, "C", StringComparison.OrdinalIgnoreCase);

            // Indicador de recusado
            bool isRecusado =
                !string.IsNullOrWhiteSpace(p.IndicadorAtual) &&
                (p.IndicadorAtual.Contains("RECUS", StringComparison.OrdinalIgnoreCase) ||
                 p.IndicadorAtual.Contains("NEGAD", StringComparison.OrdinalIgnoreCase));

            // 1) Se indicador marcar recusado, OU se o pedido foi fechado ainda com bloqueio ativo,
            //    considerar RECUSADO.
            if (isRecusado || (docFechado && teveBloqueio && !semBloqueioAtual))
            {
                // Sempre manda para RECUSADO se o indicador diz isso
                // ou se fechou com bloqueio
                p.StatusFinal = "RECUSADO";
            }
            // 2) Tinha bloqueio e hoje está sem bloqueio -> APROVADO / LIBERADO
            else if (teveBloqueio && semBloqueioAtual)
            {
                p.StatusFinal = "LIBERADO";
            }
            // 3) Tem bloqueio ativo e ainda não fechou -> pendente de aprovação
            else if (teveBloqueio && !semBloqueioAtual)
            {
                p.StatusFinal = "PENDENTE";
            }
            // 4) Nunca teve bloqueio: usa DocStatus só para diferenciar aberto/fechado
            else
            {
                p.StatusFinal = docFechado ? "LIBERADO" : "PENDENTE";
            }

            // ---------- Sem SLA (nunca teve bloqueio) ----------
            if (sla <= 0)
            {
                p.HorasDecorridas = 0;
                p.PercentualSlaConsumido = 0;
                // Se nunca teve bloqueio, considerar "dentro do SLA" se estiver liberado
                p.DentroDoSla = (p.StatusFinal == "LIBERADO");
                p.Atrasado = false;
                return;
            }

            // ---------- Cálculo de horas por tipo de bloqueio ----------
            // Regra: se já liberou, o SLA para na data de liberação.
            // Se ainda está pendente, o SLA vai até agora.
            var agora = DateTime.Now;

            // COMERCIAL
            double horasCom = 0;
            if (p.TeveBloqueioComercial)
            {
                var inicioCom = p.DocDate; // ou DataInicioBloqueioComercial se você tiver
                var fimCom = p.DataLiberacaoComercial ?? agora;
                horasCom = (fimCom - inicioCom).TotalHours;
                if (horasCom < 0) horasCom = 0;
            }

            // FINANCEIRO
            double horasFin = 0;
            if (p.TeveBloqueioFinanceiro)
            {
                var inicioFin = p.DocDate; // ou DataInicioBloqueioFinanceiro, se existir
                var fimFin = p.DataLiberacaoFinanceiro ?? agora;
                horasFin = (fimFin - inicioFin).TotalHours;
                if (horasFin < 0) horasFin = 0;
            }

            // Guarda nos campos específicos
            p.HorasComercial = horasCom;
            p.HorasFinanceiro = horasFin;

            // Horas "gerais" para ordenação / cards (pior dos bloqueios ativos)
            double horas;
            if (p.TeveBloqueioComercial && p.TeveBloqueioFinanceiro)
                horas = Math.Max(horasCom, horasFin);
            else if (p.TeveBloqueioComercial)
                horas = horasCom;
            else if (p.TeveBloqueioFinanceiro)
                horas = horasFin;
            else
                horas = 0;

            p.HorasDecorridas = horas;

            var perc = sla > 0 ? (horas / sla) * 100.0 : 0;
            if (perc < 0) perc = 0;
            p.PercentualSlaConsumido = perc;

            // Dentro/fora do SLA só faz sentido para quem teve bloqueio
            p.DentroDoSla = (p.StatusFinal == "LIBERADO" && horas <= sla);
            p.Atrasado = (p.StatusFinal == "LIBERADO" && horas > sla);
        }

    }
}
