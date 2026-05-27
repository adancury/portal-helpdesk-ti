using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;

namespace PortalHelpdeskTI.Models.Relatorios
{
    public class MudancaCarteiraUploadViewModel
    {
        [Required(ErrorMessage = "Selecione o arquivo da planilha.")]
        [Display(Name = "Arquivo Excel")]
        public IFormFile? Arquivo { get; set; }

        [Required(ErrorMessage = "Escolha qual vendedor deseja atualizar.")]
        [Display(Name = "Tipo de vendedor")]
        public string? TipoVendedor { get; set; } // "principal" ou "secundario"

        // Resultado do processamento
        public int TotalLinhas { get; set; }
        public int Atualizados { get; set; }
        public List<string> Erros { get; set; } = new();
        public bool Processado { get; set; }
    }
}
