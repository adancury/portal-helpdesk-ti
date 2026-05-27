using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PortalHelpdeskTI.Services.Integracoes
{
    public class WmsDadosApiClient
    {
        private readonly HttpClient _http;
        private readonly WmsDadosApiOptions _options;

        public WmsDadosApiClient(HttpClient http, IOptions<WmsDadosApiOptions> options)
        {
            _http = http;
            _options = options.Value;
        }

        public async Task<List<JsonElement>> BuscarAsync(WmsDadosEndpoint endpoint, DateOnly inicio, DateOnly fim, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_options.BaseUrl))
                throw new InvalidOperationException("Configure WmsDadosApi:BaseUrl para sincronizar os processos WMS.");

            var query = endpoint.BuildQuery(_options.CodProprietario, inicio, fim);
            var uri = new Uri(new Uri(_options.BaseUrl.TrimEnd('/') + "/"), endpoint.Path.TrimStart('/') + query);

            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (!string.IsNullOrWhiteSpace(_options.Token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);

            using var resp = await _http.SendAsync(req, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"WMS Dados HTTP {(int)resp.StatusCode}: {raw}");

            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException($"Resposta inesperada da API WMS em {endpoint.Path}: era esperado um array JSON.");

            return doc.RootElement.EnumerateArray().Select(x => x.Clone()).ToList();
        }
    }

    public sealed class WmsDadosEndpoint
    {
        public string Tipo { get; init; } = "";
        public string Path { get; init; } = "";
        public string InicioParam { get; init; } = "dataInicio";
        public string FimParam { get; init; } = "dataFim";

        public string BuildQuery(string codProprietario, DateOnly inicio, DateOnly fim)
        {
            static string D(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

            var qs = new List<string>();
            if (!string.IsNullOrWhiteSpace(codProprietario))
                qs.Add("codProprietario=" + Uri.EscapeDataString(codProprietario));

            qs.Add(InicioParam + "=" + Uri.EscapeDataString(D(inicio)));
            qs.Add(FimParam + "=" + Uri.EscapeDataString(D(fim)));

            return "?" + string.Join("&", qs);
        }

        public static IReadOnlyDictionary<string, WmsDadosEndpoint> Todos { get; } =
            new Dictionary<string, WmsDadosEndpoint>(StringComparer.OrdinalIgnoreCase)
            {
                ["ENTRADAS"] = new() { Tipo = "ENTRADAS", Path = "/consultaEntradas" },
                ["SAIDAS"] = new() { Tipo = "SAIDAS", Path = "/consultaSaidas", InicioParam = "dataInicioProducao", FimParam = "dataFimProducao" },
                ["RESSUPRIMENTOS"] = new() { Tipo = "RESSUPRIMENTOS", Path = "/consultaRessuprimento" },
                ["CORTES"] = new() { Tipo = "CORTES", Path = "/consultaCortes" },
                ["ATIVIDADES"] = new() { Tipo = "ATIVIDADES", Path = "/consultaAtividades" }
            };
    }
}
