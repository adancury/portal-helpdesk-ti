using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PortalHelpdeskTI.Models.Inventario;

[Table("InventarioAnexos")]
public class InventarioAnexo
{
    public int Id { get; set; }

    public int EquipamentoInventarioId { get; set; }
    public EquipamentoInventario EquipamentoInventario { get; set; } = default!;

    [Required, MaxLength(260)]
    public string NomeOriginal { get; set; } = string.Empty;

    [Required, MaxLength(260)]
    public string NomeArquivo { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string CaminhoRelativo { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? ContentType { get; set; }

    public long TamanhoBytes { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.Now;
}
