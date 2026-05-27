using System.ComponentModel.DataAnnotations;

namespace PortalHelpdeskTI.Models.Relatorios
{
    public class InativarParceiroServiceLayerVM
    {
        [Required(ErrorMessage = "Informe o CardCode do Parceiro de Negócio.")]
        public string CardCode { get; set; } = string.Empty;

        public bool Confirmar { get; set; } = false;
    }
}
