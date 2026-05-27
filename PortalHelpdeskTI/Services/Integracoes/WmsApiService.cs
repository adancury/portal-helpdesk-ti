using System.Text;
using System.Text.Json;
using PortalHelpdeskTI.Models.Integracoes;

namespace PortalHelpdeskTI.Services.Integracoes
{
    public class WmsApiService
    {
        private readonly HttpClient _http;

        public WmsApiService(HttpClient http)
        {
            _http = http;
        }

        public async Task<WmsEnviarFaturamentoResponse?> EnviarAsync(WmsEnviarFaturamentoRequest request)
        {
            var url = "https://apiwms.flsoft.com.br/brwims/wms/rest/TSM/enviarFaturamentoOrdemSaida";

            var json = JsonSerializer.Serialize(request);

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync(url, content);

            var retorno = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Erro HTTP {response.StatusCode}: {retorno}");

            return JsonSerializer.Deserialize<WmsEnviarFaturamentoResponse>(retorno,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        public async Task<string> AtualizarIndicadorPedidoVendaAsync(WmsAtualizarIndicadorPedidoVendaRequest request)
        {
            var url = "http://200.195.191.130/api/wms/atualizaIndicadorPedidoVenda";

            var json = JsonSerializer.Serialize(request);

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync(url, content);

            var retorno = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Erro HTTP {response.StatusCode}: {retorno}");

            return retorno;
        }
    }
}
