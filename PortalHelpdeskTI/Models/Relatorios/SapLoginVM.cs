using System.ComponentModel.DataAnnotations;

namespace PortalHelpdeskTI.Models.Relatorios
{
    public class SapLoginVM
    {
        [Required]
        public string SapUser { get; set; } = "";

        [Required]
        [DataType(DataType.Password)]
        public string SapPassword { get; set; } = "";
    }
}
