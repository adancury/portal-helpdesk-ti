using System.ComponentModel.DataAnnotations;

namespace PortalHelpdeskTI.Models.Inventario;

public class InventarioColetaRequest
{
    [MaxLength(120)]
    public string? NomeComputador { get; set; }

    [MaxLength(120)]
    public string? Hostname { get; set; }

    [MaxLength(120)]
    public string? SistemaOperacional { get; set; }

    [MaxLength(80)]
    public string? Fabricante { get; set; }

    [MaxLength(80)]
    public string? Modelo { get; set; }

    [MaxLength(80)]
    public string? NumeroSerie { get; set; }

    [MaxLength(45)]
    public string? EnderecoIp { get; set; }

    [MaxLength(30)]
    public string? EnderecoMac { get; set; }

    [MaxLength(120)]
    public string? Localizacao { get; set; }

    [MaxLength(160)]
    public string? UsuarioLogado { get; set; }

    [MaxLength(160)]
    public string? Dominio { get; set; }
}
