// Models/ApprovalDetailsDto.cs
using System;
using System.Collections.Generic;

namespace PortalHelpdeskTI.Models
{
    public class ApprovalDetailsDto
    {
        public string? DocumentType { get; set; }
        public string? DocumentNumber { get; set; }
        public string? RequesterName { get; set; }
        public DateTime CreatedAt { get; set; }
        public decimal Total { get; set; }
        public string? Status { get; set; }

        public List<ApprovalItemDto> Items { get; set; } = new();
        public List<ApprovalCommentDto> PreviousComments { get; set; } = new();
    }

    public class ApprovalItemDto
    {
        public string? ItemCode { get; set; }
        public string? ItemName { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
    }

    public class ApprovalCommentDto
    {
        public string? Author { get; set; }
        public DateTime Date { get; set; }
        public string? Text { get; set; }
    }
}
