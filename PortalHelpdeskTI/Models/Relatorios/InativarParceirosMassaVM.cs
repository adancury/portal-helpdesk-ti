using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace PortalHelpdeskTI.Models.Relatorios
{
    public class InativarParceirosMassaVM
    {
        [Required(ErrorMessage = "Selecione um arquivo Excel válido.")]
        public IFormFile? Arquivo { get; set; }

        public bool Confirmar { get; set; } = false;
    }
}
