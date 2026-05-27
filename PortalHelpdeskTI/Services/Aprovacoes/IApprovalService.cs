namespace PortalHelpdeskTI.Services.Aprovacoes
{
    using System.Threading.Tasks;
    using PortalHelpdeskTI.Models;

    public interface IApprovalService
    {
        Task<ApprovalDetailsDto?> GetDetailsAsync(int approvalRequestId);
    }
}
