using Microsoft.Extensions.Options;
using PortalHelpdeskTI.Models;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PortalHelpdeskTI.Services
{
    public class TranscricaoService
    {
        private readonly TranscricaoSettings _cfg;
        private readonly ILogger<TranscricaoService> _log;

        public TranscricaoService(IOptions<TranscricaoSettings> cfg, ILogger<TranscricaoService> log)
        {
            _cfg = cfg.Value;
            _log = log;
        }

        public async Task<string> TranscreverAsync(string audioPath, string workDir)
        {
            Directory.CreateDirectory(workDir);

            var inputFull = Path.GetFullPath(audioPath);
            var ext = Path.GetExtension(inputFull).ToLowerInvariant();

            // Caminhos auxiliares
            var wavPath = Path.GetFullPath(Path.Combine(workDir, "audio.wav")); // alvo para ffmpeg
            string whisperInput;  // arquivo que vamos passar ao whisper
            bool precisaWavParaDiarizacao = _cfg.DiarizationEnabled;

            // ===== 1) Normaliza entrada para o whisper =====
            if (ext == ".wav")
            {
                // WAV já pronto para whisper
                whisperInput = inputFull;

                // Se quiser garantir 16k mono pro diarizador: copie/normalize (opcional)
                if (precisaWavParaDiarizacao)
                    File.Copy(inputFull, wavPath, overwrite: true);
            }
            else if (ext == ".mp4")
            {
                // MP4 é contêiner de VÍDEO → whisper-cli não lê diretamente.
                // Extraímos o ÁUDIO para WAV 16k mono e usamos esse WAV no whisper.
                await RunAsync(_cfg.FfmpegPath, $"-y -i \"{inputFull}\" -vn -ar 16000 -ac 1 -f wav \"{wavPath}\"");
                whisperInput = wavPath;                  // <-- usar WAV no whisper
                precisaWavParaDiarizacao = true;         // já temos WAV para a diarização
            }
            else
            {
                // Outros formatos (mp3/ogg/webm etc): garantir WAV para evitar surpresas
                await RunAsync(_cfg.FfmpegPath, $"-y -i \"{inputFull}\" -ar 16000 -ac 1 \"{wavPath}\"");
                whisperInput = wavPath;
                precisaWavParaDiarizacao = true;
            }

            // ===== 2) Whisper em JSON (-oj) =====
            var args = $"-m \"{_cfg.ModelPath}\" -f \"{whisperInput}\" -l {_cfg.Language} -oj";
            _log.LogInformation($"Executando: {_cfg.WhisperExePath} {args}");
            await RunAsync(_cfg.WhisperExePath, args);

            // O whisper gera <arquivo_de_entrada>.json no MESMO diretório do arquivo passado em -f
            var whisperJsonPath = whisperInput + ".json";      // <- se WAV, será "audio.wav.json"
            if (!File.Exists(whisperJsonPath))
                throw new Exception($"Transcrição falhou: JSON não gerado. Esperado: {whisperJsonPath}");

            var whisperJsonText = await File.ReadAllTextAsync(whisperJsonPath, Encoding.UTF8);
            _log.LogInformation($"JSON Whisper:\n{whisperJsonText}");
            using var whisperDoc = JsonDocument.Parse(whisperJsonText);

            if (!TryGetWhisperSegments(whisperDoc.RootElement, out var whisperSegments, out var mode))
                throw new Exception("JSON do Whisper sem 'segments' e sem 'transcription'.");

            // ===== 3) Diarização (opcional) =====
            var speakers = new List<SpeakerSegment>();
            if (_cfg.DiarizationEnabled)
            {
                try
                {
                    // Se não existe wav ainda (caso mp4 sem conversão), garanta um wav para a diarização
                    if (!File.Exists(wavPath))
                        await RunAsync(_cfg.FfmpegPath, $"-y -i \"{inputFull}\" -vn -ar 16000 -ac 1 -f wav \"{wavPath}\"");

                    speakers = await RodarDiarizacaoAsync(wavPath, workDir);
                    _log.LogInformation($"Diarização: {speakers.Count} segmentos de fala.");
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Falha na diarização - prosseguindo sem rótulos de interlocutor.");
                }
            }

            // ===== 4) Montagem final =====
            var sb = new StringBuilder();
            foreach (var seg in whisperSegments.EnumerateArray())
            {
                string texto = seg.GetProperty("text").GetString() ?? "";
                double segStart, segEnd;

                if (mode == WhisperMode.TranscriptionArray && seg.TryGetProperty("timestamps", out var ts))
                {
                    var from = ts.GetProperty("from").GetString() ?? "00:00:00,000";
                    var to = ts.GetProperty("to").GetString() ?? "00:00:00,000";
                    segStart = HhmmssCommaToSeconds(from);
                    segEnd = HhmmssCommaToSeconds(to);
                }
                else
                {
                    segStart = seg.GetProperty("start").GetDouble();
                    segEnd = seg.GetProperty("end").GetDouble();
                }

                string who = (speakers.Count > 0) ? (EncontrarSpeaker(speakers, segStart, segEnd) ?? "SPEAKER_??") : "";
                var tIni = TimeSpan.FromSeconds(segStart).ToString(@"hh\:mm\:ss");

                if (!string.IsNullOrEmpty(who))
                    sb.AppendLine($"[{tIni}] {who}: {texto}");
                else
                    sb.AppendLine($"[{tIni}] {texto}");
            }

            await File.WriteAllTextAsync(Path.Combine(workDir, "transcricao_final.txt"), sb.ToString(), Encoding.UTF8);
            return sb.ToString();
        }


        // === Helpers ===

        private enum WhisperMode { SegmentsArray, TranscriptionArray }

        private static bool TryGetWhisperSegments(JsonElement root, out JsonElement segments, out WhisperMode mode)
        {
            if (root.TryGetProperty("segments", out var s))
            {
                segments = s;
                mode = WhisperMode.SegmentsArray;
                return true;
            }

            if (root.TryGetProperty("transcription", out var t))
            {
                segments = t;
                mode = WhisperMode.TranscriptionArray;
                return true;
            }

            segments = default;
            mode = default;
            return false;
        }

        private static double HhmmssCommaToSeconds(string hhmmssComma)
        {
            // "HH:MM:SS,ms" -> segundos (ignora ms)
            var main = hhmmssComma.Split(',')[0]; // "HH:MM:SS"
            var ps = main.Split(':');
            int hh = int.Parse(ps[0]), mm = int.Parse(ps[1]), ss = int.Parse(ps[2]);
            return hh * 3600 + mm * 60 + ss;
        }

        public class SpeakerSegment
        {
            public string speaker { get; set; } = "";
            public double start { get; set; }
            public double end { get; set; }
        }

        private static string? EncontrarSpeaker(List<SpeakerSegment> spks, double segStart, double segEnd)
        {
            // heurística: usa o ponto médio; se não cair em nenhum, escolhe maior overlap
            var mid = (segStart + segEnd) / 2.0;
            foreach (var s in spks)
                if (mid >= s.start && mid <= s.end) return s.speaker;

            double bestOverlap = 0; string? best = null;
            foreach (var s in spks)
            {
                var overlap = Math.Max(0, Math.Min(segEnd, s.end) - Math.Max(segStart, s.start));
                if (overlap > bestOverlap) { bestOverlap = overlap; best = s.speaker; }
            }
            return best;
        }

        private async Task<List<SpeakerSegment>> RodarDiarizacaoAsync(string wavPath, string workDir)
        {
            var outJson = Path.Combine(workDir, "diarization.json");
            var args = $"\"{_cfg.DiarizeScriptPath}\" \"{wavPath}\" \"{outJson}\"";

            var env = new Dictionary<string, string?>();
            if (!string.IsNullOrWhiteSpace(_cfg.HuggingFaceToken))
                env["HUGGINGFACE_TOKEN"] = _cfg.HuggingFaceToken;

            await RunAsync(_cfg.PythonExePath, args, env);

            if (!File.Exists(outJson))
                throw new Exception("Diarização falhou: diarization.json não foi gerado.");

            var text = await File.ReadAllTextAsync(outJson, Encoding.UTF8);
            using var doc = JsonDocument.Parse(text);
            var list = new List<SpeakerSegment>();
            if (doc.RootElement.TryGetProperty("speakers", out var arr))
            {
                foreach (var it in arr.EnumerateArray())
                {
                    list.Add(new SpeakerSegment
                    {
                        speaker = it.GetProperty("speaker").GetString() ?? "",
                        start = it.GetProperty("start").GetDouble(),
                        end = it.GetProperty("end").GetDouble()
                    });
                }
            }
            return list;
        }

        private async Task RunAsync(string exe, string args, IDictionary<string, string?>? extraEnv = null)
        {
            if (string.IsNullOrWhiteSpace(exe))
                throw new Exception("Caminho do executável não configurado.");
            if (!File.Exists(exe) && !IsInPath(exe))
                throw new Exception($"Executável não encontrado: {exe}");

            using var p = new Process();
            p.StartInfo.FileName = exe;
            p.StartInfo.Arguments = args;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;

            // working dir do executável
            var exeDir = Path.GetDirectoryName(exe);
            if (!string.IsNullOrEmpty(exeDir)) p.StartInfo.WorkingDirectory = exeDir;

            if (extraEnv != null)
            {
                foreach (var kv in extraEnv)
                    p.StartInfo.Environment[kv.Key] = kv.Value;
            }

            _log.LogInformation($"RUN: {exe} {args}");
            p.Start();
            string stdOut = await p.StandardOutput.ReadToEndAsync();
            string stdErr = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();

            _log.LogInformation($"EXIT {p.ExitCode}\nSTDOUT:\n{stdOut}\nSTDERR:\n{stdErr}");

            if (p.ExitCode != 0)
                throw new Exception($"Falha ao executar '{exe} {args}'\nSTDERR:\n{stdErr}\nSTDOUT:\n{stdOut}");
        }

        private static bool IsInPath(string exe)
        {
            try
            {
                var envPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var dir in envPath.Split(Path.PathSeparator))
                {
                    var candidate = Path.Combine(dir, exe);
                    if (File.Exists(candidate)) return true;
                }
            }
            catch { }
            return false;
        }
    }
}
