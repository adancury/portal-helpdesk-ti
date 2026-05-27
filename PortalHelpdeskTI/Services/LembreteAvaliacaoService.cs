using Microsoft.EntityFrameworkCore;

namespace PortalHelpdeskTI.Services
{
    public class LembreteAvaliacaoService
    {
        private readonly AppDbContext _db;
        private readonly ChamadoService _chamadoService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<LembreteAvaliacaoService> _logger;

        public LembreteAvaliacaoService(
            AppDbContext db,
            ChamadoService chamadoService,
            IConfiguration configuration,
            ILogger<LembreteAvaliacaoService> logger)
        {
            _db = db;
            _chamadoService = chamadoService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<int> EnviarLembretesPendentesAsync(CancellationToken ct = default)
        {
            var baseUrl = (_configuration["Portal:BaseUrl"] ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                _logger.LogWarning("Portal:BaseUrl nao configurado. Lembretes automaticos de avaliacao nao serao enviados.");
                return 0;
            }

            var limiteConclusao = DateTime.Now.AddHours(-24);

            var pendentes = await _db.Chamados
                .Include(c => c.Usuario)
                .Where(c =>
                    c.Status == "Concluído" &&
                    c.DataConclusao.HasValue &&
                    c.DataConclusao.Value <= limiteConclusao &&
                    !_db.AvaliacoesChamado.Any(a => a.ChamadoId == c.Id && a.UsuarioId == c.UsuarioId) &&
                    !c.AvaliacaoLembreteEnviado)
                .ToListAsync(ct);

            var enviados = 0;
            foreach (var chamado in pendentes)
            {
                ct.ThrowIfCancellationRequested();

                var linkAvaliacao = $"{baseUrl}/Chamados/Avaliar/{chamado.Id}";
                if (await _chamadoService.EnviarLembreteAvaliacaoAsync(chamado, linkAvaliacao))
                    enviados++;
            }

            return enviados;
        }
    }
}
