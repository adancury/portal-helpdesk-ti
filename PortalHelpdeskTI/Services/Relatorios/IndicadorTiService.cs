using Microsoft.EntityFrameworkCore;
using PortalHelpdeskTI.Models;
using PortalHelpdeskTI.Models.Relatorios;

namespace PortalHelpdeskTI.Services.Relatorios
{
    public class IndicadorTiService
    {
        private readonly AppDbContext _db;
        private readonly RelatorioTempoService _tempo;

        // manter consistente com RelatorioTempoService
        private static readonly TimeSpan JornadaIni = new(9, 0, 0);
        private static readonly TimeSpan JornadaFim = new(17, 0, 0);

        public IndicadorTiService(AppDbContext db, RelatorioTempoService tempo)
        {
            _db = db;
            _tempo = tempo;
        }

        public async Task<List<IndicadorTiMensalDto>> GerarMensalAsync(DateTime? de, DateTime? ate)
        {
            NormalizarRange(de, ate, out var start, out var end);

            // SLA: por (Categoria,Subcategoria) + fallback por Categoria
            var slas = await _db.SLAConfiguracoes
                .AsNoTracking()
                .Where(x => x.SubcategoriaId != null) // <<< SOMENTE configs por subcategoria
                .ToListAsync();

            var slaPorCatSub = slas.ToDictionary(
                x => (x.CategoriaId, x.SubcategoriaId!.Value),
                x => x
            );


            var slaPorCategoriaFallback = slas
                .Where(x => x.SubcategoriaId == null)
                .ToDictionary(
                    x => x.CategoriaId,
                    x => x
                );


            // Chamados concluídos, prioridade Alta, e que tenham avaliação (regra do indicador)
            var chamados = await _db.Chamados
                .AsNoTracking()
                .Where(c =>
                    c.DataConclusao != null &&
                    c.DataConclusao >= start &&
                    c.DataConclusao <= end &&
                    //c.Prioridade == "Alta" &&
                    _db.AvaliacoesChamado.Any(a => a.ChamadoId == c.Id) &&
                    c.CategoriaId != null &&
                    c.SubcategoriaId != null &&
                    _db.SLAConfiguracoes.Any(s =>
                        s.CategoriaId == c.CategoriaId &&
                        s.SubcategoriaId == c.SubcategoriaId
                    )
                )
                .Select(c => new
                {
                    c.Id,
                    c.CategoriaId,
                    c.SubcategoriaId,   // <<< necessário para SLA por Subcategoria
                    c.DataAbertura,
                    DataConclusao = c.DataConclusao!.Value
                })
                .ToListAsync();

            if (chamados.Count == 0)
                return new List<IndicadorTiMensalDto>();

            // Nota média por chamado (caso exista mais de uma avaliação)
            var notasPorChamado = await _db.AvaliacoesChamado
                .AsNoTracking()
                .Where(a => chamados.Select(x => x.Id).Contains(a.ChamadoId))
                .GroupBy(a => a.ChamadoId)
                .Select(g => new { ChamadoId = g.Key, NotaMedia = g.Average(x => x.Nota) })
                .ToDictionaryAsync(x => x.ChamadoId, x => x.NotaMedia);

            var porMes = new Dictionary<DateTime, (int qtd, double est, double real, double somaNota)>();

            foreach (var c in chamados)
            {
                if (!TryGetSla(c.CategoriaId, c.SubcategoriaId, slaPorCatSub, out var sla))
                    continue;

                var horasEstimadas = (double)sla.TempoResolucaoHoras;

                var horasReais = await _tempo.CalcularHorasUteisConsumidasAsync(
                    c.Id, c.DataAbertura, c.DataConclusao, JornadaIni, JornadaFim);

                if (horasReais <= 0)
                    horasReais = 0.01;

                var mesRef = new DateTime(c.DataConclusao.Year, c.DataConclusao.Month, 1);
                var nota = notasPorChamado.TryGetValue(c.Id, out var nm) ? nm : 0.0;

                porMes.TryGetValue(mesRef, out var acc);
                acc.qtd += 1;
                acc.est += horasEstimadas;
                acc.real += horasReais;
                acc.somaNota += nota;
                porMes[mesRef] = acc;
            }

            var result = new List<IndicadorTiMensalDto>();
            foreach (var kv in porMes.OrderByDescending(x => x.Key))
            {
                var mes = kv.Key;
                var (qtd, est, real, somaNota) = kv.Value;

                var perf = real > 0 ? (est / real) * 100.0 : 0.0;
                var perfCap = Math.Min(110.0, perf);

                var notaMedia = qtd > 0 ? (somaNota / qtd) : 0.0;
                var satPct = (notaMedia / 5.0) * 100.0;

                var indicador = (perfCap * 0.80) + (satPct * 0.20);

                result.Add(new IndicadorTiMensalDto
                {
                    MesRef = mes,
                    QtdeChamados = qtd,
                    HorasEstimadasTotal = Math.Round(est, 2),
                    HorasReaisTotal = Math.Round(real, 2),
                    PerformancePct = Math.Round(perf, 2),
                    PerformancePctCap110 = Math.Round(perfCap, 2),
                    NotaMedia_1a5 = Math.Round(notaMedia, 2),
                    SatisfacaoPct = Math.Round(satPct, 2),
                    IndicadorFinal = Math.Round(indicador, 2)
                });
            }

            return result;
        }

        public async Task<(IndicadorTiMensalDto? consolidado, List<IndicadorTiDetalheDto> detalhes)>
            GerarDetalheDoMesAsync(DateTime mesRef, DateTime? de, DateTime? ate)
        {
            // garante que mesRef é 1º dia do mês
            mesRef = new DateTime(mesRef.Year, mesRef.Month, 1);

            // período do mês
            var mesIni = mesRef;
            var mesFim = mesRef.AddMonths(1).AddTicks(-1);

            // aplica range do filtro da tela (se o usuário filtrou)
            NormalizarRange(de, ate, out var start, out var end);
            var ini = mesIni < start ? start : mesIni;
            var fim = mesFim > end ? end : mesFim;

            // SLA: por (Categoria,Subcategoria) + fallback por Categoria
            var slas = await _db.SLAConfiguracoes
                .AsNoTracking()
                .ToListAsync();

            var slaPorCatSub = slas
                .Where(x => x.SubcategoriaId != null)
                .ToDictionary(x => (x.CategoriaId, x.SubcategoriaId!.Value), x => x);


            var slaPorCategoriaFallback = slas
                .Where(x => x.SubcategoriaId == null)
                .ToDictionary(x => x.CategoriaId, x => x);



            // busca chamados do mês (concluídos), prioridade Alta, e com avaliação
            var chamados = await _db.Chamados
                .AsNoTracking()
                .Include(c => c.Usuario)
                .Include(c => c.Tecnico)
                .Include(c => c.Categoria)
                .Include(c => c.Subcategoria)
                .Where(c =>
                    c.DataConclusao != null &&
                    c.DataConclusao >= ini &&
                    c.DataConclusao <= fim &&
                    //c.Prioridade == "Alta" &&
                    _db.AvaliacoesChamado.Any(a => a.ChamadoId == c.Id) &&
                    c.CategoriaId != null &&
                    c.SubcategoriaId != null &&
                    _db.SLAConfiguracoes.Any(s =>
                        s.CategoriaId == c.CategoriaId &&
                        s.SubcategoriaId == c.SubcategoriaId
                    )
                )
                .OrderByDescending(c => c.DataConclusao)
                .ToListAsync();

            if (chamados.Count == 0)
                return (null, new List<IndicadorTiDetalheDto>());

            // avaliação: pega a última (por DataAvaliacao) para comentário; nota média para indicador
            var avals = await _db.AvaliacoesChamado
                .AsNoTracking()
                .Where(a => chamados.Select(x => x.Id).Contains(a.ChamadoId))
                .GroupBy(a => a.ChamadoId)
                .Select(g => new
                {
                    ChamadoId = g.Key,
                    NotaMedia = g.Average(x => x.Nota),
                    UltimoComentario = g.OrderByDescending(x => x.DataAvaliacao).Select(x => x.Comentario).FirstOrDefault()
                })
                .ToDictionaryAsync(x => x.ChamadoId);

            var detalhes = new List<IndicadorTiDetalheDto>();

            double totalEst = 0, totalReal = 0, somaNotas = 0;
            int qtd = 0;

            foreach (var c in chamados)
            {
                if (!TryGetSla(c.CategoriaId, c.SubcategoriaId, slaPorCatSub, out var sla))
                    continue;

                var horasEstimadas = (double)sla.TempoResolucaoHoras;

                var horasReais = await _tempo.CalcularHorasUteisConsumidasAsync(
                    c.Id, c.DataAbertura, c.DataConclusao!.Value, JornadaIni, JornadaFim);

                if (horasReais <= 0)
                    horasReais = 0.01;

                avals.TryGetValue(c.Id, out var av);
                var notaMedia = av?.NotaMedia ?? 0.0;
                var satPct = (notaMedia / 5.0) * 100.0;

                var perf = (horasEstimadas / horasReais) * 100.0;
                var perfCap = Math.Min(110.0, perf);

                detalhes.Add(new IndicadorTiDetalheDto
                {
                    ChamadoId = c.Id,
                    Titulo = c.Titulo,
                    Solicitante = c.Usuario?.Nome,
                    Tecnico = c.Tecnico?.Nome,
                    Categoria = c.Categoria?.Nome,
                    Subcategoria = c.Subcategoria?.Nome,
                    DataAbertura = c.DataAbertura,
                    DataConclusao = c.DataConclusao!.Value,
                    HorasEstimadas = Math.Round(horasEstimadas, 2),
                    HorasReais = Math.Round(horasReais, 2),
                    PerformancePct = Math.Round(perf, 2),
                    PerformancePctCap110 = Math.Round(perfCap, 2),
                    Nota_1a5 = Math.Round(notaMedia, 2),
                    SatisfacaoPct = Math.Round(satPct, 2),
                    ComentarioAvaliacao = av?.UltimoComentario
                });

                qtd++;
                totalEst += horasEstimadas;
                totalReal += horasReais;
                somaNotas += notaMedia;
            }

            if (qtd == 0 || totalReal <= 0)
                return (null, detalhes);

            var perfMes = (totalEst / totalReal) * 100.0;
            var perfMesCap = Math.Min(110.0, perfMes);

            var notaMes = somaNotas / qtd;
            var satMesPct = (notaMes / 5.0) * 100.0;

            var indicadorMes = (perfMesCap * 0.80) + (satMesPct * 0.20);

            var consolidado = new IndicadorTiMensalDto
            {
                MesRef = mesRef,
                QtdeChamados = qtd,
                HorasEstimadasTotal = Math.Round(totalEst, 2),
                HorasReaisTotal = Math.Round(totalReal, 2),
                PerformancePct = Math.Round(perfMes, 2),
                PerformancePctCap110 = Math.Round(perfMesCap, 2),
                NotaMedia_1a5 = Math.Round(notaMes, 2),
                SatisfacaoPct = Math.Round(satMesPct, 2),
                IndicadorFinal = Math.Round(indicadorMes, 2)
            };

            return (consolidado, detalhes);
        }

        private static bool TryGetSla(
    int? categoriaId,
    int? subcategoriaId,
    Dictionary<(int CategoriaId, int SubcategoriaId), SLAConfiguracao> slaPorCatSub,
    out SLAConfiguracao sla)
        {
            sla = null!;
            if (!categoriaId.HasValue || !subcategoriaId.HasValue)
                return false;

            return slaPorCatSub.TryGetValue((categoriaId.Value, subcategoriaId.Value), out sla!);
        }



        private static void NormalizarRange(DateTime? de, DateTime? ate, out DateTime start, out DateTime end)
        {
            if (de.HasValue && ate.HasValue && de.Value.Date > ate.Value.Date)
            {
                var tmp = de.Value;
                de = ate;
                ate = tmp;
            }

            start = de?.Date ?? DateTime.MinValue.Date;

            if (ate.HasValue)
            {
                var ateDate = ate.Value.Date;
                end = ateDate.AddDays(1).AddTicks(-1);
            }
            else
            {
                end = DateTime.MaxValue;
            }
        }
    }
}
