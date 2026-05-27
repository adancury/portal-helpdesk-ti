using System.ComponentModel.DataAnnotations;

namespace PortalHelpdeskTI.Models.IntegracoesWmsDados
{
    public class WmsProcessoLog
    {
        public int Id { get; set; }
        public int WmsProcessoId { get; set; }
        public WmsProcesso? WmsProcesso { get; set; }

        [MaxLength(40)]
        public string Tipo { get; set; } = "";

        [MaxLength(120)]
        public string ChaveProcesso { get; set; } = "";

        [MaxLength(220)]
        public string ChaveItem { get; set; } = "";

        [MaxLength(40)]
        public string Evento { get; set; } = "";

        [MaxLength(80)]
        public string? StatusAnterior { get; set; }

        [MaxLength(80)]
        public string? StatusNovo { get; set; }

        public string? CamposAlteradosJson { get; set; }
        public string RawJson { get; set; } = "";
        public DateTime CriadoEm { get; set; }
    }
}
