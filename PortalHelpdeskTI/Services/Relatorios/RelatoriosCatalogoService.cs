using Microsoft.EntityFrameworkCore;
using PortalHelpdeskTI.Models.Permissoes;
using PortalHelpdeskTI.Services.Permissoes;

namespace PortalHelpdeskTI.Services.Relatorios
{
    public class RelatoriosCatalogoService
    {
        private readonly AppDbContext _db;

        public RelatoriosCatalogoService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<List<RelatorioCatalogo>> ListarAtivosAsync(CancellationToken ct)
        {
            var dbItems = await _db.RelatoriosCatalogo
                .AsNoTracking()
                .Where(x => x.Ativo)
                .OrderBy(x => x.Ordem)
                .ThenBy(x => x.Titulo)
                .ToListAsync(ct);

            return MesclarComPadroes(dbItems, somenteAtivos: true);
        }

        public async Task<List<RelatorioCatalogo>> ListarTodosAsync(CancellationToken ct)
        {
            var dbItems = await _db.RelatoriosCatalogo
                .AsNoTracking()
                .OrderByDescending(x => x.Ativo)
                .ThenBy(x => x.Ordem)
                .ThenBy(x => x.Titulo)
                .ToListAsync(ct);

            return MesclarComPadroes(dbItems, somenteAtivos: false);
        }

        public async Task<bool> SetAtivoAsync(int id, bool ativo, CancellationToken ct)
        {
            var item = await _db.RelatoriosCatalogo.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (item == null) return false;

            item.Ativo = ativo;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        private static List<RelatorioCatalogo> MesclarComPadroes(List<RelatorioCatalogo> dbItems, bool somenteAtivos)
        {
            var result = dbItems
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .Select(x =>
                {
                    x.Key = x.Key.Trim().ToUpperInvariant();
                    return x;
                })
                .ToList();

            var keys = result
                .Select(x => x.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var ordem = result.Count == 0 ? 0 : result.Max(x => x.Ordem);

            foreach (var item in RelatoriosCatalogo.Todos)
            {
                var key = (item.Key ?? "").Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(key) || keys.Contains(key))
                    continue;

                result.Add(new RelatorioCatalogo
                {
                    Key = key,
                    Titulo = item.Titulo,
                    Descricao = item.Descricao,
                    Departamento = item.Departamento,
                    UrlVisualizar = item.UrlVisualizar,
                    Ativo = true,
                    Ordem = ++ordem
                });
            }

            var query = result.AsEnumerable();
            if (somenteAtivos)
                query = query.Where(x => x.Ativo);

            return query
                .OrderByDescending(x => x.Ativo)
                .ThenBy(x => x.Ordem)
                .ThenBy(x => x.Titulo)
                .ToList();
        }
    }
}
