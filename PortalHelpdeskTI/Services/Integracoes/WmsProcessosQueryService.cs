using Microsoft.EntityFrameworkCore;
using PortalHelpdeskTI.ViewModels.IntegracoesWms;

namespace PortalHelpdeskTI.Services.Integracoes
{
    public class WmsProcessosQueryService
    {
        private readonly AppDbContext _db;

        public WmsProcessosQueryService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<WmsProcessosIndexVm> BuscarAsync(WmsProcessosFiltroVm filtro, CancellationToken ct)
        {
            Normalizar(filtro);

            var query = _db.WmsProcessos.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(filtro.Tipo))
                query = query.Where(x => x.Tipo == filtro.Tipo);

            if (!string.IsNullOrWhiteSpace(filtro.Status))
                query = query.Where(x => x.Status == filtro.Status);

            if (filtro.DataIni.HasValue)
                query = query.Where(x => x.DataReferencia == null || x.DataReferencia.Value.Date >= filtro.DataIni.Value.Date);

            if (filtro.DataFim.HasValue)
            {
                var fim = filtro.DataFim.Value.Date.AddDays(1);
                query = query.Where(x => x.DataReferencia == null || x.DataReferencia < fim);
            }

            if (!string.IsNullOrWhiteSpace(filtro.Texto))
            {
                var texto = filtro.Texto.Trim();
                query = query.Where(x =>
                    x.ChaveProcesso.Contains(texto) ||
                    x.ChaveItem.Contains(texto) ||
                    (x.NumeroPedido != null && x.NumeroPedido.Contains(texto)) ||
                    (x.NumeroDocumento != null && x.NumeroDocumento.Contains(texto)) ||
                    (x.CodigoProduto != null && x.CodigoProduto.Contains(texto)) ||
                    (x.DescricaoProduto != null && x.DescricaoProduto.Contains(texto)) ||
                    (x.ClienteFornecedor != null && x.ClienteFornecedor.Contains(texto)));
            }

            var exibirAgrupado = string.Equals(filtro.Tipo, "SAIDAS", StringComparison.OrdinalIgnoreCase);
            var total = exibirAgrupado
                ? await query.Select(x => x.ChaveProcesso).Distinct().CountAsync(ct)
                : await query.CountAsync(ct);

            var itens = new List<Models.IntegracoesWmsDados.WmsProcesso>();
            var grupos = new List<WmsProcessosGrupoVm>();

            if (exibirAgrupado)
            {
                var gruposPagina = await query
                    .GroupBy(x => new { x.Tipo, x.ChaveProcesso })
                    .Select(g => new
                    {
                        g.Key.Tipo,
                        g.Key.ChaveProcesso,
                        DataReferencia = g.Max(x => x.DataReferencia),
                        AtualizadoEm = g.Max(x => x.AtualizadoEm)
                    })
                    .OrderByDescending(x => x.DataReferencia ?? x.AtualizadoEm)
                    .ThenByDescending(x => x.AtualizadoEm)
                    .Skip((filtro.Page - 1) * filtro.PageSize)
                    .Take(filtro.PageSize)
                    .ToListAsync(ct);

                var chaves = gruposPagina.Select(x => x.ChaveProcesso).ToList();
                itens = chaves.Count == 0
                    ? new()
                    : await query
                        .Where(x => chaves.Contains(x.ChaveProcesso))
                        .OrderBy(x => x.ChaveProcesso)
                        .ThenBy(x => x.CodigoProduto)
                        .ThenBy(x => x.ChaveItem)
                        .ToListAsync(ct);

                grupos = gruposPagina
                    .Select(g => CriarGrupo(g.Tipo, g.ChaveProcesso, itens.Where(x => x.Tipo == g.Tipo && x.ChaveProcesso == g.ChaveProcesso)))
                    .ToList();
            }
            else
            {
                itens = await query
                    .OrderByDescending(x => x.DataReferencia ?? x.AtualizadoEm)
                    .ThenByDescending(x => x.AtualizadoEm)
                    .Skip((filtro.Page - 1) * filtro.PageSize)
                    .Take(filtro.PageSize)
                    .ToListAsync(ct);
            }

            var desdeHoje = DateTime.Today;
            var desde24h = DateTime.Now.AddHours(-24);

            var execucoesRecentes = await _db.WmsSyncExecucoes
                .AsNoTracking()
                .OrderByDescending(x => x.InicioEm)
                .ThenByDescending(x => x.Id)
                .Take(200)
                .ToListAsync(ct);

            var ultimasExecucoes = execucoesRecentes
                .GroupBy(x => x.Tipo)
                .Select(g => g.First())
                .OrderByDescending(x => x.InicioEm)
                .ThenByDescending(x => x.Id)
                .ToList();

            return new WmsProcessosIndexVm
            {
                Filtro = filtro,
                Itens = itens,
                Grupos = grupos,
                ExibirAgrupado = exibirAgrupado,
                Total = total,
                Tipos = await _db.WmsProcessos.AsNoTracking().Select(x => x.Tipo).Distinct().OrderBy(x => x).ToListAsync(ct),
                Status = await _db.WmsProcessos.AsNoTracking().Where(x => x.Status != null && x.Status != "").Select(x => x.Status!).Distinct().OrderBy(x => x).ToListAsync(ct),
                UltimasExecucoes = ultimasExecucoes,
                TotalAbertos = await _db.WmsProcessos.AsNoTracking().CountAsync(x => x.Status != null && x.Status != "FIM" && x.Status != "ARMAZENADO", ct),
                TotalHoje = await _db.WmsProcessos.AsNoTracking().CountAsync(x => x.DataReferencia != null && x.DataReferencia.Value.Date >= desdeHoje, ct),
                TotalAlterados24h = await _db.WmsProcessoLogs.AsNoTracking().CountAsync(x => x.CriadoEm >= desde24h && x.Evento == "Atualizado", ct),
                TotalErrosSync24h = await _db.WmsSyncExecucoes.AsNoTracking().CountAsync(x => x.InicioEm >= desde24h && x.Status == "Erro", ct)
            };
        }

        private static WmsProcessosGrupoVm CriarGrupo(string tipo, string chaveProcesso, IEnumerable<Models.IntegracoesWmsDados.WmsProcesso> itens)
        {
            var lista = itens.ToList();
            var primeiro = lista
                .OrderByDescending(x => x.DataReferencia ?? x.AtualizadoEm)
                .ThenByDescending(x => x.AtualizadoEm)
                .FirstOrDefault();
            var status = lista
                .Select(x => x.Status)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new WmsProcessosGrupoVm
            {
                Tipo = tipo,
                ChaveProcesso = chaveProcesso,
                Status = status.Count == 0 ? null : status.Count == 1 ? status[0] : "MISTO",
                DataReferencia = lista.Max(x => x.DataReferencia),
                NumeroPedido = primeiro?.NumeroPedido,
                NumeroDocumento = primeiro?.NumeroDocumento,
                TotalItens = lista.Count,
                QuantidadePrevista = lista.Sum(x => x.QuantidadePrevista ?? 0),
                QuantidadeExecutada = lista.Sum(x => x.QuantidadeExecutada ?? 0),
                QuantidadeDivergente = lista.Sum(x => x.QuantidadeDivergente ?? 0),
                AtualizadoEm = lista.Count == 0 ? DateTime.MinValue : lista.Max(x => x.AtualizadoEm),
                Itens = lista
            };
        }

        public async Task<WmsProcessoDetalheVm?> DetalheAsync(int id, CancellationToken ct)
        {
            var processo = await _db.WmsProcessos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (processo == null)
                return null;

            var logs = await _db.WmsProcessoLogs
                .AsNoTracking()
                .Where(x => x.WmsProcessoId == id)
                .OrderByDescending(x => x.CriadoEm)
                .ToListAsync(ct);

            return new WmsProcessoDetalheVm
            {
                Processo = processo,
                Logs = logs
            };
        }

        private static void Normalizar(WmsProcessosFiltroVm f)
        {
            if (f.Page < 1) f.Page = 1;
            if (f.PageSize <= 0) f.PageSize = 50;
            if (f.PageSize > 200) f.PageSize = 200;
            f.Tipo = string.IsNullOrWhiteSpace(f.Tipo) ? null : f.Tipo.Trim().ToUpperInvariant();
            f.Status = string.IsNullOrWhiteSpace(f.Status) ? null : f.Status.Trim();
            f.Texto = string.IsNullOrWhiteSpace(f.Texto) ? null : f.Texto.Trim();
        }
    }
}
