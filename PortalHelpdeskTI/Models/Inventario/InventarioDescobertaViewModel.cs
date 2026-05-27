namespace PortalHelpdeskTI.Models.Inventario;

public class InventarioDescobertaViewModel
{
    public string Faixa { get; set; } = "";
    public string TipoPadrao { get; set; } = "Computador";
    public List<InventarioDescobertaResultado> Resultados { get; set; } = new();
    public string? Mensagem { get; set; }
}
