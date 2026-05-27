namespace PortalHelpdeskTI.ViewModels.Relatorios;

public sealed class DataTablePage
{
    public System.Data.DataTable Data { get; init; } = new();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int Total { get; init; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)Total / PageSize);
    public string? Search { get; init; }
}
