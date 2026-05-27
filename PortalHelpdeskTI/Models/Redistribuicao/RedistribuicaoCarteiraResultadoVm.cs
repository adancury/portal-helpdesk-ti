namespace PortalHelpdeskTI.ViewModels.Redistribuicao;

public class RedistribuicaoCarteiraResultadoVm
{
    public RedistribuicaoCarteiraFiltroVm Filtros { get; set; } = new();

    public List<ClienteRedistribuicaoVm> Pendentes { get; set; } = new();
    public List<ClienteRedistribuicaoVm> Elegiveis { get; set; } = new();

    // PASSO 2
    public List<VendedorVm> Vendedores { get; set; } = new();
    public List<ClienteRedistribuicaoSimuladoVm> Simulacao { get; set; } = new();
    public List<ResumoVendedorRedistribuicaoVm> Resumo { get; set; } = new();

    public int TotalPendentes => Pendentes.Count;
    public int TotalElegiveis => Elegiveis.Count;
    public int TotalSimulados => Simulacao.Count;
}
