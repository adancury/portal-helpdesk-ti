using System.ComponentModel.DataAnnotations;

namespace PortalHelpdeskTI.Models.IntegracoesWmsDados
{
    public class WmsSyncExecucao
    {
        public int Id { get; set; }

        [MaxLength(40)]
        public string Tipo { get; set; } = "";

        public DateTime InicioEm { get; set; }
        public DateTime? FimEm { get; set; }

        [MaxLength(30)]
        public string Status { get; set; } = "";

        public int RegistrosRecebidos { get; set; }
        public int RegistrosNovos { get; set; }
        public int RegistrosAlterados { get; set; }

        [MaxLength(500)]
        public string? Mensagem { get; set; }
    }
}
