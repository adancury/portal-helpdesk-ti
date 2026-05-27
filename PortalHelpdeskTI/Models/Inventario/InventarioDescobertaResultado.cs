namespace PortalHelpdeskTI.Models.Inventario;

public class InventarioDescobertaResultado
{
    public string EnderecoIp { get; set; } = "";
    public string? EnderecoMac { get; set; }
    public string? Hostname { get; set; }
    public string? SistemaOperacional { get; set; }
    public string? Fabricante { get; set; }
    public string? Modelo { get; set; }
    public string? NumeroSerie { get; set; }
    public bool Online { get; set; }
    public bool JaCadastrado { get; set; }
    public int? EquipamentoId { get; set; }
    public string? NomeCadastrado { get; set; }
    public string? TipoCadastrado { get; set; }
}
