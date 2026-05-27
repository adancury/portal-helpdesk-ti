// Models/Relatorios/MudancaCarteiraServiceLayerUploadVM.cs
using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace PortalHelpdeskTI.Models.Relatorios
{
    public class MudancaCarteiraServiceLayerUploadVM
    {
        [Required]
        public IFormFile? Arquivo { get; set; }

        public bool AtualizarPrincipal { get; set; } = true;
    }
}
