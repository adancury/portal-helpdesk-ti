using System;
using System.Collections.Generic;

namespace PortalHelpdeskTI.Services.Relatorios.Progress
{
    public class BatchProgressState
    {
        public string JobId { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        public int Total { get; set; }
        public int Processed { get; set; }
        public int Inativados { get; set; }

        public bool Done { get; set; }
        public string? Error { get; set; }

        public List<string> Erros { get; set; } = new();

        public int Percent => Total <= 0 ? 0 : (int)Math.Round((Processed * 100.0) / Total);
    }
}
