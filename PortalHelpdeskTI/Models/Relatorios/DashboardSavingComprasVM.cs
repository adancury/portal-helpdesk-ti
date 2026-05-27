using System;
using System.Collections.Generic;

namespace PortalHelpdeskTI.Models.Relatorios
{
    public class ProcessoCompraLinhaVM
    {
        public int ProcessoId { get; set; }

        // Solicitação de Compra (OPRQ)
        public int OPRQDocEntry { get; set; }
        public int OPRQDocNum { get; set; }
        public DateTime DataSolicitacao { get; set; }
        public string? Solicitante { get; set; }
        public string? Departamento { get; set; }

        // Pedido de Compra vencedor (OPOR)
        public int? OPORDocEntry { get; set; }
        public int? OPORDocNum { get; set; }
        public DateTime? DataPedido { get; set; }
        public string? CardCodeVencedor { get; set; }
        public string? CardNameVencedor { get; set; }
        public decimal? ValorVencedor { get; set; }

        // Saving
        public decimal? ValorBaseConcorrentes { get; set; }
        public decimal? SavingValor { get; set; }
        public decimal? SavingPercentual { get; set; }
        public int QtdConcorrentes { get; set; }
        public string StatusProcesso { get; set; } = string.Empty;
        public decimal? SavingProcessoValor { get; set; }      // R$ (mais cara - mais barata)
        public decimal? MenorCotacao { get; set; }             // opcional (se quiser exibir)
        public decimal? MaiorCotacao { get; set; }             // opcional (se quiser exibir)

    }

    public class DashboardSavingComprasVM
    {
        // Filtros
        public DateTime DataDe { get; set; }
        public DateTime DataAte { get; set; }
        public string[] DepartamentosSelecionados { get; set; } = Array.Empty<string>();

        // Busca global (pedido / fornecedor)
        public string? TermoBusca { get; set; }

        // Cards
        public decimal TotalComprasPeriodo { get; set; }
        public decimal TotalSavingPeriodo { get; set; }
        public decimal SavingMedioPercentual { get; set; }
        public int QtdProcessos { get; set; }
        public int QtdFornecedoresEnvolvidos { get; set; }

        // NOVO: processos sem cotação (fornecedor definido)
        public int QtdProcessosSemCotacao { get; set; }
        public decimal PercentualProcessosSemCotacao { get; set; }

        // Paginação
        public int PaginaAtual { get; set; } = 1;
        public int TamanhoPagina { get; set; } = 30; // fixo
        public int TotalRegistros { get; set; }
        public int TotalPaginas { get; set; }
        public int QtdProcessosEmCotacao { get; set; }
        public int QtdProcessosFechados { get; set; }
        public string EquipeResponsavelSelecionada { get; set; } = "Todos";
        // Valores: "Todos" | "Compras" | "Importacao"

        public decimal PercentualEmCotacao { get; set; }
        public decimal PercentualFechados { get; set; }

        // Grid
        public List<ProcessoCompraLinhaVM> Linhas { get; set; } = new();
        public List<TopDepartamentoGastoVM> TopDepartamentosGasto { get; set; } = new();
        public List<TopDepartamentoSavingVM> TopDepartamentosSaving { get; set; } = new();

    }
    public class TopDepartamentoSavingVM
    {
        public string Departamento { get; set; } = string.Empty;
        public decimal TotalSaving { get; set; }
        public decimal PercentualSobreSaving { get; set; }
    }

    public class TopDepartamentoGastoVM
    {
        public string Departamento { get; set; } = string.Empty;
        public decimal TotalGasto { get; set; }
        public decimal PercentualSobreTotal { get; set; }
    }

}
