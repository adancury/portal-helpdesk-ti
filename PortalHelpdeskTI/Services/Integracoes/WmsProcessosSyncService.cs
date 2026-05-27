using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PortalHelpdeskTI.Models.IntegracoesWmsDados;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PortalHelpdeskTI.Services.Integracoes
{
    public class WmsProcessosSyncService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
            PropertyNameCaseInsensitive = true
        };

        private readonly AppDbContext _db;
        private readonly WmsDadosApiClient _client;
        private readonly WmsDadosApiOptions _options;
        private readonly ILogger<WmsProcessosSyncService> _logger;

        public WmsProcessosSyncService(
            AppDbContext db,
            WmsDadosApiClient client,
            IOptions<WmsDadosApiOptions> options,
            ILogger<WmsProcessosSyncService> logger)
        {
            _db = db;
            _client = client;
            _options = options.Value;
            _logger = logger;
        }

        public async Task SincronizarAsync(CancellationToken ct)
        {
            var fim = DateOnly.FromDateTime(DateTime.Today);
            var inicio = fim.AddDays(-Math.Max(0, _options.LookbackDays));
            var tipos = (_options.Tipos?.Length > 0 ? _options.Tipos : WmsDadosEndpoint.Todos.Keys).Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var tipo in tipos)
            {
                if (!WmsDadosEndpoint.Todos.TryGetValue(tipo, out var endpoint))
                    continue;

                await SincronizarTipoAsync(endpoint, inicio, fim, ct);
            }
        }

        public async Task<WmsSyncExecucao> SincronizarTipoAsync(string tipo, DateOnly inicio, DateOnly fim, CancellationToken ct)
        {
            if (!WmsDadosEndpoint.Todos.TryGetValue(tipo, out var endpoint))
                throw new ArgumentException($"Tipo WMS não suportado: {tipo}", nameof(tipo));

            return await SincronizarTipoAsync(endpoint, inicio, fim, ct);
        }

        private async Task<WmsSyncExecucao> SincronizarTipoAsync(WmsDadosEndpoint endpoint, DateOnly inicio, DateOnly fim, CancellationToken ct)
        {
            var exec = new WmsSyncExecucao
            {
                Tipo = endpoint.Tipo,
                InicioEm = DateTime.Now,
                Status = "Executando"
            };

            _db.WmsSyncExecucoes.Add(exec);
            await _db.SaveChangesAsync(ct);

            try
            {
                var rows = await _client.BuscarAsync(endpoint, inicio, fim, ct);
                var novos = 0;
                var alterados = 0;

                foreach (var row in rows)
                {
                    var parsed = Parse(endpoint.Tipo, row);
                    var atual = await _db.WmsProcessos
                        .FirstOrDefaultAsync(x => x.Tipo == parsed.Tipo && x.ChaveItem == parsed.ChaveItem, ct);

                    if (atual == null)
                    {
                        novos++;
                        _db.WmsProcessos.Add(parsed);
                        await _db.SaveChangesAsync(ct);
                        _db.WmsProcessoLogs.Add(CriarLog(parsed, "Criado", null, parsed.Status, null));
                        continue;
                    }

                    var camposAlterados = Diff(atual, parsed);
                    if (camposAlterados.Count == 0 && atual.PayloadHash == parsed.PayloadHash)
                    {
                        atual.UltimaSincronizacaoEm = DateTime.Now;
                        continue;
                    }

                    alterados++;
                    var statusAnterior = atual.Status;
                    Aplicar(atual, parsed);
                    _db.WmsProcessoLogs.Add(CriarLog(atual, "Atualizado", statusAnterior, parsed.Status, camposAlterados));
                }

                exec.RegistrosRecebidos = rows.Count;
                exec.RegistrosNovos = novos;
                exec.RegistrosAlterados = alterados;
                exec.Status = "Sucesso";
                exec.Mensagem = $"Recebidos: {rows.Count}; novos: {novos}; alterados: {alterados}.";
            }
            catch (Exception ex)
            {
                exec.Status = "Erro";
                exec.Mensagem = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                _logger.LogError(ex, "Erro ao sincronizar processos WMS do tipo {Tipo}.", endpoint.Tipo);
            }
            finally
            {
                exec.FimEm = DateTime.Now;
                await _db.SaveChangesAsync(ct);
            }

            return exec;
        }

        private static WmsProcesso Parse(string tipo, JsonElement row)
        {
            var raw = JsonSerializer.Serialize(row, JsonOptions);
            var now = DateTime.Now;

            string keyProcesso;
            string keyItem;

            if (tipo.Equals("SAIDAS", StringComparison.OrdinalIgnoreCase))
            {
                keyProcesso = First(row, "NUM_ORD_SAIDA", "NUM_PEDIDO", "NUM_ATIVIDADE");
                keyItem = JoinKey(keyProcesso, First(row, "NUM_ATIVIDADE"), First(row, "COD_PRODUTO"), First(row, "LOTE"), First(row, "NUM_VOLUME_EXP"));
            }
            else if (tipo.Equals("ENTRADAS", StringComparison.OrdinalIgnoreCase))
            {
                keyProcesso = First(row, "NUM_ORD_ENTR", "NUM_ORD_ENTR_ERP", "NUM_NF");
                keyItem = JoinKey(keyProcesso, First(row, "COD_PRODUTO"), First(row, "COD_BARRAS"));
            }
            else if (tipo.Equals("RESSUPRIMENTOS", StringComparison.OrdinalIgnoreCase))
            {
                keyProcesso = First(row, "NUM_ATIVIDADE");
                keyItem = JoinKey(keyProcesso, First(row, "COD_PRODUTO"), First(row, "ENDER_ORIGEM"), First(row, "ENDER_DESTINO"));
            }
            else if (tipo.Equals("CORTES", StringComparison.OrdinalIgnoreCase))
            {
                keyProcesso = First(row, "NUM_ORD_SAIDA", "NUM_PEDIDO");
                keyItem = JoinKey(keyProcesso, First(row, "COD_PRODUTO"), First(row, "MOTIVO_CORTE"));
            }
            else
            {
                keyProcesso = First(row, "NUM_ATIVIDADE", "COD_ATIVIDADE");
                keyItem = JoinKey(keyProcesso, First(row, "COD_PRODUTO"), First(row, "ENDER_ORIGEM"), First(row, "ENDER_DESTINO"), First(row, "COD_UMA"));
            }

            if (string.IsNullOrWhiteSpace(keyProcesso))
                keyProcesso = ComputeHash(raw)[..16];

            if (string.IsNullOrWhiteSpace(keyItem))
                keyItem = keyProcesso;

            return new WmsProcesso
            {
                Tipo = tipo.ToUpperInvariant(),
                ChaveProcesso = keyProcesso,
                ChaveItem = keyItem,
                Status = First(row, "SITUACAO", "FLAG_SITUACAO"),
                DataReferencia = FirstDate(row, "DT_PRODUCAO", "DATA_ENTRADA", "DT_INICIO_RESSUP", "DT_CORTE", "DT_HR_INICIO", "DATA_PEDIDO"),
                CodProprietario = First(row, "COD_PROPRIET"),
                NomeProprietario = First(row, "NOME_PROPRIET"),
                NumeroDocumento = First(row, "NUM_NF", "NUM_ORD_ENTR_ERP"),
                NumeroPedido = First(row, "NUM_PEDIDO", "NUM_ORD_SAIDA", "NUM_ORD_ENTR"),
                CodigoProduto = First(row, "COD_PRODUTO"),
                DescricaoProduto = First(row, "DESCR_PRODUTO"),
                ClienteFornecedor = First(row, "RAZAO_SOCIAL_CLIENTE", "NOME_EMITENTE", "NOME_FORNECEDOR", "RAZAO_SOCIAL"),
                UsuarioResponsavel = First(row, "COD_USUARIO_SEPARACAO", "COD_USUARIO_FIM_CONF", "COD_USUARIO", "USUARIO"),
                QuantidadePrevista = FirstDecimal(row, "QTD_A_RECEBER", "QTD_PEDIDA", "QTD_SEPARACAO", "QTD_CAIXAS_RESSUP", "QUANTIDADE"),
                QuantidadeExecutada = FirstDecimal(row, "QTD_RECEBIDA", "QTD_ATENDIDA"),
                QuantidadeDivergente = FirstDecimal(row, "QTD_CORTE"),
                PayloadHash = ComputeHash(raw),
                RawJson = raw,
                CriadoEm = now,
                AtualizadoEm = now,
                UltimaSincronizacaoEm = now
            };
        }

        private static void Aplicar(WmsProcesso atual, WmsProcesso novo)
        {
            atual.StatusAnterior = atual.Status;
            atual.Status = novo.Status;
            atual.DataReferencia = novo.DataReferencia;
            atual.CodProprietario = novo.CodProprietario;
            atual.NomeProprietario = novo.NomeProprietario;
            atual.NumeroDocumento = novo.NumeroDocumento;
            atual.NumeroPedido = novo.NumeroPedido;
            atual.CodigoProduto = novo.CodigoProduto;
            atual.DescricaoProduto = novo.DescricaoProduto;
            atual.ClienteFornecedor = novo.ClienteFornecedor;
            atual.UsuarioResponsavel = novo.UsuarioResponsavel;
            atual.QuantidadePrevista = novo.QuantidadePrevista;
            atual.QuantidadeExecutada = novo.QuantidadeExecutada;
            atual.QuantidadeDivergente = novo.QuantidadeDivergente;
            atual.PayloadHash = novo.PayloadHash;
            atual.RawJson = novo.RawJson;
            atual.AtualizadoEm = DateTime.Now;
            atual.UltimaSincronizacaoEm = DateTime.Now;
        }

        private static WmsProcessoLog CriarLog(
            WmsProcesso p,
            string evento,
            string? statusAnterior,
            string? statusNovo,
            Dictionary<string, object?>? campos)
        {
            return new WmsProcessoLog
            {
                WmsProcessoId = p.Id,
                Tipo = p.Tipo,
                ChaveProcesso = p.ChaveProcesso,
                ChaveItem = p.ChaveItem,
                Evento = evento,
                StatusAnterior = statusAnterior,
                StatusNovo = statusNovo,
                CamposAlteradosJson = campos == null || campos.Count == 0 ? null : JsonSerializer.Serialize(campos, JsonOptions),
                RawJson = p.RawJson,
                CriadoEm = DateTime.Now
            };
        }

        private static Dictionary<string, object?> Diff(WmsProcesso atual, WmsProcesso novo)
        {
            var diff = new Dictionary<string, object?>();
            Add("Status", atual.Status, novo.Status);
            Add("DataReferencia", atual.DataReferencia, novo.DataReferencia);
            Add("NumeroDocumento", atual.NumeroDocumento, novo.NumeroDocumento);
            Add("NumeroPedido", atual.NumeroPedido, novo.NumeroPedido);
            Add("CodigoProduto", atual.CodigoProduto, novo.CodigoProduto);
            Add("QuantidadePrevista", atual.QuantidadePrevista, novo.QuantidadePrevista);
            Add("QuantidadeExecutada", atual.QuantidadeExecutada, novo.QuantidadeExecutada);
            Add("QuantidadeDivergente", atual.QuantidadeDivergente, novo.QuantidadeDivergente);
            if (atual.PayloadHash != novo.PayloadHash)
                diff["Payload"] = "Alterado";
            return diff;

            void Add<T>(string name, T oldValue, T newValue)
            {
                if (!EqualityComparer<T>.Default.Equals(oldValue, newValue))
                    diff[name] = new { De = oldValue, Para = newValue };
            }
        }

        private static string First(JsonElement row, params string[] names)
        {
            foreach (var name in names)
            {
                if (row.TryGetProperty(name, out var prop) && prop.ValueKind != JsonValueKind.Null && prop.ValueKind != JsonValueKind.Undefined)
                    return prop.ToString()?.Trim() ?? "";
            }

            return "";
        }

        private static decimal? FirstDecimal(JsonElement row, params string[] names)
        {
            foreach (var name in names)
            {
                if (!row.TryGetProperty(name, out var prop) || prop.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    continue;

                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var n))
                    return n;

                if (decimal.TryParse(prop.ToString(), out var parsed))
                    return parsed;
            }

            return null;
        }

        private static DateTime? FirstDate(JsonElement row, params string[] names)
        {
            foreach (var name in names)
            {
                var raw = First(row, name);
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var formats = new[] { "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy", "MM/dd/yyyy HH:mm:ss", "yyyy-MM-ddTHH:mm:ss" };
                if (DateTime.TryParseExact(raw, formats, null, System.Globalization.DateTimeStyles.AssumeLocal, out var dt))
                    return dt;

                if (DateTime.TryParse(raw, out dt))
                    return dt;
            }

            return null;
        }

        private static string JoinKey(params string[] parts)
        {
            return string.Join("|", parts.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
        }

        private static string ComputeHash(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }
    }
}
