using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExcelDataReader;
using Microsoft.Extensions.Configuration;

namespace PortalHelpdeskTI.Services.Relatorios
{
    public class MudancaCarteiraServiceLayerService
    {
        private readonly IConfiguration _cfg;

        public MudancaCarteiraServiceLayerService(IConfiguration cfg)
        {
            _cfg = cfg;
        }

        public async Task<MudancaCarteiraResultado> ProcessarAsync(
            Stream excelStream,
            bool atualizarPrincipal,
            string sapUser,
            string sapPassword,
            Action<MudancaCarteiraProgresso>? onProgress = null,
            CancellationToken ct = default)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            var linhas = LerPlanilha(excelStream);

            var resultado = new MudancaCarteiraResultado
            {
                Total = linhas.Count
            };

            onProgress?.Invoke(new MudancaCarteiraProgresso
            {
                Total = resultado.Total,
                Processados = 0,
                Atualizados = 0,
                Status = "Lendo planilha e validando configuração..."
            });

            if (linhas.Count == 0)
                return resultado;

            // === Config base do SL a partir de SapB1 ===
            var baseUrl = _cfg["SapB1:ServiceLayerBaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
                baseUrl = _cfg["SapB1:BaseUrl"];

            var companyDb = _cfg["SapB1:CompanyDB"];
            var ignoreSsl = bool.TryParse(_cfg["SapB1:IgnoreSsl"], out var ig) && ig;

            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(companyDb))
            {
                resultado.Erros.Add("Configuração SapB1 incompleta (ServiceLayerBaseUrl/BaseUrl e CompanyDB).");

                onProgress?.Invoke(new MudancaCarteiraProgresso
                {
                    Total = resultado.Total,
                    Processados = 0,
                    Atualizados = 0,
                    Status = "Erro: configuração SAP incompleta.",
                    Concluido = true
                });

                return resultado;
            }

            if (string.IsNullOrWhiteSpace(sapUser) || string.IsNullOrWhiteSpace(sapPassword))
            {
                resultado.Erros.Add("Credenciais SAP não informadas.");

                onProgress?.Invoke(new MudancaCarteiraProgresso
                {
                    Total = resultado.Total,
                    Processados = 0,
                    Atualizados = 0,
                    Status = "Erro: credenciais SAP não informadas.",
                    Concluido = true
                });

                return resultado;
            }

            var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            if (ignoreSsl)
            {
                handler.ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }

            using var http = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/"),
                Timeout = TimeSpan.FromSeconds(120)
            };

            onProgress?.Invoke(new MudancaCarteiraProgresso
            {
                Total = resultado.Total,
                Processados = 0,
                Atualizados = 0,
                Status = "Autenticando no Service Layer..."
            });

            // Login com credenciais informadas pelo usuário
            var loginOk = await LoginAsync(http, companyDb, sapUser, sapPassword, ct);
            if (!loginOk)
            {
                resultado.Erros.Add("Falha ao autenticar no Service Layer (usuário/senha ou permissão).");

                onProgress?.Invoke(new MudancaCarteiraProgresso
                {
                    Total = resultado.Total,
                    Processados = 0,
                    Atualizados = 0,
                    Status = "Erro: falha ao autenticar no Service Layer.",
                    Concluido = true
                });

                return resultado;
            }

            int processados = 0;

            foreach (var linha in linhas)
            {
                ct.ThrowIfCancellationRequested();

                processados++;

                onProgress?.Invoke(new MudancaCarteiraProgresso
                {
                    Total = resultado.Total,
                    Processados = processados - 1,
                    Atualizados = resultado.Atualizados,
                    Status = $"Processando {linha.CardCode}..."
                });

                try
                {
                    var patch = await PatchBpAsync(http, linha.CardCode, linha.SlpCode, atualizarPrincipal, ct);

                    if (patch.Success)
                        resultado.Atualizados++;
                    else
                        resultado.Erros.Add($"Cliente {linha.CardCode}: HTTP {patch.StatusCode} - {patch.Message}");
                }
                catch (Exception ex)
                {
                    resultado.Erros.Add($"Cliente {linha.CardCode}: erro ao atualizar ({ex.Message}).");
                }

                onProgress?.Invoke(new MudancaCarteiraProgresso
                {
                    Total = resultado.Total,
                    Processados = processados,
                    Atualizados = resultado.Atualizados,
                    Status = $"Processados: {processados}/{resultado.Total} | Atualizados: {resultado.Atualizados}"
                });
            }

            // Logout (opcional)
            try { await http.PostAsync("Logout", new StringContent(""), ct); } catch { /* ignore */ }

            onProgress?.Invoke(new MudancaCarteiraProgresso
            {
                Total = resultado.Total,
                Processados = resultado.Total,
                Atualizados = resultado.Atualizados,
                Status = "Concluído.",
                Concluido = true
            });

            return resultado;
        }

        public async Task<bool> TestarLoginAsync(string sapUser, string sapPassword, CancellationToken ct = default)
        {
            var baseUrl = _cfg["SapB1:ServiceLayerBaseUrl"] ?? _cfg["SapB1:BaseUrl"];
            var companyDb = _cfg["SapB1:CompanyDB"];
            var ignoreSsl = bool.TryParse(_cfg["SapB1:IgnoreSsl"], out var ig) && ig;

            var handler = new HttpClientHandler { CookieContainer = new CookieContainer() };
            if (ignoreSsl)
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

            using var http = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/"),
                Timeout = TimeSpan.FromSeconds(60)
            };

            return await LoginAsync(http, companyDb, sapUser, sapPassword, ct);
        }

        private static List<MudancaCarteiraLinha> LerPlanilha(Stream excelStream)
        {
            var linhas = new List<MudancaCarteiraLinha>();

            using var reader = ExcelReaderFactory.CreateReader(excelStream);

            do
            {
                while (reader.Read())
                {
                    if (reader.Depth == 0) continue; // header

                    var cardCode = reader.GetValue(0)?.ToString()?.Trim();
                    var slpStr = reader.GetValue(1)?.ToString()?.Trim();

                    if (string.IsNullOrWhiteSpace(cardCode) || string.IsNullOrWhiteSpace(slpStr))
                        continue;

                    if (!int.TryParse(slpStr, out var slpCode))
                        continue;

                    linhas.Add(new MudancaCarteiraLinha
                    {
                        CardCode = cardCode,
                        SlpCode = slpCode
                    });
                }
            } while (reader.NextResult());

            return linhas;
        }

        private static async Task<bool> LoginAsync(HttpClient http, string companyDb, string user, string pass, CancellationToken ct)
        {
            var payload = new
            {
                CompanyDB = companyDb,
                UserName = user,
                Password = pass
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var resp = await http.PostAsync("Login", content, ct);
            return resp.IsSuccessStatusCode;
        }

        private sealed class PatchBpResult
        {
            public bool Success { get; init; }
            public int StatusCode { get; init; }
            public string? Message { get; init; }   // mensagem extraída do SL
            public string? RawBody { get; init; }   // resposta completa (JSON/texto)
        }

        private static string ExtractServiceLayerMessage(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "Erro sem corpo de resposta.";

            try
            {
                using var doc = JsonDocument.Parse(raw);

                // padrão do Service Layer:
                // { "error": { "message": { "value": "..." } } }
                if (doc.RootElement.TryGetProperty("error", out var error) &&
                    error.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("value", out var value))
                {
                    var msg = value.GetString();
                    if (!string.IsNullOrWhiteSpace(msg))
                        return msg!;
                }
            }
            catch { }

            return raw;
        }


        private static async Task<PatchBpResult> PatchBpAsync(
    HttpClient http,
    string cardCode,
    int slpCode,
    bool atualizarPrincipal,
    CancellationToken ct)
        {
            object payload = atualizarPrincipal
                ? new { SalesPersonCode = slpCode }   // carteira principal
                : new { U_SegVendedor = slpCode };    // UDF

            var json = JsonSerializer.Serialize(payload);

            var url = $"BusinessPartners('{EscapeODataKey(cardCode)}')";
            using var req = new HttpRequestMessage(new HttpMethod("PATCH"), url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            using var resp = await http.SendAsync(req, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);

            if (resp.IsSuccessStatusCode)
            {
                return new PatchBpResult
                {
                    Success = true,
                    StatusCode = (int)resp.StatusCode
                };
            }

            return new PatchBpResult
            {
                Success = false,
                StatusCode = (int)resp.StatusCode,
                RawBody = raw,
                Message = ExtractServiceLayerMessage(raw)
            };
        }


        private static string EscapeODataKey(string key) => key.Replace("'", "''");
    }

    // DTO de progresso (simples, para polling)
    public class MudancaCarteiraProgresso
    {
        public int Total { get; set; }
        public int Processados { get; set; }
        public int Atualizados { get; set; }
        public string Status { get; set; } = "";
        public bool Concluido { get; set; }
    }
}
