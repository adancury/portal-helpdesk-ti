using Microsoft.Extensions.Options;

namespace PortalHelpdeskTI.Services.Integracoes
{
    public class WmsProcessosBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly WmsDadosApiOptions _options;
        private readonly ILogger<WmsProcessosBackgroundService> _logger;

        public WmsProcessosBackgroundService(
            IServiceScopeFactory scopeFactory,
            IOptions<WmsDadosApiOptions> options,
            ILogger<WmsProcessosBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation("WmsProcessosBackgroundService desabilitado.");
                return;
            }

            if (_options.RunOnStartup)
                await Sincronizar(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes)), stoppingToken);
                await Sincronizar(stoppingToken);
            }
        }

        private async Task Sincronizar(CancellationToken ct)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<WmsProcessosSyncService>();
                await svc.SincronizarAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro na sincronização periódica de processos WMS.");
            }
        }
    }
}
