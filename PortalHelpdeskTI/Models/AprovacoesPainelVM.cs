using System.Collections.Generic;
using static PortalHelpdeskTI.Services.SAP.ServiceLayerClient;
namespace PortalHelpdeskTI.Models
{
    public class AprovacoesPainelVM
    {
        // Lista de aprovações da página atual
        public List<PortalHelpdeskTI.Services.SAP.ServiceLayerClient.ApprovalRequestDto> Itens { get; set; }
            = new();

        // Dados da paginação
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public int Total { get; set; } = 0;
    }
}
