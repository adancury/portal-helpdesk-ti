using Microsoft.AspNetCore.Mvc;
using PortalHelpdeskTI.Services;
using System.Text.RegularExpressions;

namespace PortalHelpdeskTI.Controllers
{
    public class ReunioesController : Controller
    {
        private readonly TranscricaoService _transcricao;
        private readonly AtaService _ata;
        private readonly ChatGptAtaService _chatGptAta;

        public ReunioesController(TranscricaoService transcricao, AtaService ata, ChatGptAtaService chatGptAta)
        {
            _transcricao = transcricao;
            _ata = ata;
            _chatGptAta = chatGptAta;
        }

        [HttpGet]
        public IActionResult Nova() => View();

        [HttpPost]
        [RequestSizeLimit(250_000_000)] // ~250MB
        public async Task<IActionResult> UploadAudio(IFormFile audio, string titulo, string participantes, string emailsDestino)
        {
            if (audio == null || audio.Length == 0)
                return BadRequest("Áudio vazio.");

            // Pasta segura fora do wwwroot
            var baseDir = System.IO.Path.Combine("Anexos", "Reunioes");
            Directory.CreateDirectory(baseDir);

            // ID para agrupar arquivos desta gravação (string/Guid)
            var reuniaoId = Guid.NewGuid().ToString("N");
            var dir = System.IO.Path.Combine(baseDir, reuniaoId);
            Directory.CreateDirectory(dir);

            // Detectar extensão pelo ContentType ou nome
            var ext = ObterExtensaoAudio(audio);
            var audioPath = System.IO.Path.Combine(dir, $"audio{ext}");

            using (var fs = new FileStream(audioPath, FileMode.Create))
                await audio.CopyToAsync(fs);

            // (Opcional) você pode já disparar a transcrição aqui de forma assíncrona em background.
            // Ex.: _ = Task.Run(async () => { var txt = await _transcricao.TranscreverAsync(audioPath); await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(dir, "transcricao.txt"), txt); });

            return Json(new
            {
                ok = true,
                reuniaoId,                         // <- FRONT usa isso depois para chamar /Reunioes/GerarAta
                audioPath = audioPath.Replace("\\", "/"),
                titulo,
                participantes,
                emailsDestino
            });
        }

        [HttpPost]
        public async Task<IActionResult> GerarAta(
            string reuniaoId,
            string titulo,
            string participantes,
            bool usarGpt = false,
            string? descricao = null)
        {
            if (string.IsNullOrWhiteSpace(reuniaoId))
                return BadRequest("reuniaoId inválido.");

            var baseDir = System.IO.Path.Combine("Anexos", "Reunioes");
            var dir = System.IO.Path.Combine(baseDir, reuniaoId);

            if (!Directory.Exists(dir))
                return BadRequest("Diretório da reunião não encontrado. Faça o upload do áudio primeiro.");

            // Localiza o áudio salvo (audio.*)
            var audioPath = Directory.GetFiles(dir, "audio.*").FirstOrDefault();
            if (audioPath == null)
                return BadRequest("Áudio não encontrado no diretório da reunião.");

            var caminhoTranscricao = System.IO.Path.Combine(dir, "transcricao.txt");

            // Transcrição (gera se não existir)
            string transcricaoTexto;
            if (!System.IO.File.Exists(caminhoTranscricao))
            {
                transcricaoTexto = await _transcricao.TranscreverAsync(audioPath, dir);
                await System.IO.File.WriteAllTextAsync(caminhoTranscricao, transcricaoTexto);
            }
            else
            {
                transcricaoTexto = await System.IO.File.ReadAllTextAsync(caminhoTranscricao);
            }

            // Participantes da tela
            var participantesTela = (participantes ?? "")
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .ToList();

            // Horários (ajuste se tiver persistência real)
            DateTime inicio = DateTime.Now.AddMinutes(-30);
            DateTime fim = DateTime.Now;

            // === Escolha do gerador de ata ===
            ParsedMinutes parsed;
            if (usarGpt)
            {
                var ataTexto = await _chatGptAta.GerarMinutesAsync(
                    transcricao: transcricaoTexto,
                    descricao: descricao,
                    titulo: titulo,
                    participantes: participantesTela
                );

                parsed = MinutesParser.Parse(ataTexto);

                // Garante que o objetivo seja preenchido, se não veio do GPT
                if (string.IsNullOrWhiteSpace(parsed.Objective) && !string.IsNullOrWhiteSpace(descricao))
                    parsed.Objective = descricao;

                // Merge de participantes
                parsed.Participants = (parsed.Participants ?? new List<string>())
                    .Union(participantesTela, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            else
            {
                parsed = MinutesParser.Parse(transcricaoTexto);
                if (string.IsNullOrWhiteSpace(parsed.Objective) && !string.IsNullOrWhiteSpace(descricao))
                    parsed.Objective = descricao.Trim();

                // garante merge com os da tela
                parsed.Participants = (parsed.Participants ?? new List<string>())
                    .Union(participantesTela, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            // Gera PDF no mesmo diretório
            string pdfPath = await _ata.GerarPdfEstruturadoAsync(
                diretorio: dir,
                titulo: titulo,
                participantesManuais: participantesTela,
                inicio: inicio,
                fim: fim,
                parsed: parsed,
                arquivoAudioRelativo: $"/Anexos/Reunioes/{reuniaoId}/" + System.IO.Path.GetFileName(audioPath)
            );

            var downloadUrl = Url.Action("DownloadAta", "Reunioes", new { reuniaoId }, protocol: Request.Scheme)
                              ?? $"/Reunioes/DownloadAta?reuniaoId={reuniaoId}";

            return Json(new { ok = true, pdf = downloadUrl });
        }
        [HttpGet]
        public IActionResult DownloadAta(string reuniaoId)
        {
            if (string.IsNullOrWhiteSpace(reuniaoId)) return BadRequest();

            var dir = System.IO.Path.Combine("Anexos", "Reunioes", reuniaoId);
            var pdfPath = System.IO.Path.Combine(dir, "ata.pdf");
            if (!System.IO.File.Exists(pdfPath)) return NotFound();

            var stream = new FileStream(pdfPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, "application/pdf", "ata.pdf");
        }

        // --- helpers ---

        private static string ObterExtensaoAudio(IFormFile audio)
        {
            // tenta pelo ContentType primeiro
            var ct = audio.ContentType?.ToLowerInvariant() ?? "";
            if (ct.Contains("webm")) return ".webm";
            if (ct.Contains("ogg")) return ".ogg";
            if (ct.Contains("mp4") || ct.Contains("mpeg4") || ct.Contains("aac") || ct.Contains("mp4a")) return ".mp4";
            if (ct.Contains("wav")) return ".wav";
            if (ct.Contains("mpeg") || ct.Contains("mp3")) return ".mp3";

            // fallback pelo nome do arquivo
            var name = audio.FileName?.ToLowerInvariant() ?? "";
            if (name.EndsWith(".webm")) return ".webm";
            if (name.EndsWith(".ogg")) return ".ogg";
            if (name.EndsWith(".mp4") || name.EndsWith(".m4a")) return ".mp4";
            if (name.EndsWith(".wav")) return ".wav";
            if (name.EndsWith(".mp3")) return ".mp3";

            // padrão
            return ".webm";
        }
    }
}
