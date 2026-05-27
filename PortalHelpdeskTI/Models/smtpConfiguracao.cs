namespace PortalHelpdeskTI.Models
{
    public class SmtpConfiguracao
    {
        public int Id { get; set; }
        public string SmtpServer { get; set; }
        public int SmtpPort { get; set; }
        public string SmtpUser { get; set; }
        public string SmtpPass { get; set; }
        public string FromEmail { get; set; }
    }
}
