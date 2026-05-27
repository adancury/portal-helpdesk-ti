using Microsoft.Extensions.Options;
using PortalHelpdeskTI.Models;
using System.Data.Odbc;

namespace PortalHelpdeskTI.Services.Relatorios
{
    public class RupturaPrevisaoBackgroundService : BackgroundService
    {
        private static readonly TimeSpan MinRetryDelay = TimeSpan.FromMinutes(5);

        private readonly RupturaPrevisaoJobSettings _settings;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RupturaPrevisaoBackgroundService> _logger;
        private readonly RupturaPrevisaoJobRunner _runner;

        public RupturaPrevisaoBackgroundService(
            IOptions<RupturaPrevisaoJobSettings> settings,
            IConfiguration configuration,
            ILogger<RupturaPrevisaoBackgroundService> logger,
            RupturaPrevisaoJobRunner runner)
        {
            _settings = settings.Value;
            _configuration = configuration;
            _logger = logger;
            _runner = runner;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_settings.Enabled)
            {
                _logger.LogInformation("RupturaPrevisaoBackgroundService desabilitado por configuracao.");
                return;
            }

            _logger.LogInformation("RupturaPrevisaoBackgroundService iniciado. Execucao diaria configurada para {RunAt}.", _settings.RunAt);

            if (_settings.RunOnStartup || await ShouldRunMissedStartupExecutionAsync(stoppingToken))
            {
                await TryRunAsync(stoppingToken);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = GetDelayUntilNextRun(DateTime.Now);

                try
                {
                    _logger.LogInformation("Proxima execucao da previsao de ruptura em {Delay}.", delay);
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                await TryRunAsync(stoppingToken);
            }

            _logger.LogInformation("RupturaPrevisaoBackgroundService finalizado.");
        }

        private async Task TryRunAsync(CancellationToken stoppingToken)
        {
            try
            {
                await _runner.RunAsync("agendamento", stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Execucao da previsao de ruptura cancelada.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao executar previsao de ruptura.");
            }
        }

        private TimeSpan GetDelayUntilNextRun(DateTime now)
        {
            if (!TimeOnly.TryParse(_settings.RunAt, out var runAt))
            {
                _logger.LogWarning("Horario invalido em RupturaPrevisaoJob:RunAt ({RunAt}). Usando 05:00.", _settings.RunAt);
                runAt = new TimeOnly(5, 0);
            }

            var nextRun = now.Date.Add(runAt.ToTimeSpan());
            if (nextRun <= now)
                nextRun = nextRun.AddDays(1);

            var delay = nextRun - now;
            return delay < TimeSpan.Zero ? MinRetryDelay : delay;
        }

        private async Task<bool> ShouldRunMissedStartupExecutionAsync(CancellationToken stoppingToken)
        {
            if (!_settings.RunMissedOnStartup)
                return false;

            if (!TimeOnly.TryParse(_settings.RunAt, out var runAt))
                runAt = new TimeOnly(5, 0);

            var scheduledToday = DateTime.Now.Date.Add(runAt.ToTimeSpan());
            if (DateTime.Now < scheduledToday)
                return false;

            var lastRun = await GetLastGeneratedAtAsync(stoppingToken);
            if (!lastRun.HasValue || lastRun.Value < scheduledToday)
            {
                _logger.LogInformation(
                    "Execucao de hoje da previsao de ruptura ainda nao encontrada. Ultima geracao: {LastRun}.",
                    lastRun);
                return true;
            }

            return false;
        }

        private async Task<DateTime?> GetLastGeneratedAtAsync(CancellationToken stoppingToken)
        {
            var connStr = _configuration.GetConnectionString("HanaConn");
            if (string.IsNullOrWhiteSpace(connStr))
            {
                _logger.LogWarning("ConnectionString 'HanaConn' nao configurada. Execucao perdida sera disparada por seguranca.");
                return null;
            }

            try
            {
                await using var cn = new OdbcConnection(connStr);
                await cn.OpenAsync(stoppingToken);

                await using var cmd = cn.CreateCommand();
                cmd.CommandText = @"SELECT MAX(""GeradoEm"") FROM ""Z_RUPTURA_PREV_CONSUMO""";

                var value = await cmd.ExecuteScalarAsync(stoppingToken);
                if (value == null || value == DBNull.Value)
                    return null;

                if (value is DateTime dt)
                    return dt;

                return DateTime.TryParse(value.ToString(), out var parsed)
                    ? parsed
                    : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Nao foi possivel verificar a ultima geracao da previsao de ruptura. Execucao perdida sera disparada por seguranca.");
                return null;
            }
        }

    }
}
