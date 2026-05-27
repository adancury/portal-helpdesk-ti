namespace PortalHelpdeskTI.ViewModels.Redistribuicao;

public class ClienteRedistribuicaoVm
{
    public string CodPN { get; set; } = "";
    public string? NomeFantasia { get; set; }
    public string? RazaoSocial { get; set; }
    public string? Cnpj { get; set; }
    public string? Estado { get; set; }

    public string CadastroPendente { get; set; } = "N"; // 'Y'/'N'
    public string? MotivoCadastroPendente { get; set; }

    public DateTime? UltimaVenda { get; set; }
    public string Status { get; set; } = ""; // Lead/Inativo
    public decimal KpiTkm { get; set; }
    public string? VendedorAtualNome { get; set; }

    public int? SlpCodeAtual { get; set; }
    public string? Ativo { get; set; } // Y/N
}
