using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PortalHelpdeskTI.ViewModels.IntegracoesWms;
using System.Text.Json;

namespace PortalHelpdeskTI.Services.Integracoes
{
    public class WmsProcessosQueryService
    {
        private readonly AppDbContext _db;
        private readonly WmsDadosApiOptions _options;

        public WmsProcessosQueryService(AppDbContext db, IOptions<WmsDadosApiOptions> options)
        {
            _db = db;
            _options = options.Value;
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

            var exibirAgrupado = true;
            var total = await query
                .GroupBy(x => new { x.Tipo, x.ChaveProcesso })
                .CountAsync(ct);

            var itens = new List<Models.IntegracoesWmsDados.WmsProcesso>();
            var grupos = new List<WmsProcessosGrupoVm>();

            var gruposQuery = query
                .GroupBy(x => new { x.Tipo, x.ChaveProcesso })
                .Select(g => new WmsProcessoGrupoResumo
                {
                    Tipo = g.Key.Tipo,
                    ChaveProcesso = g.Key.ChaveProcesso,
                    Status = g.Min(x => x.Status),
                    DataReferencia = g.Max(x => x.DataReferencia),
                    NumeroPedido = g.Max(x => x.NumeroPedido),
                    NumeroDocumento = g.Max(x => x.NumeroDocumento),
                    TotalItens = g.Count(),
                    QuantidadePrevista = g.Sum(x => x.QuantidadePrevista ?? 0),
                    AtualizadoEm = g.Max(x => x.AtualizadoEm)
                });

            gruposQuery = OrdenarGrupos(gruposQuery, filtro);

            var gruposPagina = await gruposQuery
                .Skip((filtro.Page - 1) * filtro.PageSize)
                .Take(filtro.PageSize)
                .ToListAsync(ct);

            var chaves = gruposPagina.Select(x => x.ChaveProcesso).ToList();
            itens = chaves.Count == 0
                ? new()
                : await query
                    .Where(x => chaves.Contains(x.ChaveProcesso))
                    .OrderBy(x => x.Tipo)
                    .ThenBy(x => x.ChaveProcesso)
                    .ThenBy(x => x.CodigoProduto)
                    .ThenBy(x => x.ChaveItem)
                    .ToListAsync(ct);

            grupos = gruposPagina
                .Select(g => CriarGrupo(g.Tipo, g.ChaveProcesso, itens.Where(x => x.Tipo == g.Tipo && x.ChaveProcesso == g.ChaveProcesso)))
                .ToList();

            var desdeHoje = DateTime.Today;
            var desde24h = DateTime.Now.AddHours(-24);

            var execucoesRecentes = await _db.WmsSyncExecucoes
                .AsNoTracking()
                .OrderByDescending(x => x.InicioEm)
                .ThenByDescending(x => x.Id)
                .Take(200)
                .ToListAsync(ct);

            foreach (var execucao in execucoesRecentes.Where(x => ErroConhecidoConsultaCortes(x.Tipo, x.Mensagem)))
            {
                execucao.Status = "Ignorado";
                execucao.Mensagem = "Endpoint /consultaCortes indisponivel na API WMS: MOTIVO_CORTE nao existe na consulta Oracle remota.";
            }

            var ultimasExecucoes = execucoesRecentes
                .GroupBy(x => x.Tipo)
                .Select(g => g.First())
                .OrderByDescending(x => x.InicioEm)
                .ThenByDescending(x => x.Id)
                .ToList();

            var tiposConfigurados = (_options.Tipos?.Length > 0 ? _options.Tipos : WmsDadosEndpoint.Todos.Keys)
                .Where(x => WmsDadosEndpoint.Todos.ContainsKey(x))
                .Select(x => x.ToUpperInvariant());
            var tiposComDados = await _db.WmsProcessos.AsNoTracking().Select(x => x.Tipo).Distinct().ToListAsync(ct);

            return new WmsProcessosIndexVm
            {
                Filtro = filtro,
                Itens = itens,
                Grupos = grupos,
                ExibirAgrupado = exibirAgrupado,
                Total = total,
                Tipos = tiposConfigurados.Concat(tiposComDados).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
                Status = await _db.WmsProcessos.AsNoTracking().Where(x => x.Status != null && x.Status != "").Select(x => x.Status!).Distinct().OrderBy(x => x).ToListAsync(ct),
                UltimasExecucoes = ultimasExecucoes,
                TotalAbertos = await _db.WmsProcessos.AsNoTracking().CountAsync(x => x.Status != null && x.Status != "FIM" && x.Status != "ARMAZENADO", ct),
                TotalHoje = await _db.WmsProcessos.AsNoTracking().CountAsync(x => x.DataReferencia != null && x.DataReferencia.Value.Date >= desdeHoje, ct),
                TotalAlterados24h = await _db.WmsProcessoLogs.AsNoTracking().CountAsync(x => x.CriadoEm >= desde24h && x.Evento == "Atualizado", ct),
                TotalErrosSync24h = await _db.WmsSyncExecucoes.AsNoTracking().CountAsync(x =>
                    x.InicioEm >= desde24h
                    && x.Status == "Erro"
                    && !(x.Tipo == "CORTES"
                        && x.Mensagem != null
                        && x.Mensagem.Contains("ORA-00904")
                        && x.Mensagem.Contains("MOTIVO_CORTE")), ct)
            };
        }

        private static WmsProcessosGrupoVm CriarGrupo(string tipo, string chaveProcesso, IEnumerable<Models.IntegracoesWmsDados.WmsProcesso> itens)
        {
            var lista = itens.ToList();
            var primeiro = lista
                .OrderByDescending(x => x.DataReferencia ?? x.AtualizadoEm)
                .ThenByDescending(x => x.AtualizadoEm)
                .FirstOrDefault();
            var classificacao = IdentificarMovimentacao(tipo, primeiro?.RawJson);
            var statusResumo = lista
                .GroupBy(
                    x => string.IsNullOrWhiteSpace(x.Status) ? "PENDENTE" : x.Status.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .Select(g => new WmsProcessosStatusResumoVm
                {
                    Status = g.Key.ToUpperInvariant(),
                    Descricao = DescreverStatus(g.Key),
                    Quantidade = g.Count()
                })
                .OrderBy(x => OrdemStatus(x.Status))
                .ThenBy(x => x.Descricao)
                .ToList();

            return new WmsProcessosGrupoVm
            {
                Tipo = tipo,
                ChaveProcesso = chaveProcesso,
                Status = statusResumo.Count == 0 ? null : statusResumo.Count == 1 ? statusResumo[0].Status : "MISTO",
                DataReferencia = lista.Max(x => x.DataReferencia),
                NumeroPedido = primeiro?.NumeroPedido,
                NumeroDocumento = primeiro?.NumeroDocumento,
                TipoMovimentacao = classificacao.Movimentacao,
                TipoDocumento = classificacao.Documento,
                TotalItens = lista.Count,
                QuantidadePrevista = lista.Sum(x => x.QuantidadePrevista ?? 0),
                QuantidadeExecutada = lista.Sum(x => x.QuantidadeExecutada ?? 0),
                QuantidadeDivergente = lista.Sum(x => x.QuantidadeDivergente ?? 0),
                AtualizadoEm = lista.Count == 0 ? DateTime.MinValue : lista.Max(x => x.AtualizadoEm),
                StatusResumo = statusResumo,
                Itens = lista
            };
        }

        private static string DescreverStatus(string status)
        {
            return status.Trim().ToUpperInvariant() switch
            {
                "LIB" => "Liberado",
                "FIM" => "Finalizado",
                "BLQ" => "Bloqueado",
                "PEND" or "PENDENTE" => "Pendente",
                "ARMAZENADO" => "Armazenado",
                _ => status.Trim()
            };
        }

        private static int OrdemStatus(string status)
        {
            return status.ToUpperInvariant() switch
            {
                "LIB" => 0,
                "PEND" or "PENDENTE" => 1,
                "BLQ" => 2,
                "FIM" => 3,
                "ARMAZENADO" => 4,
                _ => 5
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
            var classificacao = IdentificarMovimentacao(processo.Tipo, processo.RawJson);

            return new WmsProcessoDetalheVm
            {
                Processo = processo,
                Logs = logs,
                TipoMovimentacao = classificacao.Movimentacao,
                TipoDocumento = classificacao.Documento
            };
        }

        private static (string Movimentacao, string Documento) IdentificarMovimentacao(string tipo, string? rawJson)
        {
            if (tipo.Equals("ENTRADAS", StringComparison.OrdinalIgnoreCase))
            {
                var flagDevolucao = ObterCampoJson(rawJson, "FLAG_DEVOL");
                return flagDevolucao.Equals("S", StringComparison.OrdinalIgnoreCase)
                    ? ("Devolução de venda", "NF de devolução de saída")
                    : flagDevolucao.Equals("N", StringComparison.OrdinalIgnoreCase)
                        ? ("Entrada de compra", "NF de entrada")
                        : ("Entrada", "Tipo não informado");
            }

            if (tipo.Equals("SAIDAS", StringComparison.OrdinalIgnoreCase))
                return ("Saída", "NF de saída");

            return tipo.ToUpperInvariant() switch
            {
                "RESSUPRIMENTOS" => ("Movimentação interna", "Sem documento fiscal"),
                "ATIVIDADES" => ("Atividade interna", "Sem documento fiscal"),
                "ESTOQUE" => ("Posição de estoque", "Sem documento fiscal"),
                "CORTES" => ("Corte de saída", "Referência da NF de saída"),
                _ => ("Movimentação WMS", "Não identificado")
            };
        }

        private static string ObterCampoJson(string? rawJson, string campo)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
                return "";

            try
            {
                using var json = JsonDocument.Parse(rawJson);
                return json.RootElement.TryGetProperty(campo, out var valor)
                    ? valor.ToString().Trim()
                    : "";
            }
            catch (JsonException)
            {
                return "";
            }
        }

        private static void Normalizar(WmsProcessosFiltroVm f)
        {
            if (f.Page < 1) f.Page = 1;
            if (f.PageSize <= 0) f.PageSize = 50;
            if (f.PageSize > 200) f.PageSize = 200;
            f.Tipo = string.IsNullOrWhiteSpace(f.Tipo) ? null : f.Tipo.Trim().ToUpperInvariant();
            f.Status = string.IsNullOrWhiteSpace(f.Status) ? null : f.Status.Trim();
            f.Texto = string.IsNullOrWhiteSpace(f.Texto) ? null : f.Texto.Trim();
            f.OrdenarPor = f.OrdenarPor?.Trim().ToLowerInvariant() switch
            {
                "tipo" => "tipo",
                "processo" => "processo",
                "status" => "status",
                "referencia" => "referencia",
                "pedido" => "pedido",
                "itens" => "itens",
                "quantidade" => "quantidade",
                "atualizado" => "atualizado",
                _ => "referencia"
            };
            f.Direcao = string.Equals(f.Direcao, "asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc";
        }

        private static IQueryable<WmsProcessoGrupoResumo> OrdenarGrupos(
            IQueryable<WmsProcessoGrupoResumo> query,
            WmsProcessosFiltroVm filtro)
        {
            var asc = filtro.Direcao == "asc";

            IOrderedQueryable<WmsProcessoGrupoResumo> ordenada = filtro.OrdenarPor switch
            {
                "tipo" => asc ? query.OrderBy(x => x.Tipo) : query.OrderByDescending(x => x.Tipo),
                "processo" => asc ? query.OrderBy(x => x.ChaveProcesso) : query.OrderByDescending(x => x.ChaveProcesso),
                "status" => asc ? query.OrderBy(x => x.Status) : query.OrderByDescending(x => x.Status),
                "pedido" => asc
                    ? query.OrderBy(x => x.NumeroPedido).ThenBy(x => x.NumeroDocumento)
                    : query.OrderByDescending(x => x.NumeroPedido).ThenByDescending(x => x.NumeroDocumento),
                "itens" => asc ? query.OrderBy(x => x.TotalItens) : query.OrderByDescending(x => x.TotalItens),
                "quantidade" => asc ? query.OrderBy(x => x.QuantidadePrevista) : query.OrderByDescending(x => x.QuantidadePrevista),
                "atualizado" => asc ? query.OrderBy(x => x.AtualizadoEm) : query.OrderByDescending(x => x.AtualizadoEm),
                _ => asc
                    ? query.OrderBy(x => x.DataReferencia ?? x.AtualizadoEm)
                    : query.OrderByDescending(x => x.DataReferencia ?? x.AtualizadoEm)
            };

            return ordenada
                .ThenByDescending(x => x.AtualizadoEm)
                .ThenBy(x => x.Tipo)
                .ThenBy(x => x.ChaveProcesso);
        }

        private static bool ErroConhecidoConsultaCortes(string tipo, string? mensagem)
        {
            return tipo.Equals("CORTES", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(mensagem)
                && mensagem.Contains("ORA-00904", StringComparison.OrdinalIgnoreCase)
                && mensagem.Contains("MOTIVO_CORTE", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class WmsProcessoGrupoResumo
        {
            public string Tipo { get; set; } = "";
            public string ChaveProcesso { get; set; } = "";
            public string? Status { get; set; }
            public DateTime? DataReferencia { get; set; }
            public string? NumeroPedido { get; set; }
            public string? NumeroDocumento { get; set; }
            public int TotalItens { get; set; }
            public decimal QuantidadePrevista { get; set; }
            public DateTime AtualizadoEm { get; set; }
        }
    }
}
