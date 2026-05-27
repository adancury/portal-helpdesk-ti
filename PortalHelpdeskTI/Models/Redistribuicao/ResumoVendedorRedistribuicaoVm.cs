namespace PortalHelpdeskTI.ViewModels.Redistribuicao;

public class ResumoVendedorRedistribuicaoVm
{
    public int SlpCode { get; set; }
    public string SlpName { get; set; } = "";
    public string Email { get; set; } = "";
    public int QtdClientes { get; set; }
    public decimal SomaKpiTkm { get; set; }
    public int QtdUFs { get; set; }
}
