using Microsoft.EntityFrameworkCore;
using PortalHelpdeskTI.Models;
using System.Net;
using System.Net.Mail;

namespace PortalHelpdeskTI.Services
{
    public class EmailService : IEmailService
    {
        private readonly AppDbContext _context;

        public EmailService(AppDbContext context)
        {
            _context = context;
        }

        // =========================
        // MÉTODO LEGADO (já usado)
        // =========================
        public async Task EnviarEmailAsync(string para, string assunto, string mensagem)
        {
            var cfg = await ObterConfigAsync();

            using var client = CriarSmtp(cfg);

            using var mail = new MailMessage
            {
                From = new MailAddress(cfg.FromEmail),
                Subject = assunto,
                Body = mensagem,
                IsBodyHtml = true
            };

            mail.To.Add(para);

            await client.SendMailAsync(mail);
        }

        // =========================
        // MÉTODO NOVO (PDF/anexo + CC)
        // =========================
        public async Task EnviarComAnexoPdfAsync(
            string para,
            string assunto,
            string corpoHtml,
            byte[] pdfBytes,
            string nomeArquivoPdf,
            CancellationToken ct,
            string? cc = null)
        {
            var cfg = await ObterConfigAsync();

            using var client = CriarSmtp(cfg);

            using var mail = new MailMessage
            {
                From = new MailAddress(cfg.FromEmail),
                Subject = assunto,
                Body = corpoHtml,
                IsBodyHtml = true
            };

            mail.To.Add(para);

            // adiciona destinatários em cópia
            if (!string.IsNullOrWhiteSpace(cc))
            {
                var emailsCc = cc
                    .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase);

                foreach (var emailCc in emailsCc)
                    mail.CC.Add(emailCc);
            }

            if (pdfBytes?.Length > 0)
            {
                // o stream precisa permanecer vivo até o envio concluir
                var ms = new MemoryStream(pdfBytes);
                mail.Attachments.Add(new Attachment(ms, nomeArquivoPdf, "application/pdf"));
            }

            // SmtpClient não suporta CancellationToken nativamente
            await Task.Run(() => client.Send(mail), ct);
        }

        // =========================
        // Helpers
        // =========================
        private async Task<SmtpConfiguracao> ObterConfigAsync()
        {
            var cfg = await _context.SmtpConfiguracoes.AsNoTracking().FirstOrDefaultAsync();

            if (cfg == null)
                throw new InvalidOperationException("Nenhuma configuração SMTP encontrada no banco de dados.");

            if (string.IsNullOrWhiteSpace(cfg.SmtpServer))
                throw new InvalidOperationException("Config SMTP inválida: SmtpServer não informado.");

            if (cfg.SmtpPort <= 0)
                throw new InvalidOperationException("Config SMTP inválida: SmtpPort não informado.");

            if (string.IsNullOrWhiteSpace(cfg.FromEmail))
                throw new InvalidOperationException("Config SMTP inválida: FromEmail não informado.");

            return cfg;
        }

        private static SmtpClient CriarSmtp(SmtpConfiguracao cfg)
        {
            return new SmtpClient(cfg.SmtpServer, cfg.SmtpPort)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(cfg.SmtpUser, cfg.SmtpPass)
            };
        }
    }
}