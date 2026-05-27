using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PortalHelpdeskTI.Services
{
    public class LembreteAvaliacaoBackgroundService : BackgroundService
    {
        private static readonly TimeSpan IntervaloExecucao = TimeSpan.FromHours(1);
        private static readonly TimeSpan AtrasoInicial = TimeSpan.FromMinutes(2);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<LembreteAvaliacaoBackgroundService> _logger;

        public LembreteAvaliacaoBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<LembreteAvaliacaoBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("LembreteAvaliacaoBackgroundService iniciado.");

            try
            {
                await Task.Delay(AtrasoInicial, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<LembreteAvaliacaoService>();

                    var enviados = await service.EnviarLembretesPendentesAsync(stoppingToken);
                    if (enviados > 0)
                        _logger.LogInformation("Lembretes automaticos de avaliacao enviados: {Quantidade}.", enviados);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao enviar lembretes automaticos de avaliacao.");
                }

                try
                {
                    await Task.Delay(IntervaloExecucao, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("LembreteAvaliacaoBackgroundService finalizado.");
        }
    }
}
