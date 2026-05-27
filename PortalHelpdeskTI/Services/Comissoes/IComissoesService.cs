using PortalHelpdeskTI.Models.Comissoes;

namespace PortalHelpdeskTI.Services.Comissoes
{
    public interface IComissoesService
    {
        Task<RelatorioComissaoVm> GerarRelatorioAsync(int slpCode, DateTime ini, DateTime fim, CancellationToken ct);
        Task<ResumoComissaoVm> GerarResumoAsync(int ano, int mes, CancellationToken ct);
        Task<List<OsLpRow>> BuscarVendedoresSapAsync(CancellationToken ct);

    }
}
