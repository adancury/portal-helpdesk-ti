using System.ComponentModel.DataAnnotations;

namespace PortalHelpdeskTI.Models
{
    public class EmailSettingsViewModel
    {
        [Required]
        public string SmtpServer { get; set; }

        [Required]
        [Range(1, 65535)]
        public int SmtpPort { get; set; }

        [Required]
        public string SmtpUser { get; set; }

        [Required]
        public string SmtpPass { get; set; }

        [Required]
        [EmailAddress]
        public string FromEmail { get; set; }
    }

}
