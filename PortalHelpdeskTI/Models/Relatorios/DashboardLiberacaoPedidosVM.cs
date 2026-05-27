// Models/Relatorios/DashboardLiberacaoPedidosVM.cs
using PortalHelpdeskTI.Models.Relatorios.PortalHelpdeskTI.Models.Relatorios;

namespace PortalHelpdeskTI.Models.Relatorios
{
    public class PedidoLiberacaoLinhaVM
    {
        public int DocEntry { get; set; }
        public int DocNum { get; set; }
        public string CardCode { get; set; } = "";
        public string CardName { get; set; } = "";
        public DateTime DocDate { get; set; }
        public decimal DocTotal { get; set; }

        public string PenComAtual { get; set; } = "";
        public string PenFinAtual { get; set; } = "";
        public string IndicadorAtual { get; set; } = "";
        public string DocStatus { get; set; } = "";

        public string StatusDocumento { get; set; } = "";  // "Aberto" / "Fechado"

        public DateTime? DataLiberacaoComercial { get; set; }
        public DateTime? DataLiberacaoFinanceiro { get; set; }

        public bool TeveBloqueioComercial { get; set; }
        public bool TeveBloqueioFinanceiro { get; set; }

        public double SLAHorasTotal { get; set; }
        public double HorasDecorridas { get; set; }
        public double PercentualSlaConsumido { get; set; }

        public string StatusFinal { get; set; } = "";   // LIBERADO, RECUSADO, PENDENTE
        public bool DentroDoSla { get; set; }
        public bool Atrasado { get; set; }
        public string TipoBloqueio { get; set; } = "";
        public double HorasComercial { get; set; }
        public double HorasFinanceiro { get; set; }

        public decimal? ValorDevolvido { get; set; }
        public string Aprovador { get; set; } = "";
        public string Canceled { get; set; } = "";

        // >>> usado no resumo por usuário (rejeitados)
        public string UsuarioCancelou { get; set; } = "";

        public DateTime? DataFaturamento { get; set; }
        public DateTime? DataEntrega { get; set; }
        public string ComentariosDevolucao { get; set; } = ""; // ORRR.Comments (somente quando StatusDocumento = Devolvido)
        public string SerialNF { get; set; } = "";
        public DateTime? DataDevolucao { get; set; }
    }

    public class DashboardLiberacaoPedidosVM
    {
        public DateTime DataDe { get; set; }
        public DateTime DataAte { get; set; }

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

        public decimal VarTotalPedidosMesAnteriorPct { get; set; }
        public decimal VarTotalPedidosAnoAnteriorPct { get; set; }
        public decimal VarLiberadosDentroSlaMesAnteriorPct { get; set; }
        public decimal VarLiberadosDentroSlaAnoAnteriorPct { get; set; }
        public decimal VarLiberadosComAtrasoMesAnteriorPct { get; set; }
        public decimal VarLiberadosComAtrasoAnoAnteriorPct { get; set; }
        public decimal VarSemBloqueioMesAnteriorPct { get; set; }
        public decimal VarSemBloqueioAnoAnteriorPct { get; set; }
        public decimal VarPendentesMesAnteriorPct { get; set; }
        public decimal VarPendentesAnoAnteriorPct { get; set; }
        public decimal VarPendentesEmAtrasoMesAnteriorPct { get; set; }
        public decimal VarPendentesEmAtrasoAnoAnteriorPct { get; set; }
        public decimal VarRecusadosMesAnteriorPct { get; set; }
        public decimal VarRecusadosAnoAnteriorPct { get; set; }

        public string[] DepartamentosSelecionados { get; set; } = Array.Empty<string>();
        public int QtdeDevolvidos { get; set; }
        public decimal ValorDevolvidos { get; set; }
        public decimal VarDevolvidosMesAnteriorPct { get; set; }
        public decimal VarDevolvidosAnoAnteriorPct { get; set; }
        public decimal? ValorDevolvido { get; set; }
        public string TipoDataBase { get; set; } = "Pedido";
        public List<PedidoLiberacaoLinhaVM> Pedidos { get; set; } = new();
        public List<ResumoLiberacaoUsuarioVM> ResumoPorUsuario { get; set; } = new();

    }
}
