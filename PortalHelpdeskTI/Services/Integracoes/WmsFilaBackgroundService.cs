using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PortalHelpdeskTI.Services.Integracoes
{
    public class WmsFilaBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<WmsFilaBackgroundService> _logger;

        public WmsFilaBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<WmsFilaBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("WmsFilaBackgroundService iniciado.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var processador = scope.ServiceProvider.GetRequiredService<WmsProcessadorFilaService>();

                    _logger.LogInformation("Iniciando processamento automático da fila WMS.");
                    await processador.ProcessarAsync();
                    _logger.LogInformation("Processamento automático da fila WMS finalizado.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao processar fila WMS automaticamente.");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }

            _logger.LogInformation("WmsFilaBackgroundService finalizado.");
        }
    }
}