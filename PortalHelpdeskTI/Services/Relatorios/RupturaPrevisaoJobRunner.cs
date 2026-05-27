using Microsoft.Extensions.Options;
using PortalHelpdeskTI.Models;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace PortalHelpdeskTI.Services.Relatorios
{
    public class RupturaPrevisaoJobStatus
    {
        public bool IsRunning { get; set; }
        public int Percentual { get; set; }
        public string Mensagem { get; set; } = "Aguardando execucao.";
        public DateTime? StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public bool LastRunSucceeded { get; set; }
        public string? LastError { get; set; }
    }

    public class RupturaPrevisaoJobRunner
    {
        private static readonly Regex ProgressRegex = new(@"^PROGRESS:(\d+):(.*)$", RegexOptions.Compiled);

        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly object _statusLock = new();
        private readonly RupturaPrevisaoJobSettings _settings;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<RupturaPrevisaoJobRunner> _logger;
        private RupturaPrevisaoJobStatus _status = new();

        public RupturaPrevisaoJobRunner(
            IOptions<RupturaPrevisaoJobSettings> settings,
            IWebHostEnvironment env,
            ILogger<RupturaPrevisaoJobRunner> logger)
        {
            _settings = settings.Value;
            _env = env;
            _logger = logger;
        }

        public RupturaPrevisaoJobStatus GetStatus()
        {
            lock (_statusLock)
            {
                return new RupturaPrevisaoJobStatus
                {
                    IsRunning = _status.IsRunning,
                    Percentual = _status.Percentual,
                    Mensagem = _status.Mensagem,
                    StartedAt = _status.StartedAt,
                    FinishedAt = _status.FinishedAt,
                    LastRunSucceeded = _status.LastRunSucceeded,
                    LastError = _status.LastError
                };
            }
        }

        public async Task<bool> TryStartAsync(string origem, CancellationToken cancellationToken)
        {
            if (!await _lock.WaitAsync(0, cancellationToken))
                return false;

            _ = Task.Run(async () =>
            {
                try
                {
                    await RunScriptAsync(origem, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao executar previsao de ruptura.");
                    SetStatus(isRunning: false, percentual: 100, mensagem: "Erro ao atualizar previsao de ruptura.", lastRunSucceeded: false, lastError: ex.Message);
                }
                finally
                {
                    _lock.Release();
                }
            });

            return true;
        }

        public async Task RunAsync(string origem, CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                await RunScriptAsync(origem, cancellationToken);
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task RunScriptAsync(string origem, CancellationToken stoppingToken)
        {
            if (string.IsNullOrWhiteSpace(_settings.ScriptPath))
                throw new InvalidOperationException("RupturaPrevisaoJob:ScriptPath nao configurado.");

            var scriptPath = ResolveScriptPath(_settings.ScriptPath);

            if (!File.Exists(scriptPath))
                throw new FileNotFoundException("Script Python de previsao de ruptura nao encontrado.", scriptPath);

            var pythonExePath = ResolvePythonExePath(_settings.PythonExePath);

            SetStatus(
                isRunning: true,
                percentual: 0,
                mensagem: $"Iniciando atualizacao ({origem})...",
                startedAt: DateTime.Now,
                finishedAt: null,
                lastRunSucceeded: false,
                lastError: null);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, _settings.TimeoutMinutes)));

            using var process = new Process();
            process.StartInfo.FileName = pythonExePath;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.ArgumentList.Add(scriptPath);

            var scriptDir = Path.GetDirectoryName(scriptPath);
            if (!string.IsNullOrWhiteSpace(scriptDir))
                process.StartInfo.WorkingDirectory = scriptDir;

            var outputLock = new object();
            var stdoutTail = new Queue<string>();
            var stderrTail = new Queue<string>();

            _logger.LogInformation("Iniciando script de previsao de ruptura: {Python} {Script}", pythonExePath, scriptPath);

            process.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data))
                    return;

                _logger.LogInformation("Saida da previsao de ruptura: {Stdout}", e.Data);
                lock (outputLock)
                {
                    AppendTail(stdoutTail, e.Data);
                }
                TryApplyProgress(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data))
                    return;

                _logger.LogWarning("Erros/avisos da previsao de ruptura: {Stderr}", e.Data);
                lock (outputLock)
                {
                    AppendTail(stderrTail, e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);

                if (process.ExitCode != 0)
                {
                    string detalhes;
                    lock (outputLock)
                    {
                        detalhes = BuildProcessFailureMessage(process.ExitCode, stderrTail, stdoutTail);
                    }

                    throw new InvalidOperationException(detalhes);
                }

                SetStatus(isRunning: false, percentual: 100, mensagem: "Atualizacao concluida.", finishedAt: DateTime.Now, lastRunSucceeded: true, lastError: null);
                _logger.LogInformation("Script de previsao de ruptura finalizado com sucesso.");
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                TryKill(process);
                throw new TimeoutException($"Script de previsao de ruptura excedeu o timeout de {_settings.TimeoutMinutes} minutos.");
            }
        }

        private void TryApplyProgress(string line)
        {
            var match = ProgressRegex.Match(line.Trim());
            if (!match.Success)
                return;

            var percentual = Math.Clamp(int.Parse(match.Groups[1].Value), 0, 100);
            var mensagem = match.Groups[2].Value.Trim();
            SetStatus(isRunning: true, percentual: percentual, mensagem: mensagem);
        }

        private void SetStatus(
            bool? isRunning = null,
            int? percentual = null,
            string? mensagem = null,
            DateTime? startedAt = null,
            DateTime? finishedAt = null,
            bool? lastRunSucceeded = null,
            string? lastError = null)
        {
            lock (_statusLock)
            {
                _status = new RupturaPrevisaoJobStatus
                {
                    IsRunning = isRunning ?? _status.IsRunning,
                    Percentual = percentual ?? _status.Percentual,
                    Mensagem = mensagem ?? _status.Mensagem,
                    StartedAt = startedAt ?? _status.StartedAt,
                    FinishedAt = finishedAt,
                    LastRunSucceeded = lastRunSucceeded ?? _status.LastRunSucceeded,
                    LastError = lastError
                };
            }
        }

        private string ResolveScriptPath(string scriptPath)
        {
            if (Path.IsPathRooted(scriptPath))
                return scriptPath;

            return Path.GetFullPath(Path.Combine(_env.ContentRootPath, scriptPath));
        }

        private static string ResolvePythonExePath(string? configuredPath)
        {
            var candidates = new List<string>();

            if (!string.IsNullOrWhiteSpace(configuredPath))
                candidates.Add(configuredPath.Trim());

            foreach (var fallback in new[] { "python", "python3", "py" })
            {
                if (!candidates.Contains(fallback, StringComparer.OrdinalIgnoreCase))
                    candidates.Add(fallback);
            }

            foreach (var candidate in candidates)
            {
                if (Path.IsPathRooted(candidate) && File.Exists(candidate) && IsUsablePython(candidate))
                    return candidate;

                var pathCandidate = FindInPath(candidate);
                if (pathCandidate is not null && IsUsablePython(pathCandidate))
                    return pathCandidate;
            }

            var searched = string.Join(", ", candidates);
            throw new FileNotFoundException(
                $"Executavel do Python nao encontrado. Configure RupturaPrevisaoJob:PythonExePath ou instale Python no PATH. Locais testados: {searched}",
                configuredPath);
        }

        private static bool IsUsablePython(string executablePath)
        {
            try
            {
                using var process = new Process();
                process.StartInfo.FileName = executablePath;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.ArgumentList.Add("--version");

                if (!process.Start())
                    return false;

                if (!process.WaitForExit(milliseconds: 10000))
                {
                    TryKill(process);
                    return false;
                }

                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignora falha ao encerrar processo durante cancelamento/timeout.
            }
        }

        private static void AppendTail(Queue<string> lines, string line, int maxLines = 30)
        {
            lines.Enqueue(line);

            while (lines.Count > maxLines)
                lines.Dequeue();
        }

        private static string BuildProcessFailureMessage(int exitCode, IEnumerable<string> stderrTail, IEnumerable<string> stdoutTail)
        {
            var sb = new StringBuilder($"Script de previsao de ruptura finalizou com codigo {exitCode}.");
            var stderr = stderrTail.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
            var stdout = stdoutTail.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();

            if (stderr.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Ultimas linhas de erro:");
                foreach (var line in stderr)
                    sb.AppendLine(line);
            }

            if (stdout.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Ultimas linhas de saida:");
                foreach (var line in stdout)
                    sb.AppendLine(line);
            }

            return sb.ToString().TrimEnd();
        }

        private static string? FindInPath(string exe)
        {
            var envPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            var extensions = Path.HasExtension(exe)
                ? new[] { "" }
                : (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
                    .Split(';', StringSplitOptions.RemoveEmptyEntries);

            foreach (var dir in envPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    foreach (var extension in extensions)
                    {
                        var candidate = Path.Combine(dir.Trim(), exe + extension);
                        if (File.Exists(candidate) && !IsWindowsAppsPythonAlias(candidate))
                            return candidate;
                    }
                }
                catch
                {
                    // Ignora entradas invalidas no PATH.
                }
            }

            return null;
        }

        private static bool IsWindowsAppsPythonAlias(string path)
        {
            var fileName = Path.GetFileName(path);
            if (!fileName.Equals("python.exe", StringComparison.OrdinalIgnoreCase) &&
                !fileName.Equals("python3.exe", StringComparison.OrdinalIgnoreCase))
                return false;

            return path.Contains(
                Path.Combine("Microsoft", "WindowsApps"),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
