using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PortalHelpdeskTI.Models.Inventario;

[Table("InventarioEquipamentos")]
public class EquipamentoInventario
{
    public int Id { get; set; }

    [Required, MaxLength(40)]
    public string Tipo { get; set; } = "Computador";

    [Required, MaxLength(40)]
    public string Status { get; set; } = "Ativo";

    [Required, MaxLength(30)]
    public string OrigemCadastro { get; set; } = "Manual";

    [MaxLength(120)]
    public string? NomeEquipamento { get; set; }

    [MaxLength(80)]
    public string? Fabricante { get; set; }

    [MaxLength(80)]
    public string? Modelo { get; set; }

    [MaxLength(80)]
    public string? NumeroSerie { get; set; }

    [MaxLength(80)]
    public string? Patrimonio { get; set; }

    [MaxLength(45)]
    public string? EnderecoIp { get; set; }

    [MaxLength(30)]
    public string? EnderecoMac { get; set; }

    [MaxLength(120)]
    public string? Hostname { get; set; }

    [MaxLength(120)]
    public string? SistemaOperacional { get; set; }

    [MaxLength(120)]
    public string? Localizacao { get; set; }

    public int? ProprietarioUsuarioId { get; set; }
    public Usuario? ProprietarioUsuario { get; set; }

    [MaxLength(160)]
    public string? ProprietarioNomeManual { get; set; }

    [MaxLength(160)]
    public string? ProprietarioEmailManual { get; set; }

    [MaxLength(120)]
    public string? ProprietarioDepartamentoManual { get; set; }

    public DateTime CriadoEm { get; set; } = DateTime.Now;
    public DateTime AtualizadoEm { get; set; } = DateTime.Now;
    public DateTime? UltimaDescobertaEm { get; set; }

    public string? Observacoes { get; set; }

    public ICollection<InventarioAnexo> Anexos { get; set; } = new List<InventarioAnexo>();

    [NotMapped]
    public string ProprietarioDescricao =>
        ProprietarioUsuario?.Nome
        ?? ProprietarioNomeManual
        ?? "Sem proprietário";
}
