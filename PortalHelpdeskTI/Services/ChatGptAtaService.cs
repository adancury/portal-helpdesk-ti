using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PortalHelpdeskTI.Services;

public class ChatGptAtaService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ChatGptOptions _options;

    public ChatGptAtaService(IHttpClientFactory httpClientFactory, IOptions<ChatGptOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<string> GerarMinutesAsync(string transcricao, string descricao, string titulo, IEnumerable<string> participantes)
    {
        var prompt = $@"
Você é um secretário de reuniões corporativas.
Com base na transcrição abaixo, gere uma ata de reunião formal, clara e bem estruturada.

A ata deve conter:
- Título: {titulo}
- Descrição breve: {descricao}
- Lista de Participantes: {string.Join(", ", participantes)}
- Data e horário aproximado (com base na introdução do conteúdo, se possível)
- Principais assuntos discutidos
- Decisões tomadas
- Tarefas atribuídas com responsáveis (se mencionadas)

Transcrição:
{transcricao}
";

        var requestBody = new
        {
            model = _options.Model,
            messages = new[]
            {
                new { role = "system", content = "Você é um assistente que elabora atas de reuniões formais e bem organizadas." },
                new { role = "user", content = prompt }
            },
            temperature = 0.5
        };

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await client.PostAsync(_options.Endpoint, content);

            if (!response.IsSuccessStatusCode)
            {
                var erro = await response.Content.ReadAsStringAsync();
                throw new Exception($"Erro da OpenAI: {response.StatusCode} - {erro}");
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var reply = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

            return reply;
        }
        catch (Exception ex)
        {
            Console.WriteLine("❌ ERRO OpenAI (Ata):\n" + ex);
            throw;
        }
    }
}
