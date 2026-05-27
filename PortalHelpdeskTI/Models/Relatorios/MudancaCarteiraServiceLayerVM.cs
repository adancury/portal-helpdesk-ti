using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace PortalHelpdeskTI.Models.Relatorios
{
    public class MudancaCarteiraServiceLayerServiceVM
    {
        [Required(ErrorMessage = "Informe o usuário SAP.")]
        public string SapUser { get; set; } = string.Empty;

        [Required(ErrorMessage = "Informe a senha SAP.")]
        [DataType(DataType.Password)]
        public string SapPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Selecione o arquivo Excel.")]
        public IFormFile? Arquivo { get; set; }

        public bool AtualizarPrincipal { get; set; } = true;
    }
}
