using System.Linq;
using PortalHelpdeskTI.Models; // <- para enxergar AvaliacaoChamado (modelo)

namespace PortalHelpdeskTI.Services.AvaliacaoChamado  // <- combine com a pasta
{
    public class AvaliacaoService
    {
        private readonly AppDbContext _context;

        public AvaliacaoService(AppDbContext context)
        {
            _context = context;
        }

        public async Task SalvarAvaliacaoAsync(int chamadoId, int usuarioId, int nota, string? comentario)
        {
            var avaliacao = new PortalHelpdeskTI.Models.AvaliacaoChamado // se quiser, use o nome completo p/ evitar ambiguidade
            {
                ChamadoId = chamadoId,
                UsuarioId = usuarioId,
                Nota = nota,
                Comentario = comentario,
                DataAvaliacao = DateTime.Now
            };

            _context.AvaliacoesChamado.Add(avaliacao);
            await _context.SaveChangesAsync();
        }

        public bool JaAvaliado(int chamadoId, int usuarioId)
        {
            return _context.AvaliacoesChamado
                           .Any(a => a.ChamadoId == chamadoId && a.UsuarioId == usuarioId);
        }
    }
}
