using PortalHelpdeskTI.Models;
using PortalHelpdeskTI.Services.SAP;

namespace PortalHelpdeskTI.Services.Aprovacoes
{
    using System.Linq;
    using System.Threading.Tasks;
    using PortalHelpdeskTI.Models;
    using PortalHelpdeskTI.Services.SAP;

    public class ApprovalService : IApprovalService
    {
        private readonly ServiceLayerClient _sl;
        public ApprovalService(ServiceLayerClient sl) { _sl = sl; }

        public async Task<ApprovalDetailsDto?> GetDetailsAsync(int id)
        {
            var raw = await _sl.GetApprovalDetailsAsync(id);
            if (raw == null) return null;

            return new ApprovalDetailsDto
            {
                DocumentType = raw.DocumentType,
                DocumentNumber = raw.DocumentNumber,
                RequesterName = raw.RequesterName,
                CreatedAt = raw.CreatedAt,
                Total = raw.Total,
                Status = raw.Status,
                Items = (raw.Items ?? new()).Select(i => new ApprovalItemDto
                {
                    ItemCode = i.ItemCode,
                    ItemName = i.ItemName,
                    Quantity = i.Quantity,
                    Price = i.Price
                }).ToList(),
                PreviousComments = (raw.Comments ?? new()).Select(c => new ApprovalCommentDto
                {
                    Author = c.Author,
                    Date = c.Date,
                    Text = c.Text
                }).ToList()
            };
        }
    }
}

