using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PortalHelpdeskTI.Models
{
    [Table("ChamadoStatusLogs")]
    [Index(nameof(ChamadoId), nameof(DataHora), Name = "IX_ChamadoStatusLogs_Cha_Data")]
    public class ChamadoStatusLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [ForeignKey(nameof(Chamado))]
        public int ChamadoId { get; set; }

        [Required, MaxLength(80)]
        public string Status { get; set; } = "";  // ex.: "Aberto", "Em atendimento", "Aguardando retorno", "Concluído"

        [Required]
        public DateTime DataHora { get; set; }    // quando alterou

        public int? UsuarioId { get; set; }       // quem alterou (opcional)

        public Chamado? Chamado { get; set; }
    }
}
