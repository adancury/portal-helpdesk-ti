using Microsoft.AspNetCore.Mvc;
using PortalHelpdeskTI.Models;
using PortalHelpdeskTI.Services;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PortalHelpdeskTI.Controllers
{
    public class AjudaController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly AppDbContext _context;
        private readonly IEmailService _emailService;

        public AjudaController(IHttpClientFactory httpClientFactory, IConfiguration configuration, AppDbContext context, IEmailService emailService)                  // <-- injete aqui
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _context = context;
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        }
        [HttpPost]
        public async Task<IActionResult> SugerirIA([FromBody] TextoIAInput input)
        {
            try
            {
                string texto = input.Texto?.ToLower() ?? "";

                var textoLimpo = RemoverPontuacao(input.Texto ?? "").ToLower();

                var palavrasTexto = textoLimpo
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim().ToLower())
                    .Where(p => !Stopwords.Contains(p))
                    .Select(p => Stem(p))
                    .ToList();

                var sugestoesLocal = _context.BaseConhecimentoIA
                    .AsEnumerable()
                    .Where(b =>
                    {
                        var palavrasChave = RemoverPontuacao(b.PalavraChave ?? "").ToLower()
                            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .Select(p => p.Trim())
                            .Where(p => !Stopwords.Contains(p))
                            .Select(p => Stem(p))
                            .ToList();

                        // Considera relevante se houver ao menos 2 palavras iguais
                        return palavrasTexto.Intersect(palavrasChave).Count() >= 2;
                    })
                    .Select(b => b.Sugestao)
                    .Distinct()
                    .ToList();



                if (sugestoesLocal.Any())
                {
                    return Json(new { sugestoes = sugestoesLocal });
                }

                // 🔍 Palavras-chave dinâmicas da base
                var palavrasChaveHelpdesk = _context.PalavrasChaveIA
                    .Select(p => p.Termo.ToLower())
                    .ToList();

                bool textoTemRelevancia = palavrasChaveHelpdesk.Any(p => texto.Contains(p));
                if (!textoTemRelevancia)
                {
                    return Json(new { sugestoes = new List<string>() });
                }

                var sugestoesGPT = await ObterSugestoesViaChatGPT(input.Texto);

                foreach (var sugestao in sugestoesGPT)
                {
                    bool jaExiste = _context.BaseConhecimentoIA.Any(b =>
                        b.PalavraChave.ToLower() == texto && b.Sugestao.ToLower() == sugestao.ToLower());

                    if (!jaExiste)
                    {
                        _context.BaseConhecimentoIA.Add(new BaseConhecimentoIA
                        {
                            PalavraChave = texto,
                            Sugestao = sugestao
                        });
                    }
                }

                await _context.SaveChangesAsync();

                return Json(new { sugestoes = sugestoesGPT });
            }
            catch (Exception ex)
            {
                Console.WriteLine("Erro interno na IA: " + ex);

                try
                {
                    await NotificarErroParaTecnicosAsync(ex.Message, input.Texto ?? "");
                }
                catch (Exception emailEx)
                {
                    Console.WriteLine("Erro ao tentar notificar técnicos: " + emailEx.Message);
                }

                // Retorna mensagem de erro genérica para o frontend
                return Json(new
                {
                    sugestoes = new List<string>(),
                    erro = "Não foi possível gerar sugestões automáticas no momento. Tente novamente mais tarde."
                });
            }

        }
        private async Task<List<string>> ObterSugestoesViaChatGPT(string texto)
        {
            try
            {
                var prompt = $"Usuário descreveu: \"{texto}\". Sugira até 3 ações simples que ele possa tentar para resolver o problema sozinho, como se você fosse um técnico de TI experiente.";

                var requestBody = new
                {
                    model = "gpt-3.5-turbo", // Troque para gpt-3.5 para testar com menos restrição
                    messages = new[]
                    {
                new { role = "system", content = "Você é um assistente técnico experiente, objetivo e direto." },
                new { role = "user", content = prompt }
            },
                    temperature = 0.6
                };

                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _configuration["OpenAI:ApiKey"]);

                var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);

                if (!response.IsSuccessStatusCode)
                {
                    var erro = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Erro da OpenAI: {response.StatusCode} - {erro}");
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var reply = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

                /*return reply.Split(new[] { '\n', '-', '•' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim())
                            .Where(s => s.Length > 5)
                            .ToList();*/
                return AgruparSugestoes(reply);
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ ERRO OpenAI:\n" + ex); // Isso vai aparecer no console do Visual Studio
                throw; // relança o erro para o controller capturar
            }
        }
        public class TextoIAInput
        {
            public string Texto { get; set; }
        }
        private static readonly HashSet<string> Stopwords = new HashSet<string>
        {
            "a", "o", "e", "de", "do", "da", "em", "para", "com", "sem", "por",
            "um", "uma", "os", "as", "no", "na", "ao", "aos", "que", "meu", "minha",
            "seu", "sua", "está", "estao", "estava", "vou", "vai", "pra", "pro", "tá"
        };
        private string Stem(string palavra)
        {
            palavra = palavra.ToLower();

            if (palavra.EndsWith("ções") || palavra.EndsWith("ção"))
                return palavra[..^3]; // "licença" → "licen"
            if (palavra.EndsWith("mento") || palavra.EndsWith("mentos"))
                return palavra[..^5]; // "licenciamento" → "licencia"
            if (palavra.EndsWith("s") && palavra.Length > 4)
                return palavra[..^1]; // "usuarios" → "usuario"

            return palavra;
        }
        private string RemoverPontuacao(string texto)
        {
            var sb = new StringBuilder();
            foreach (char c in texto)
            {
                if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
                    sb.Append(c);
            }
            return sb.ToString();
        }
        private List<string> AgruparSugestoes(string texto)
        {
            var linhas = texto.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                              .Select(l => l.Trim())
                              .Where(l => !string.IsNullOrWhiteSpace(l))
                              .ToList();

            var sugestoes = new List<string>();
            StringBuilder sugestaoAtual = new();

            foreach (var linha in linhas)
            {
                // Se a linha começa com número ou bullet, é nova sugestão
                if (System.Text.RegularExpressions.Regex.IsMatch(linha, @"^(\d+[\.\-\)])|^[•\-]"))
                {
                    // Salva a sugestão anterior, se houver
                    if (sugestaoAtual.Length > 0)
                    {
                        sugestoes.Add(sugestaoAtual.ToString().Trim());
                        sugestaoAtual.Clear();
                    }
                    sugestaoAtual.Append(linha);
                }
                else
                {
                    // Continua a sugestão anterior
                    sugestaoAtual.Append(" " + linha);
                }
            }

            // Última sugestão pendente
            if (sugestaoAtual.Length > 0)
                sugestoes.Add(sugestaoAtual.ToString().Trim());

            // Remove sugestões muito curtas ou repetidas
            return sugestoes
                .Where(s => s.Length >= 10)
                .Distinct()
                .ToList();
        }
        private async Task NotificarErroParaTecnicosAsync(string erro, string textoUsuario)
        {
            // 🔧 Substitua pelos e-mails reais dos técnicos ou recupere do banco
            var tecnicos = new[] { "suporte@empresa.com", "ti@empresa.com" };

            var assunto = "[Portal Helpdesk] Erro ao consultar IA (ChatGPT)";
            var corpoHtml = $@"
            <p>Ocorreu um erro ao tentar obter sugestões automáticas com a IA:</p>
            <p><strong>Descrição digitada:</strong></p>
            <pre>{WebUtility.HtmlEncode(textoUsuario)}</pre>
            <p><strong>Erro:</strong></p>
            <pre style='color:red'>{WebUtility.HtmlEncode(erro)}</pre>
            <hr>
            <p>Verifique o saldo da OpenAI ou a validade da chave de API.</p>";

            foreach (var email in tecnicos)
            {
                await _emailService.EnviarEmailAsync(email, assunto, corpoHtml);
            }
        }

    }
}
