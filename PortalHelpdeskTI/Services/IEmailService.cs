using System.Threading;
using System.Threading.Tasks;

namespace PortalHelpdeskTI.Services
{
    public interface IEmailService
    {
        // legado (já usado no sistema)
        Task EnviarEmailAsync(string para, string assunto, string mensagem);

        // novo (comissões / PDF)
        Task EnviarComAnexoPdfAsync(
            string para,
            string assunto,
            string corpoHtml,
            byte[] pdfBytes,
            string nomeArquivoPdf,
            CancellationToken ct,
            string? cc = null);
    }
}
