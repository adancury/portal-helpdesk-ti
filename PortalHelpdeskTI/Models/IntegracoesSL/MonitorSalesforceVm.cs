using System;
using System.Collections.Generic;

namespace PortalHelpdeskTI.Models.IntegracoesSL
{
    // ✅ POCO: Dapper mapeia sem dor (construtor vazio + set)
    public sealed class NexxEventDto
    {
        public string? EventId { get; set; }
        public string? ObjectType { get; set; }
        public string? TransactionType { get; set; }
        public string? PrimaryKeys { get; set; }
        public string? EventData { get; set; }
        public DateTime? CreatedDate { get; set; }
        public string? CreatedBy { get; set; }
        public string? Status { get; set; }
        public string? ErrorMessage { get; set; }
        public int? RetryCount { get; set; }
        public int? Priority { get; set; }
        public DateTime? ProcessedDate { get; set; }
        public string? CorrelationId { get; set; }
        public string? ParentEventId { get; set; }
    }

    public sealed class NexxLogDto
    {
        public DateTime? CreateDate { get; set; }
        public int? CreateTime { get; set; } // HHMM (ex.: 2359)
        public string? TipoDocumento { get; set; }
        public string? IdDocumento { get; set; }
        public DateTime? DataRegistro { get; set; }
        public DateTime? DataIntegracao { get; set; }
        public int? Status { get; set; }
        public string? IdRetorno { get; set; }
        public string? Mensagem { get; set; }
        public string? IdDocumentoLegado { get; set; }
        public string? JsonEnvio { get; set; }
        public string? JsonRetorno { get; set; }
    }

    public sealed class MonitorSalesforceVm
    {
        public string Aba { get; set; } = "events"; // events | logs

        public DateTime? DataIni { get; set; }
        public DateTime? DataFim { get; set; }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;

        public string? Q { get; set; }

        public string? StatusEvent { get; set; }
        public int? StatusLog { get; set; }
        public string? TipoDoc { get; set; }

        public List<NexxEventDto> Events { get; set; } = new();
        public List<NexxLogDto> Logs { get; set; } = new();

        public Dictionary<string, int> KpiEvents { get; set; } = new();
        public Dictionary<int, int> KpiLogs { get; set; } = new();

        public List<string> StatusEventsDisponiveis { get; set; } = new();
        public List<int> StatusLogsDisponiveis { get; set; } = new();
        public List<string> TiposDocDisponiveis { get; set; } = new();
        public int Total { get; set; }
        public int TotalEvents { get; set; }
        public int TotalLogs { get; set; }
        public int EventsPendentes { get; set; }
        public int EventsProcessados { get; set; }
        public int EventsErros { get; set; }

        public int LogsPendentes { get; set; }
        public int LogsProcessados { get; set; }
        public int LogsErros { get; set; }

    }
}
