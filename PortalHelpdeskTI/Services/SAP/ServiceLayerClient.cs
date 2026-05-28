using Org.BouncyCastle.Cms;
using PortalHelpdeskTI.Infrastructure;
using PortalHelpdeskTI.Models;
using PortalHelpdeskTI.Models.Integracoes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using static iText.StyledXmlParser.Jsoup.Select.Evaluator;
using static PortalHelpdeskTI.Services.SAP.ServiceLayerClient;
using static System.Net.WebRequestMethods;
using System.Data.Odbc;

namespace PortalHelpdeskTI.Services.SAP;
public class ServiceLayerClient
{
    private readonly IHanaDb _hana;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _cfg;
    private readonly ServiceLayerCookieStore _cookieStore;
    private readonly IHttpContextAccessor _httpCtx;
    private readonly Dictionary<int, string> _stageCache = new();
    private readonly HttpClient _httpClient;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    public class SlAttachmentDto
    {
        [JsonPropertyName("AttachmentEntry")]
        public int AttachmentEntry { get; set; }

        [JsonPropertyName("Attachments2_Lines")]
        public List<SlAttachmentLine> Attachments2_Lines { get; set; } = new();
    }

    public class SlAttachmentLine
    {
        // Vou guardar aqui também o número do AttachmentEntry para facilitar depois
        [JsonIgnore]
        public int AttachmentEntry { get; set; }

        [JsonPropertyName("LineNum")]
        public int LineNum { get; set; }

        [JsonPropertyName("SourcePath")]
        public string SourcePath { get; set; } = "";

        [JsonPropertyName("FileName")]
        public string FileName { get; set; } = "";

        [JsonPropertyName("FileExtension")]
        public string FileExtension { get; set; } = "";
    }
    public bool HasActiveSession() => HasSlSession();
    private string BaseUrlRaw => _cfg["SapB1:ServiceLayerBaseUrl"]!;
    private string BaseUrl => BaseUrlRaw.EndsWith("/") ? BaseUrlRaw : BaseUrlRaw + "/";
    private string CompanyDB => _cfg["SapB1:CompanyDB"]!;
    private bool IgnoreSsl => bool.TryParse(_cfg["SapB1:IgnoreSsl"], out var b) && b;
    public async Task<(bool ok, string? error)> PostUpdateApprovalRequestSlimAsync(
    int approvalRequestId,
    string status,           // "ardApproved" | "ardRejected"
    string? remarks,
    string? approverUserName = null,
    int? userId = null,
    CancellationToken ct = default)
    {
        using var client = CreateClientWithCookies(noCache: true); // BaseAddress = .../b1s/v1/
        var rel = $"ApprovalRequests({approvalRequestId})/UpdateApprovalRequest";

        static StringContent J(string json) =>
            new StringContent(json, Encoding.UTF8, "application/json");

        static string Nice(string raw, HttpResponseMessage resp)
        {
            try
            {
                using var d = System.Text.Json.JsonDocument.Parse(raw);
                var root = d.RootElement;
                if (root.TryGetProperty("error", out var e) &&
                    e.TryGetProperty("message", out var m))
                {
                    if (m.TryGetProperty("value", out var v) &&
                        v.ValueKind == System.Text.Json.JsonValueKind.String)
                        return v.GetString()!;
                    if (m.ValueKind == System.Text.Json.JsonValueKind.String)
                        return m.GetString()!;
                }
            }
            catch { }
            return $"{(int)resp.StatusCode} {resp.ReasonPhrase}";
        }

        // monta o fragmento de decisão (camelCase nos campos internos)
        var decisionBase = approverUserName is { Length: > 0 }
            ? $@"""approverUserName"": ""{approverUserName}"", ""status"": ""{status}"", ""remarks"": {System.Text.Json.JsonSerializer.Serialize(remarks)}"
            : $@"""status"": ""{status}"", ""remarks"": {System.Text.Json.JsonSerializer.Serialize(remarks)}";

        // se tiver userId, criamos também a variante com userID explícito
        var decisionWithUserId = userId.HasValue
            ? $@"""userID"": {userId.Value}, ""status"": ""{status}"", ""remarks"": {System.Text.Json.JsonSerializer.Serialize(remarks)}"
            : null;

        // Variantes de PARAM NAME (o que o SL está cobrando):
        //  - "approvalRequest" (camel) e "ApprovalRequest" (Pascal)
        //  - array interno tanto "ApprovalRequestDecisions" (Pascal) quanto "approvalRequestDecisions" (camel)
        var bodies = new List<(string label, string json)>();

        void AddBodiesForParam(string paramName)
        {
            bodies.Add(($"A1-{paramName}-PascalArray",
                $@"{{ ""{paramName}"": {{ ""ApprovalRequestDecisions"": [ {{ {decisionBase} }} ] }} }}"));

            bodies.Add(($"A2-{paramName}-camelArray",
                $@"{{ ""{paramName}"": {{ ""approvalRequestDecisions"": [ {{ {decisionBase} }} ] }} }}"));

            if (decisionWithUserId != null)
            {
                bodies.Add(($"A3-{paramName}-PascalArray-userId",
                    $@"{{ ""{paramName}"": {{ ""ApprovalRequestDecisions"": [ {{ {decisionWithUserId} }} ] }} }}"));

                bodies.Add(($"A4-{paramName}-camelArray-userId",
                    $@"{{ ""{paramName}"": {{ ""approvalRequestDecisions"": [ {{ {decisionWithUserId} }} ] }} }}"));
            }

            // Alguns builds exigem approverPassword presente (mesmo vazio)
            if (approverUserName is { Length: > 0 })
            {
                bodies.Add(($"A5-{paramName}-camelArray-pass",
                    $@"{{ ""{paramName}"": {{ ""approvalRequestDecisions"": [ {{ ""approverUserName"": ""{approverUserName}"", ""approverPassword"": """", ""status"": ""{status}"", ""remarks"": {System.Text.Json.JsonSerializer.Serialize(remarks)} }} ] }} }}"));
            }
        }

        AddBodiesForParam("approvalRequest"); // mais provável
        AddBodiesForParam("ApprovalRequest"); // fallback

        var attempts = new StringBuilder();

        foreach (var (label, json) in bodies)
        {
            using var resp = await client.PostAsync(rel, J(json), ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);

            if (resp.IsSuccessStatusCode)
                return (true, null);

            attempts.AppendLine($"{label}: {Nice(raw, resp)}");
        }

        return (false, "UpdateApprovalRequest falhou em todas as variantes.\n" + attempts.ToString());
    }

    public async Task<(bool ok, string? error)> PostUpdateApprovalRequestPascalAsync(
    int approvalRequestId,
    int userId,
    string status,             // "ardApproved" | "ardRejected"
    string? remarks,
    CancellationToken ct = default)
    {
        using var client = CreateClientWithCookies(noCache: true); // BaseAddress = .../b1s/v1/
        var url = "ApprovalRequestsService_UpdateApprovalRequest"; // CAMINHO RELATIVO

        // JSONs montados à mão (garante PascalCase)
        string Escape(string? s) => s is null ? "null" : System.Text.Json.JsonSerializer.Serialize(s);

        var withWrapper = $@"
{{
  ""ApprovalRequest"": {{
    ""Code"": {approvalRequestId},
    ""ApprovalRequestDecisions"": [
      {{
        ""UserID"": {userId},
        ""Status"": ""{status}"",
        ""Remarks"": {Escape(remarks)}
      }}
    ]
  }}
}}".Trim();

        var rootOnly = $@"
{{
  ""Code"": {approvalRequestId},
  ""ApprovalRequestDecisions"": [
    {{
      ""UserID"": {userId},
      ""Status"": ""{status}"",
      ""Remarks"": {Escape(remarks)}
    }}
  ]
}}".Trim();

        static string Nice(string raw, HttpResponseMessage resp)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var e) &&
                    e.TryGetProperty("message", out var m))
                {
                    if (m.TryGetProperty("value", out var v) &&
                        v.ValueKind == System.Text.Json.JsonValueKind.String) return v.GetString()!;
                    if (m.ValueKind == System.Text.Json.JsonValueKind.String) return m.GetString()!;
                }
            }
            catch { }
            return $"{(int)resp.StatusCode} {resp.ReasonPhrase}";
        }

        // Tentativa 1: COM wrapper
        using (var content1 = new StringContent(withWrapper, Encoding.UTF8, "application/json"))
        {
            var r1 = await client.PostAsync(url, content1, ct);
            var b1 = await r1.Content.ReadAsStringAsync(ct);
            System.Diagnostics.Debug.WriteLine("[SL][UpdateApproval] T1 (wrapper) → " + (int)r1.StatusCode + " " + r1.ReasonPhrase);
            System.Diagnostics.Debug.WriteLine("[SL][UpdateApproval] BODY → " + b1);
            if (r1.IsSuccessStatusCode) return (true, null);
            var msg1 = Nice(b1, r1);

            // Tentativa 2: SEM wrapper
            using var content2 = new StringContent(rootOnly, Encoding.UTF8, "application/json");
            var r2 = await client.PostAsync(url, content2, ct);
            var b2 = await r2.Content.ReadAsStringAsync(ct);
            System.Diagnostics.Debug.WriteLine("[SL][UpdateApproval] T2 (root) → " + (int)r2.StatusCode + " " + r2.ReasonPhrase);
            System.Diagnostics.Debug.WriteLine("[SL][UpdateApproval] BODY → " + b2);
            if (r2.IsSuccessStatusCode) return (true, null);
            var msg2 = Nice(b2, r2);

            return (false, $"UpdateApprovalRequest recusado.\nT1: {msg1}\nT2: {msg2}");
        }
    }
    public async Task<(bool ok, string? error)> PostUpdateApprovalRequestCompatAsync(
    int approvalRequestId,
    string approverUserName,      // << SAP UserCode do aprovador logado
    string status,                // "ardApproved" | "ardRejected"
    string? remarks,
    CancellationToken ct = default)
    {
        using var client = CreateClientWithCookies(noCache: true); // BaseAddress = .../b1s/v1/
        var url = "ApprovalRequestsService_UpdateApprovalRequest"; // caminho relativo (cookies!)

        // Monta JSON manualmente (PascalCase) p/ evitar mudanças de casing
        string Escape(string? s) => s is null ? "null" : System.Text.Json.JsonSerializer.Serialize(s);

        // VARIANTE 1 — COM WRAPPER, só ApproverUserName
        var body1 = $@"
{{
  ""ApprovalRequest"": {{
    ""Code"": {approvalRequestId},
    ""ApprovalRequestDecisions"": [
      {{
        ""ApproverUserName"": ""{approverUserName}"",
        ""Status"": ""{status}"",
        ""Remarks"": {Escape(remarks)}
      }}
    ]
  }}
}}".Trim();

        // VARIANTE 2 — COM WRAPPER, ApproverUserName + ApproverPassword vazio
        var body2 = $@"
{{
  ""ApprovalRequest"": {{
    ""Code"": {approvalRequestId},
    ""ApprovalRequestDecisions"": [
      {{
        ""ApproverUserName"": ""{approverUserName}"",
        ""ApproverPassword"": """",
        ""Status"": ""{status}"",
        ""Remarks"": {Escape(remarks)}
      }}
    ]
  }}
}}".Trim();

        // VARIANTE 3 — SEM WRAPPER (raiz), só ApproverUserName
        var body3 = $@"
{{
  ""Code"": {approvalRequestId},
  ""ApprovalRequestDecisions"": [
    {{
      ""ApproverUserName"": ""{approverUserName}"",
      ""Status"": ""{status}"",
      ""Remarks"": {Escape(remarks)}
    }}
  ]
}}".Trim();

        // VARIANTE 4 — SEM WRAPPER (raiz), ApproverUserName + ApproverPassword vazio
        var body4 = $@"
{{
  ""Code"": {approvalRequestId},
  ""ApprovalRequestDecisions"": [
    {{
      ""ApproverUserName"": ""{approverUserName}"",
      ""ApproverPassword"": """",
      ""Status"": ""{status}"",
      ""Remarks"": {Escape(remarks)}
    }}
  ]
}}".Trim();

        static string Nice(string raw, HttpResponseMessage resp)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var e) &&
                    e.TryGetProperty("message", out var m))
                {
                    if (m.TryGetProperty("value", out var v) &&
                        v.ValueKind == System.Text.Json.JsonValueKind.String) return v.GetString()!;
                    if (m.ValueKind == System.Text.Json.JsonValueKind.String) return m.GetString()!;
                }
            }
            catch { }
            return $"{(int)resp.StatusCode} {resp.ReasonPhrase}";
        }

        async Task<(bool ok, string msg)> TryAsync(string lbl, string body)
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            var resp = await client.PostAsync(url, content, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            System.Diagnostics.Debug.WriteLine($"[SL][UpdateApproval][{lbl}] {(int)resp.StatusCode} {resp.ReasonPhrase}");
            System.Diagnostics.Debug.WriteLine($"[SL][UpdateApproval][{lbl}] BODY -> {raw}");
            return (resp.IsSuccessStatusCode, Nice(raw, resp));
        }

        var t1 = await TryAsync("T1-wrapper-user", body1);
        if (t1.ok) return (true, null);

        var t2 = await TryAsync("T2-wrapper-user-pass", body2);
        if (t2.ok) return (true, null);

        var t3 = await TryAsync("T3-root-user", body3);
        if (t3.ok) return (true, null);

        var t4 = await TryAsync("T4-root-user-pass", body4);
        if (t4.ok) return (true, null);

        return (false, $"UpdateApprovalRequest recusado.\nT1: {t1.msg}\nT2: {t2.msg}\nT3: {t3.msg}\nT4: {t4.msg}");
    }

    public async Task<(bool ok, string? error)> PostUpdateApprovalRequestMatrixAsync(
    int approvalRequestId,
    string approverUserName,  // seu SAP_USERCODE
    string status,            // "ardApproved" | "ardRejected"
    string? remarks,
    CancellationToken ct = default)
    {
        using var client = CreateClientWithCookies(noCache: true); // BaseAddress = .../b1s/v1/

        // Endpoints RELATIVOS a tentar (ordem importante)
        var endpoints = new[]
        {
        // bound action com "namespace" no caminho (muito comum em builds recentes)
        $"ApprovalRequests({approvalRequestId})/ApprovalRequestsService_UpdateApprovalRequest",
        // bound action "curta"
        $"ApprovalRequests({approvalRequestId})/UpdateApprovalRequest",
        // import simples (alguns ambientes expõem assim)
        "ApprovalRequestsService_UpdateApprovalRequest"
    };

        // Escape seguro p/ remarks
        string Esc(string? s) => s is null ? "null" : System.Text.Json.JsonSerializer.Serialize(s);

        // Decisão-base (PascalCase para os campos internos)
        string decisionPascal(string extra) =>
            $@"{{ {extra} ""Status"": ""{status}"", ""Remarks"": {Esc(remarks)} }}";

        // Variantes de decisão
        var d_user = decisionPascal($@"""ApproverUserName"": ""{approverUserName}"", ");
        var d_user_pass = decisionPascal($@"""ApproverUserName"": ""{approverUserName}"", ""ApproverPassword"": """", ");
        var d_min = decisionPascal(""); // sem user (SL pode inferir da sessão+pendência)

        // Payloads (ordem: wrapper Pascal, wrapper camel, root Pascal, root camel, array direto camel)
        var bodies = new (string label, string json)[]
        {
        ("W-Pascal-User", $@"{{ ""ApprovalRequest"": {{ ""ApprovalRequestDecisions"": [ {d_user} ] }} }}"),
        ("W-Pascal-UserPass", $@"{{ ""ApprovalRequest"": {{ ""ApprovalRequestDecisions"": [ {d_user_pass} ] }} }}"),
        ("W-Pascal-Min", $@"{{ ""ApprovalRequest"": {{ ""ApprovalRequestDecisions"": [ {d_min} ] }} }}"),

        ("W-camel-User", $@"{{ ""approvalRequest"": {{ ""approvalRequestDecisions"": [ {ToCamel(d_user)} ] }} }}"),
        ("W-camel-UserPass", $@"{{ ""approvalRequest"": {{ ""approvalRequestDecisions"": [ {ToCamel(d_user_pass)} ] }} }}"),
        ("W-camel-Min", $@"{{ ""approvalRequest"": {{ ""approvalRequestDecisions"": [ {ToCamel(d_min)} ] }} }}"),

        ("R-Pascal-User", $@"{{ ""Code"": {approvalRequestId}, ""ApprovalRequestDecisions"": [ {d_user} ] }}"),
        ("R-Pascal-UserPass", $@"{{ ""Code"": {approvalRequestId}, ""ApprovalRequestDecisions"": [ {d_user_pass} ] }}"),
        ("R-Pascal-Min", $@"{{ ""Code"": {approvalRequestId}, ""ApprovalRequestDecisions"": [ {d_min} ] }}"),

        ("R-camel-User", $@"{{ ""code"": {approvalRequestId}, ""approvalRequestDecisions"": [ {ToCamel(d_user)} ] }}"),
        ("R-camel-UserPass", $@"{{ ""code"": {approvalRequestId}, ""approvalRequestDecisions"": [ {ToCamel(d_user_pass)} ] }}"),
        ("R-camel-Min", $@"{{ ""code"": {approvalRequestId}, ""approvalRequestDecisions"": [ {ToCamel(d_min)} ] }}"),

        // algumas builds exigem o parâmetro com o NOME da ação (sem wrapper nem Code)
        ("P-camel-OnlyArray", $@"{{ ""approvalRequestDecisions"": [ {ToCamel(d_user)} ] }}")
        };

        // helper: Pascal->camel nos nomes de propriedades internas
        static string ToCamel(string pascalJson)
            => pascalJson
                .Replace("ApproverUserName", "approverUserName")
                .Replace("ApproverPassword", "approverPassword")
                .Replace("Status", "status")
                .Replace("Remarks", "remarks");

        static StringContent JC(string json) => new StringContent(json, Encoding.UTF8, "application/json");

        static string Nice(string raw, HttpResponseMessage resp)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var e) &&
                    e.TryGetProperty("message", out var m))
                {
                    if (m.TryGetProperty("value", out var v) &&
                        v.ValueKind == System.Text.Json.JsonValueKind.String) return v.GetString()!;
                    if (m.ValueKind == System.Text.Json.JsonValueKind.String) return m.GetString()!;
                }
            }
            catch { }
            return $"{(int)resp.StatusCode} {resp.ReasonPhrase}";
        }

        var attempts = new System.Text.StringBuilder();

        foreach (var ep in endpoints)
        {
            foreach (var (label, json) in bodies)
            {
                using var resp = await client.PostAsync(ep, JC(json), ct);
                var raw = await resp.Content.ReadAsStringAsync(ct);

                System.Diagnostics.Debug.WriteLine($"[SL][UpdApproval] EP={ep} {label} → {(int)resp.StatusCode} {resp.ReasonPhrase}");
                System.Diagnostics.Debug.WriteLine($"[SL][UpdApproval] BODY -> {raw}");

                if (resp.IsSuccessStatusCode)
                    return (true, null);

                attempts.AppendLine($"EP={ep} {label}: {Nice(raw, resp)}");
            }
        }

        return (false, "UpdateApprovalRequest falhou em todas as tentativas:\n" + attempts.ToString());
    }

    public async Task<List<SlAttachmentLine>> GetAttachmentsAsync(int atcEntry)
    {
        using var client = CreateClientWithCookies(noCache: true);

        var resp = await client.GetAsync($"Attachments2({atcEntry})");
        var raw = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            System.Diagnostics.Debug.WriteLine($"[SL] ERRO Attachments2({atcEntry}): {resp.StatusCode} {resp.ReasonPhrase}\n{raw}");
            return new();
        }

        var dto = JsonSerializer.Deserialize<SlAttachmentDto>(raw, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (dto?.Attachments2_Lines == null)
            return new();

        // 🎯 AQUI ESTAVA O PROBLEMA:
        // antes: line.AttachmentEntry = dto.AttachmentEntry; (que estava 0)
        // agora: usamos o parâmetro atcEntry, que é o correto
        foreach (var line in dto.Attachments2_Lines)
        {
            line.AttachmentEntry = atcEntry;
        }

        return dto.Attachments2_Lines;
    }


    public async Task<byte[]> GetAttachmentFileAsync(int attachmentEntry, int lineNum)
    {
        using var client = CreateClientWithCookies(noCache: true);

        // Vamos tentar alguns padrões de URL (variando de versão p/ versão)
        var urls = new[]
        {
        // Padrão mais comum: binário direto da linha
        $"Attachments2({attachmentEntry})/Attachments2_Lines({lineNum})/$value",

        // Alguns tenants expõem "Attachment" como propriedade binária
        $"Attachments2({attachmentEntry})/Attachments2_Lines({lineNum})/Attachment",

        // Fallback bruto: pegar o anexo inteiro (quando só tem 1 linha)
        $"Attachments2({attachmentEntry})/$value"
    };

        foreach (var url in urls)
        {
            try
            {
                var resp = await client.GetAsync(url);
                System.Diagnostics.Debug.WriteLine($"[SL] GetAttachmentFileAsync GET {url} -> {(int)resp.StatusCode} {resp.ReasonPhrase}");

                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"[SL] GetAttachmentFileAsync BODY (erro):\n{body}");
                    continue;
                }

                // Se chegou aqui, sucesso: retorna os bytes
                var bytes = await resp.Content.ReadAsByteArrayAsync();
                System.Diagnostics.Debug.WriteLine($"[SL] GetAttachmentFileAsync OK {url}, bytes = {bytes.Length}");
                return bytes;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SL] GetAttachmentFileAsync EXC em {url}: {ex.Message}");
            }
        }

        // Se nenhuma URL funcionou
        System.Diagnostics.Debug.WriteLine($"[SL] GetAttachmentFileAsync: nenhuma URL funcionou para AttachmentEntry={attachmentEntry}, LineNum={lineNum}");
        return Array.Empty<byte>();
    }

    public async Task<int?> TryResolveUserIdAsync(string userCode)
    {
        if (string.IsNullOrWhiteSpace(userCode)) return null;

        using var client = CreateClientWithCookies(noCache: true);
        static string Esc(string s) => s.Replace("'", "''");

        async Task<int?> ParseInternalKeyAsync(HttpResponseMessage resp)
        {
            var raw = await resp.Content.ReadAsStringAsync();
            System.Diagnostics.Debug.WriteLine($"[SL][Users] {resp.StatusCode} {resp.ReasonPhrase} {raw}");
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("value", out var arr) &&
                arr.ValueKind == JsonValueKind.Array &&
                arr.GetArrayLength() > 0 &&
                arr[0].TryGetProperty("InternalKey", out var key) &&
                key.ValueKind == JsonValueKind.Number)
            {
                return key.GetInt32();
            }
            return null;
        }

        // 1) tentativa exata (sem tolower)
        var url1 = $"Users?$select=InternalKey,UserCode&$filter=UserCode eq '{Esc(userCode)}'&$top=1";
        var r1 = await client.GetAsync(url1);
        var id1 = await ParseInternalKeyAsync(r1);
        if (id1.HasValue) return id1;

        // 2) começa com
        var url2 = $"Users?$select=InternalKey,UserCode&$filter=startswith(UserCode,'{Esc(userCode)}')&$top=1";
        var r2 = await client.GetAsync(url2);
        var id2 = await ParseInternalKeyAsync(r2);
        if (id2.HasValue) return id2;

        // 3) contém
        var url3 = $"Users?$select=InternalKey,UserCode&$filter=contains(UserCode,'{Esc(userCode)}')&$top=1";
        var r3 = await client.GetAsync(url3);
        var id3 = await ParseInternalKeyAsync(r3);
        if (id3.HasValue) return id3;


        return null;
    }

    public async Task<List<ApprovalRequestDto>> ListarPendenciasUsuarioHLGAsync(int userId, int top = 200)
    {
        using var client = CreateClientWithCookies(noCache: true);

        // base filter: só solicitações pendentes
        var filter = "Status eq 'arsPending'";

        // se conheço o userId, filtro no servidor: só as pendentes PARA MIM
        if (userId > 0)
            filter += $" and ApprovalRequestLines/any(l: l/Status eq 'ardPending' and l/UserID eq {userId})";

        var select = "Code,Status,ObjectType,ObjectEntry,OriginatorID,CurrentStage,IsDraft,DraftEntry,DraftType,CreationDate";
        // (não precisa expand para o any funcionar, mas vamos expandir leve pra reforço no client)
        var expand = "ApprovalRequestLines($select=UserID,Status)";

        var qs = new List<string>
    {
        "$orderby=" + Uri.EscapeDataString("Code desc"),
        "$top=" + top,
        "$filter=" + Uri.EscapeDataString(filter),
        "$select=" + Uri.EscapeDataString(select),
        "$expand=" + Uri.EscapeDataString(expand)
    };

        var url = "ApprovalRequests?" + string.Join("&", qs);

        var resp = await client.GetAsync(url);
        var raw = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            System.Diagnostics.Debug.WriteLine($"[SL] {resp.StatusCode} {resp.ReasonPhrase}\n{raw}");
            return new();
        }

        using var doc = JsonDocument.Parse(raw);
        var arr = doc.RootElement.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array
            ? v
            : (doc.RootElement.TryGetProperty("d", out var d) && d.TryGetProperty("results", out var r) ? r : default);

        if (arr.ValueKind != JsonValueKind.Array) return new();

        var list = new List<ApprovalRequestDto>();
        foreach (var e in arr.EnumerateArray())
        {
            var dto = JsonSerializer.Deserialize<ApprovalRequestDto>(e.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto != null) list.Add(dto);
        }

        // 🔎 hidrata decisions (opcional mas útil p/ tela de detalhes/ações)
        var hydrated = await HydrateDecisionsAsync(client, list);
        await EnrichApprovalRowsAsync(client, list);

        // Reforço no CLIENTE: se por algum motivo o SL ignorar o any (varia por versão),
        // filtramos novamente as pendentes PARA MIM antes de retornar.
        if (userId > 0)
        {
            list = list.Where(r =>
                (r.ApprovalRequestLines?.Any(l =>
                    string.Equals(l.Status, "ardPending", StringComparison.OrdinalIgnoreCase) && l.UserID == userId
                ) ?? false)
                ||
                (r.ApprovalRequestDecisions?.Any(d =>
                    string.Equals(d.Status, "ardPending", StringComparison.OrdinalIgnoreCase) && d.UserID == userId
                ) ?? false)
            ).ToList();
        }
        else
        {
            // Sem userId, mantenha só arsPending global
            list = list.Where(p => string.Equals(p.Status, "arsPending", StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return list;
    }

    // HELPER: detecta "cancelado" em um JSON de Draft/Documento
    private async Task HydrateOriginatorsAsync(List<ApprovalRequestDto> list)
    {
        // pega todos os OriginatorID distintos
        var ids = list
            .Where(x => x.OriginatorID.HasValue)
            .Select(x => x.OriginatorID!.Value)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
            return;

        var idsCsv = string.Join(",", ids);

        var sql = $@"
        SELECT ""USERID"", ""USER_CODE"", ""U_NAME""
        FROM ""OUSR""
        WHERE ""USERID"" IN ({idsCsv});";

        var dt = await _hana.QueryToDataTableAsync(sql, null);

        var map = dt.AsEnumerable()
            .ToDictionary(
                r => Convert.ToInt32(r["USERID"]),
                r => new
                {
                    UserCode = Convert.ToString(r["USER_CODE"]),
                    Name = Convert.ToString(r["U_NAME"])
                });

        foreach (var ar in list)
        {
            if (ar.OriginatorID.HasValue &&
                map.TryGetValue(ar.OriginatorID.Value, out var info))
            {
                ar.OriginatorUserCode = info.UserCode;
                ar.OriginatorName = info.Name;
            }
        }
    }

    public async Task<List<ApprovalRequestDto>> ListarPendentesSomenteDoUsuario(int top = 200)
    {
        //using var client = CreateClientWithCookies(noCache: true);
        using var client = CreateClientWithCookies(noCache: true);

        // 1) Resolve identidade do usuário atual (ID e Name)
        var (myId, myUserName) = await ResolveCurrentUserIdentityAsync(client);
        System.Diagnostics.Debug.WriteLine($"[SL] MY ID = {myId}, MY NAME = '{myUserName}'");

        // Lista que vai receber TODAS as pendências de todas as páginas
        var list = new List<ApprovalRequestDto>();

        // 2) URL inicial: arsPending, ordenado por Code desc, com paginação por $top
        string? nextUrl = $"ApprovalRequests?$filter=Status%20eq%20'arsPending'&$orderby=Code%20desc&$top={top}";

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        while (!string.IsNullOrEmpty(nextUrl))
        {
            var resp = await client.GetAsync(nextUrl);
            var raw = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[SL] {resp.StatusCode} {resp.ReasonPhrase}\n{raw}");
                if (IsInvalidSessionResponse(resp, raw))
                {
                    ClearSavedSession();
                    throw new InvalidOperationException("Sessão do Service Layer expirada. Refaça o login para listar as pendências.");
                }

                throw new InvalidOperationException(BuildSlError(raw, resp));
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            // Novo formato do SL (value + odata.nextLink)
            if (!root.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                // Formato inesperado – encerra loop
                break;
            }

            // Deserializa itens da página atual
            foreach (var e in arr.EnumerateArray())
            {
                var dto = JsonSerializer.Deserialize<ApprovalRequestDto>(e.GetRawText(), jsonOptions);
                if (dto != null)
                    list.Add(dto);
            }

            // Pega a próxima página (se existir)
            if (root.TryGetProperty("odata.nextLink", out var nextLinkProp) &&
                nextLinkProp.ValueKind == JsonValueKind.String)
            {
                nextUrl = nextLinkProp.GetString();
            }
            else
            {
                nextUrl = null; // não tem mais páginas
            }
        }

        // ------------------------------------------------------
        // FILTRA DRAFTS CANCELADOS / FECHADOS (PRQ - ObjType 112)
        // E JÁ TRAZ O DOCNUM DO ESBOÇO
        // ------------------------------------------------------
        var prqDraftIds = list
            .Where(ar => string.Equals(ar.IsDraft, "Y", StringComparison.OrdinalIgnoreCase)
                      && ar.DraftEntry.HasValue)
            .Select(ar => ar.DraftEntry!.Value)
            .Distinct()
            .ToList();

        if (prqDraftIds.Count > 0)
        {
            // 1) IN list
            var ids = string.Join(",", prqDraftIds);

            // 2) Busca DocEntry + DocNum + WddStatus
            var sql = $@"
    SELECT ""DocEntry"", ""DocNum"", ""WddStatus""
    FROM ""ODRF""
    WHERE ""DocEntry"" IN ({ids});";

            var dt = await _hana.QueryToDataTableAsync(sql, null);

            // 2.1) Monta um dicionário DocEntry -> (DocNum, WddStatus)
            var draftInfo = dt.AsEnumerable()
                .ToDictionary(
                    r => Convert.ToInt32(r["DocEntry"]),
                    r => new
                    {
                        DocNum = r["DocNum"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["DocNum"]),
                        WddStatus = Convert.ToString(r["WddStatus"])?.Trim()
                    }
                );

            // 3) Mantém somente rascunhos NÃO cancelados (WddStatus <> 'C')
            var openDraftIds = draftInfo
                .Where(kv => !string.Equals(kv.Value.WddStatus, "C", StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToHashSet();

            // 4) Remove da lista os inexistentes/cancelados
            list = list.Where(ar =>
                !(string.Equals(ar.IsDraft, "Y", StringComparison.OrdinalIgnoreCase)
                  && ar.DraftEntry.HasValue
                  && !openDraftIds.Contains(ar.DraftEntry.Value))
            ).ToList();

            // 5) Preenche DraftDocNum em cada Approval
            foreach (var ar in list)
            {
                if (string.Equals(ar.IsDraft, "Y", StringComparison.OrdinalIgnoreCase)
                    && ar.DraftEntry.HasValue
                    && draftInfo.TryGetValue(ar.DraftEntry.Value, out var info))
                {
                    ar.DraftDocNum = info.DocNum;
                }
            }
        }

        // 3) Hidrata decisions (p/ ter UserID/ApproverUserName nas decisões)
        await HydrateDecisionsAsync(client, list);

        // 4) Filtra "pendente para mim" com fallbacks
        bool IsMine(ApprovalRequestDto r)
        {
            // 1) Garante que SOMENTE arsPending entra no filtro
            if (!string.Equals(r.Status, "arsPending", StringComparison.OrdinalIgnoreCase))
                return false;

            // 2) Lógica atual de “pendente para mim”
            bool byLineUserId = r.ApprovalRequestLines?.Any(l =>
                string.Equals(l.Status, "ardPending", StringComparison.OrdinalIgnoreCase) &&
                myId.HasValue && l.UserID == myId.Value
            ) == true;

            bool byDecisionUserId = r.ApprovalRequestDecisions?.Any(d =>
                string.Equals(d.Status, "ardPending", StringComparison.OrdinalIgnoreCase) &&
                myId.HasValue && d.UserID == myId.Value
            ) == true;

            bool byDecisionUserName = r.ApprovalRequestDecisions?.Any(d =>
                string.Equals(d.Status, "ardPending", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(myUserName) &&
                string.Equals(d.ApproverUserName, myUserName, StringComparison.OrdinalIgnoreCase)
            ) == true;

            return byLineUserId || byDecisionUserId || byDecisionUserName;
        }

        var mine = list.Where(IsMine).ToList();
        mine = mine.Where(r => string.Equals(r.Status, "arsPending", StringComparison.OrdinalIgnoreCase)).ToList();


        // 5) Enriquecimento opcional (DocTotal/CardName etc.)
        if (mine.Count > 0)
        {
            await HydrateOriginatorsAsync(mine);      // <<< NOVO
            await EnrichApprovalRowsAsync(client, mine);
        }

        System.Diagnostics.Debug.WriteLine($"[SL] pendentes totais={list.Count} | meus={mine.Count}");
        if (mine.Count == 0)
        {
            // Logs úteis para diagnóstico rápido
            foreach (var rr2 in list.Take(10))
            {
                var lUsers = string.Join(",", rr2.ApprovalRequestLines?.Select(l => $"{l.UserID}:{l.Status}") ?? Enumerable.Empty<string>());
                var dUsers = string.Join(",", rr2.ApprovalRequestDecisions?.Select(d => $"{d.UserID}/{d.ApproverUserName}:{d.Status}") ?? Enumerable.Empty<string>());
                System.Diagnostics.Debug.WriteLine($"[SL] Code={rr2.Code} lines[{lUsers}] decs[{dUsers}] CurrentStage={rr2.CurrentStage}");
            }
        }

        return mine;
    }
    public void ClearSavedSession()
    {
        // supondo que seu _cookieStore tenha algo como Clear/Remove
        _cookieStore.Clear(SessionKey);  // ou Remove(SessionKey), conforme sua implementação
    }
    public async Task<(bool ok, string? error, ApprovalRequestDto? updated)> PatchApprovalDecisionAsync(
    int approvalRequestId,
    string status,               // "ardApproved" | "ardRejected"
    string? remarks = null,
    bool returnRepresentation = true)
    {
        var body = new
        {
            ApprovalRequestDecisions = new[]
            {
            new { Status = status, Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks }
        }
        };

        async Task<(bool ok, string? err, ApprovalRequestDto? dto)> SendOnceAsync()
        {
            using var client = CreateClientWithCookies(noCache: true); // ⬅️ MESMO PADRÃO DO SEU GET
            using var req = new HttpRequestMessage(HttpMethod.Patch, $"ApprovalRequests({approvalRequestId})")
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body),
                                            System.Text.Encoding.UTF8,
                                            "application/json")
            };
            if (returnRepresentation)
                req.Headers.TryAddWithoutValidation("Prefer", "return=representation");

            using var resp = await client.SendAsync(req);
            var content = await resp.Content.ReadAsStringAsync();

            // Trata sessão inválida (301 SL)
            if (!resp.IsSuccessStatusCode)
            {
                if (IsInvalidSessionPayload(content)) // veja helper abaixo
                    return (false, "SL-301: Invalid session", null);

                return (false, $"Service Layer {(int)resp.StatusCode}: {content}", null);
            }

            ApprovalRequestDto? dto = null;
            if (returnRepresentation && !string.IsNullOrWhiteSpace(content))
                dto = System.Text.Json.JsonSerializer.Deserialize<ApprovalRequestDto>(content, _jsonOptions);

            return (true, null, dto);
        }

        // 1ª tentativa
        var (ok, err, dto) = await SendOnceAsync();
        if (ok) return (ok, err, dto);

        // Se foi sessão inválida, reloga e tenta 1x de novo (se você já tem um LoginAsync)
        if (err == "SL-301: Invalid session")
        {
            await LoginAsync(
                _httpCtx.HttpContext?.Session.GetString("SAP_USERCODE") ?? throw new InvalidOperationException("SAP_USERCODE ausente."),
                _httpCtx.HttpContext?.Session.GetString("SAP_PASSWORD") ?? throw new InvalidOperationException("SAP_PASSWORD ausente.")
            );

            (ok, err, dto) = await SendOnceAsync();
        }

        return (ok, err, dto);
    }

    private static bool IsInvalidSessionPayload(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("error", out var e) &&
                e.TryGetProperty("code", out var c) &&
                c.ValueKind == JsonValueKind.Number &&
                c.GetInt32() == 301)
                return true;
        }
        catch { /* ignore */ }
        return false;
    }
    private static bool IsInvalidSessionResponse(HttpResponseMessage resp, string content)
    {
        return resp.StatusCode == HttpStatusCode.Unauthorized ||
               resp.StatusCode == HttpStatusCode.Forbidden ||
               IsInvalidSessionPayload(content);
    }
    private async Task<(int? id, string? name)> ResolveCurrentUserIdentityAsync(HttpClient client)
    {
        // 1) Tenta pegar ID da sessão (setado no LoginAsync)
        var idFromSession = GetSapUserIdFromSession();
        if (idFromSession.HasValue)
        {
            var name = await ResolveUserNameByIdAsync(idFromSession.Value);
            return (idFromSession.Value, name);
        }

        // 2) Tenta pelo UserCode que você guardou na sessão durante o login
        var code = GetSapUserCodeFromSession();
        if (!string.IsNullOrWhiteSpace(code))
        {
            var id = await GetSapUserIdAsync(client, code);
            var name = id.HasValue ? await ResolveUserNameByIdAsync(id.Value) : null;
            return (id, name);
        }

        // 3) Último fallback: sem info de sessão, sem filtro por ID; nome ficará nulo
        return (null, null);
    }
    private async Task<HashSet<int>> HydrateDecisionsAsync(HttpClient client, List<ApprovalRequestDto> requests)
    {
        var okIds = new HashSet<int>();
        if (requests.Count == 0) return okIds;

        var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var postOpts = new JsonSerializerOptions { PropertyNamingPolicy = null }; // PascalCase no payload

        // Descobre endpoint e nome do parâmetro no $metadata
        var meta = await GetMetadataAsync(client);
        (string endpoint, string paramName)? sig = null;
        if (!string.IsNullOrEmpty(meta))
            sig = FindApprovalGetSignature(meta!);

        // Fallback se não achou nada no metadata
        var endpointName = sig?.endpoint ?? "ApprovalRequestsService_GetApprovalRequest";
        var paramName = sig?.paramName ?? "ApprovalRequestID";

        System.Diagnostics.Debug.WriteLine($"[SL] Using action: {endpointName} with param '{paramName}'");

        foreach (var req in requests)
        {
            try
            {
                // Monta payload dinâmico com o nome certo do parâmetro
                var payload = new Dictionary<string, object?> { [paramName] = req.Code };

                var resp = await client.PostAsJsonAsync(endpointName, payload, postOpts);
                var body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[SL] POST {endpointName} -> {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {body}");
                    continue;
                }

                using var doc = JsonDocument.Parse(body);

                // Formatos possíveis:
                // 1) { "ApprovalRequest": { ... } }
                // 2) { ... } (o próprio objeto)
                JsonElement ar;
                if (doc.RootElement.TryGetProperty("ApprovalRequest", out var arObj) && arObj.ValueKind == JsonValueKind.Object)
                    ar = arObj;
                else
                    ar = doc.RootElement;

                // Decisions podem vir como ApprovalRequestDecisions ou outro nome; verifique ambos
                if (ar.TryGetProperty("ApprovalRequestDecisions", out var decs) && decs.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<ApprovalRequestDecisionDto>();
                    foreach (var e in decs.EnumerateArray())
                    {
                        var dec = JsonSerializer.Deserialize<ApprovalRequestDecisionDto>(e.GetRawText(), jsonOpts);
                        if (dec != null) list.Add(dec);
                    }
                    req.ApprovalRequestDecisions = list;
                    okIds.Add(req.Code);
                }
                else
                {
                    // log para inspecionar a estrutura quando não vier no nome esperado
                    var snippet = ar.GetRawText();
                    if (snippet.Length > 600) snippet = snippet.Substring(0, 600) + "...";
                    System.Diagnostics.Debug.WriteLine($"[SL] GetApprovalRequest OK (Code={req.Code}), mas não achei 'ApprovalRequestDecisions'. Body: {snippet}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SL] EXC Hydrate Code={req.Code}: {ex.Message}");
            }
        }

        return okIds;
    }
    private async Task EnrichApprovalRowsAsync(HttpClient client, List<ApprovalRequestDto> rows)
    {
        if (rows.Count == 0) return;
        var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var postOpts = new JsonSerializerOptions { PropertyNamingPolicy = null };

        foreach (var r in rows)
        {
            // 1) Completa CreationDate/Remarks via action
            try
            {
                var payload = new Dictionary<string, object?> { ["ApprovalRequestID"] = r.Code };
                var resp = await client.PostAsJsonAsync("ApprovalRequestsService_GetApprovalRequest", payload, postOpts);
                var body = await resp.Content.ReadAsStringAsync();
                if (resp.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(body);
                    var ar = doc.RootElement.TryGetProperty("ApprovalRequest", out var arObj) ? arObj : doc.RootElement;

                    if (ar.TryGetProperty("CreationDate", out var cd) && cd.ValueKind == JsonValueKind.String &&
                        DateTime.TryParse(cd.GetString(), out var dt))
                        r.CreationDate = dt;

                    if (ar.TryGetProperty("CreationTime", out var ct) && ct.ValueKind == JsonValueKind.String)
                        r.CreationTime = ct.GetString();

                    if (string.IsNullOrEmpty(r.Remarks) && ar.TryGetProperty("Remarks", out var rem) && rem.ValueKind == JsonValueKind.String)
                        r.Remarks = rem.GetString();
                }
            }
            catch { /* opcional: log */ }

            // 2) Dados do documento de origem (DocNum/CardName/Total)
            try
            {
                if (r.ObjectEntry.HasValue && r.ObjectEntry.Value > 0)
                {
                    if (r.ObjectType == "1470000113") // Purchase Request
                    {
                        var resp = await client.GetAsync($"PurchaseRequests({r.ObjectEntry.Value})?$select=DocNum,CardName,DocTotal,DocTotalFc");
                        var json = await resp.Content.ReadAsStringAsync();
                        if (resp.IsSuccessStatusCode)
                        {
                            using var doc2 = JsonDocument.Parse(json);
                            var root = doc2.RootElement.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > 0 ? v[0] : doc2.RootElement;

                            r.DraftType = "PRQ";
                            if (root.TryGetProperty("DocTotal", out var tot))
                            {
                                if (tot.ValueKind == JsonValueKind.Number && tot.TryGetDecimal(out var n))
                                    r.DocTotal = n;
                                else if (tot.ValueKind == JsonValueKind.String &&
                                         decimal.TryParse(tot.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s))
                                    r.DocTotal = s;
                            }

                            if (root.TryGetProperty("DocTotalFc", out var totfc))
                            {
                                if (tot.ValueKind == JsonValueKind.Number && totfc.TryGetDecimal(out var n))
                                    r.DocTotalFC = n;
                                else if (tot.ValueKind == JsonValueKind.String &&
                                         decimal.TryParse(totfc.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s))
                                    r.DocTotalFC = s;
                            }

                            r.CardName = root.TryGetProperty("CardName", out var cn) ? cn.GetString() : r.CardName;
                        }
                    }
                    else if (r.ObjectType == "22") // Purchase Order
                    {
                        var resp = await client.GetAsync($"PurchaseOrders({r.ObjectEntry.Value})?$select=DocNum,CardName,DocTotal,DocTotalFc");
                        var json = await resp.Content.ReadAsStringAsync();
                        if (resp.IsSuccessStatusCode)
                        {
                            using var doc2 = JsonDocument.Parse(json);
                            var root = doc2.RootElement.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > 0 ? v[0] : doc2.RootElement;

                            r.DraftType = "PO";
                            if (root.TryGetProperty("DocTotal", out var tot))
                            {
                                if (tot.ValueKind == JsonValueKind.Number && tot.TryGetDecimal(out var n))
                                    r.DocTotal = n;
                                else if (tot.ValueKind == JsonValueKind.String &&
                                         decimal.TryParse(tot.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s))
                                    r.DocTotal = s;
                            }

                            if (root.TryGetProperty("DocTotalFc", out var totfc))
                            {
                                if (tot.ValueKind == JsonValueKind.Number && totfc.TryGetDecimal(out var n))
                                    r.DocTotalFC =  n;
                                else if (tot.ValueKind == JsonValueKind.String &&
                                         decimal.TryParse(totfc.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s))
                                    r.DocTotalFC = s;
                            }

                            r.CardName = root.TryGetProperty("CardName", out var cn) ? cn.GetString() : r.CardName;
                        }
                    }
                    // outros ObjectType: mapear conforme usar
                }
                else
                {
                    // >>> NOVO: enriquecer RASCUNHOS (IsDraft = 'Y' e DraftEntry presente)
                    bool isDraft = string.Equals(r.IsDraft, "Y", StringComparison.OrdinalIgnoreCase);
                    if (isDraft && r.DraftEntry.HasValue)
                    {
                        var headUrl = $"Drafts({r.DraftEntry.Value})?$select=CardName,DocTotal,DocTotalFc";
                        var hResp = await client.GetAsync(headUrl);
                        var hJson = await hResp.Content.ReadAsStringAsync();
                        if (hResp.IsSuccessStatusCode)
                        {
                            using var hDoc = JsonDocument.Parse(hJson);
                            var hRootAll = hDoc.RootElement;

                            JsonElement hRoot = hRootAll;
                            if (hRootAll.TryGetProperty("value", out var hv) && hv.ValueKind == JsonValueKind.Array && hv.GetArrayLength() > 0)
                                hRoot = hv[0];
                            else if (hRootAll.TryGetProperty("d", out var hd) && hd.TryGetProperty("results", out var hrs) &&
                                     hrs.ValueKind == JsonValueKind.Array && hrs.GetArrayLength() > 0)
                                hRoot = hrs[0];

                            if (hRoot.TryGetProperty("CardName", out var cn) && cn.ValueKind == JsonValueKind.String)
                                r.CardName = cn.GetString();

                            if (hRoot.TryGetProperty("DocTotal", out var dtok) && dtok.ValueKind != JsonValueKind.Null)
                            {
                                if (dtok.ValueKind == JsonValueKind.Number && dtok.TryGetDecimal(out var n))
                                    r.DocTotal = n;
                                else if (dtok.ValueKind == JsonValueKind.String &&
                                         decimal.TryParse(dtok.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s))
                                    r.DocTotal = s;
                            }

                            if (hRoot.TryGetProperty("DocTotalFc", out var dtokfc) && dtok.ValueKind != JsonValueKind.Null)
                            {
                                if (dtok.ValueKind == JsonValueKind.Number && dtokfc.TryGetDecimal(out var n))
                                    r.DocTotalFC = n;
                                else if (dtok.ValueKind == JsonValueKind.String &&
                                         decimal.TryParse(dtokfc.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s))
                                    r.DocTotalFC = s;
                            }

                            // opcional: marcar tipo
                            if (string.IsNullOrWhiteSpace(r.DraftType))
                                r.DraftType = "Rascunho";
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[SL] GET {headUrl} -> {(int)hResp.StatusCode} {hResp.ReasonPhrase}\n{hJson}");
                        }
                    }
                }
            }
            catch { /* opcional: log */ }
            // 3) Nome da Etapa
            try
            {
                if (r.CurrentStage.HasValue)
                    r.CurrentStageName = await ResolveStageNameAsync(client, r.CurrentStage.Value);
            }
            catch { /* opcional: log */ }
        }
    }
    public async Task<List<ApprovalRequestDto>> ListarPendenciasGerais(int top = 100)
    {
        using var client = CreateClientWithCookies(noCache: true);
        var url = $"ApprovalRequests?$filter=Status eq 'arsPending'&$orderby=Code desc&$top={top}" +
          "&$select=Code,Status,ObjectType,ObjectEntry,OriginatorID,CurrentStage,IsDraft,DraftEntry,DraftType,CreationDate";

        var lista = await GetListAsync<ApprovalRequestDto>(client, url);

        await HydrateDecisionsAsync(client, lista);
        await EnrichApprovalRowsAsync(client, lista);
        return lista;
    }
    private async Task<List<T>> GetListAsync<T>(HttpClient c, string relativeUrl)
    {
        var resp = await c.GetAsync(relativeUrl);
        var raw = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) return new();

        using var doc = JsonDocument.Parse(raw);
        JsonElement arr;
        if (doc.RootElement.TryGetProperty("value", out var valueArr) && valueArr.ValueKind == JsonValueKind.Array)
            arr = valueArr;
        else if (doc.RootElement.TryGetProperty("d", out var d) && d.TryGetProperty("results", out var results))
            arr = results;
        else
            return new();

        var list = new List<T>();
        foreach (var e in arr.EnumerateArray())
            list.Add(JsonSerializer.Deserialize<T>(e.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!);
        return list;
    }
    private bool HasSlSession()
    {
        var (b1, _) = _cookieStore.Get(SessionKey);
        return !string.IsNullOrWhiteSpace(b1);
    }
    public ServiceLayerClient(IHttpClientFactory httpFactory, IConfiguration cfg, ServiceLayerCookieStore cookieStore, IHttpContextAccessor httpCtx,
        HttpClient httpClient, IHanaDb hana)
    {
        _hana = hana;
        _httpClient = httpClient;
        if (_httpClient.BaseAddress == null)
            throw new InvalidOperationException("HttpClient.BaseAddress não configurado. Verifique SapB1:BaseUrl/DI.");
        _httpFactory = httpFactory;
        _cfg = cfg;
        _cookieStore = cookieStore;
        _httpCtx = httpCtx;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }
    private string SessionKey => _httpCtx.HttpContext?.Session?.Id ?? "anon";
    public HttpClient CreateClientWithCookies(bool noCache = false)
    {
        var baseUri = new Uri(BaseUrl); // ex.: https://...:50000/b1s/v1/
        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = _cookieStore.ToCookieContainer(baseUri, SessionKey)
        };

        if (IgnoreSsl)
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        // 🔎 LOG 1: antes de criar o HttpClient (já dá pra ver se temos cookie salvo)
        var (b1, route) = _cookieStore.Get(SessionKey);
        System.Diagnostics.Debug.WriteLine($"[SL] (pre) SessionKey={SessionKey} | HasB1={(!string.IsNullOrWhiteSpace(b1))} | RouteID={(route ?? "(null)")}");

        var timeoutSeconds = 100;
        if (int.TryParse(_cfg["SapB1:TimeoutSeconds"], out var configuredTimeoutSeconds) &&
            configuredTimeoutSeconds > 0)
        {
            timeoutSeconds = configuredTimeoutSeconds;
        }

        var client = new HttpClient(handler)
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };

        // 🔎 LOG 2: depois de criar o HttpClient
        System.Diagnostics.Debug.WriteLine($"[SL] BaseAddress={client.BaseAddress}");

        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Remove("B1S-CaseInsensitive");
        client.DefaultRequestHeaders.Add("B1S-CaseInsensitive", "true");

        // 👉 Ponto chave: forçar no-cache quando solicitado
        if (noCache)
        {
            client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true,
                MustRevalidate = true
            };
            // Alguns proxies/servidores respeitam também o Pragma
            client.DefaultRequestHeaders.Pragma.Clear();
            client.DefaultRequestHeaders.Pragma.ParseAdd("no-cache");
        }

        return client;
    }
    private static string BuildSlError(string content, HttpResponseMessage resp)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("value", out var val))
            {
                return $"Service Layer: {val.GetString()}";
            }
        }
        catch { /* ignore */ }

        return $"Service Layer retornou {(int)resp.StatusCode} {resp.ReasonPhrase}. Conteúdo: {content}";
    }
    // ===== Login / Logout =====
    public async Task<(bool ok, string? error)> LoginAsync(string userName, string password)
    {
        try
        {
            using var client = CreateClientWithCookies(noCache: true);

            // MUITO IMPORTANTE: manter PascalCase nas propriedades
            var payload = new { CompanyDB = CompanyDB, UserName = userName, Password = password };
            var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = null };

            var resp = await client.PostAsJsonAsync("Login", payload, jsonOpts);

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                // Ex.: 404 costuma ser BaseUrl sem barra final (você já ajustou, mas deixo o hint)
                var hint = resp.StatusCode == HttpStatusCode.NotFound
                    ? "Verifique se a URL termina com /b1s/v1/ (com a barra no final)."
                    : null;

                return (false, $"Erro no Login SL: {(int)resp.StatusCode} {resp.ReasonPhrase}. {hint}\n{body}");
            }

            // Captura cookies no header Set-Cookie
            if (resp.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                string? b1 = null, route = null;

                foreach (var c in setCookies)
                {
                    // cada linha pode vir com múltiplos atributos
                    foreach (var part in c.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var kv = part.Split('=', 2);
                        if (kv.Length < 2) continue;
                        var k = kv[0].Trim();
                        var v = kv[1].Trim();

                        if (k.Equals("B1SESSION", StringComparison.OrdinalIgnoreCase)) b1 = v;
                        if (k.Equals("ROUTEID", StringComparison.OrdinalIgnoreCase)) route = v;
                    }
                }

                if (!string.IsNullOrWhiteSpace(b1))
                {
                    _cookieStore.Set(SessionKey, b1!, route ?? "");
                    _httpCtx.HttpContext?.Session?.SetString("SAP_USERCODE", userName);
                    var uid = await GetSapUserIdAsync(client, userName);
                    if (uid.HasValue)
                        _httpCtx.HttpContext?.Session?.SetInt32("SAP_USERID", uid.Value);
                    return (true, null);
                }

                return (false, "Login no SL retornou 200 mas não enviou B1SESSION no Set-Cookie.");
            }

            return (false, "Login no SL retornou 200 mas sem cabeçalho Set-Cookie.");
        }
        catch (HttpRequestException ex)
        {
            // Problema de rede/certificado/timeout etc.
            return (false, $"Falha ao conectar no Service Layer: {ex.Message}. " +
                           $"URL atual: {BaseUrl} | CompanyDB: {CompanyDB} | IgnoreSsl: {IgnoreSsl}");
        }
        catch (Exception ex)
        {
            return (false, $"Exceção no Login SL: {ex.Message}");
        }
    }
    public void Logout() => _cookieStore.Clear(SessionKey);
    // ===== Modelos =====
    public record ApprovalDecision(int StageCode, int UserID, string Status, string? Remarks);
    public class DocLineDto
    {
        public string? ItemCode { get; set; }
        public string? ItemName { get; set; }
        public string? ItemDescription { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        // UDF da PRQ1 (Requisição de Compra)
        [JsonPropertyName("U_TX_DescMatNfse")]
        public string? U_TX_DescMatNfse { get; set; }
    }
    public sealed class ApprovalRequestDto
    {
        public int Code { get; set; }
        public int? ApprovalTemplatesID { get; set; }
        public string? ObjectType { get; set; }
        public string? IsDraft { get; set; }
        public int? ObjectEntry { get; set; }
        public string? Status { get; set; }
        public string? Remarks { get; set; }
        public int? CurrentStage { get; set; }
        public string? CurrentStageName { get; set; }

        public int? OriginatorID { get; set; }   // já existia

        public DateTime? CreationDate { get; set; }
        public string? CreationTime { get; set; }
        public int? DraftEntry { get; set; }
        public string? DraftType { get; set; }
        public int? DraftDocNum { get; set; }

        // NAV / coleções
        public List<ApprovalRequestStageDto>? ApprovalRequestStages { get; set; }
        public List<ApprovalRequestLineDto>? ApprovalRequestLines { get; set; }
        public List<ApprovalRequestDecisionDto>? ApprovalRequestDecisions { get; set; }

        // Enriquecimento p/ grid
        public string? CardName { get; set; }
        public decimal? DocTotal { get; set; }
        public decimal? DocTotalFC { get; set; }
        public string? DocNum { get; set; }

        // >>> NOVO: informações do solicitante
        public string? OriginatorUserCode { get; set; }
        public string? OriginatorName { get; set; }
        public string? CreatorName => OriginatorName;

        // Título normalizado (tipo INITCAP)
        public string? OriginatorNameInitCap
            => string.IsNullOrWhiteSpace(OriginatorName)
                ? OriginatorName
                : CultureInfo.CurrentCulture.TextInfo
                    .ToTitleCase(OriginatorName.ToLowerInvariant());

        // helpers de display
        public string? DisplayCreatedAt => CreationDate?.ToString("dd/MM/yyyy") ?? "—";
        public string? DisplaySummary => string.IsNullOrWhiteSpace(Remarks) ? "—" : Remarks;
    }
    public sealed class ODataResult<T>
    {
        [System.Text.Json.Serialization.JsonPropertyName("value")]
        public List<T> Value { get; set; } = new();

        // Importante: nome exato com o "odata.nextLink"
        [System.Text.Json.Serialization.JsonPropertyName("odata.nextLink")]
        public string? NextLink { get; set; }
    }

    public class ApprovalRequestLineDto
    {
        public int? StageCode { get; set; }
        public int? UserID { get; set; }
        public string? Status { get; set; }     // ardPending/ardApproved...
        public string? Remarks { get; set; }
        public DateTime? UpdateDate { get; set; }
        public string? UpdateTime { get; set; } // HLG entrega string ou null
        public DateTime? CreationDate { get; set; }
        public string? CreationTime { get; set; }
    }
    public class ApprovalRequestStageDto
    {
        public int? StageCode { get; set; }
        public int? UserID { get; set; }
        public string? Status { get; set; }       // ardPending / ardApproved / ardRejected...
        public string? Remarks { get; set; }
        public DateTime? CreationDate { get; set; }
        public DateTime? UpdateDate { get; set; }
        public string? UpdateTime { get; set; }   // algumas versões retornam string
    }
    public class ApprovalRequestDecisionDto
    {
        public int? UserID { get; set; }           // <- novo (se a tua tiver)
        public int? StageID { get; set; }
        public int? StageCode { get; set; }        // <- novo (se a tua tiver)
        public string? ApproverUserName { get; set; }
        public string? ApproverPassword { get; set; }
        public string? Status { get; set; }        // ardPending/ardApproved/...
        public string? Remarks { get; set; }
        public int? StageCodeInTemplate { get; set; } // opcional, se usar template
    }
    public record CurrentUserDto(int InternalKey, string UserCode, string? UserName);

        // ===== Helpers =====
        // dentro de ServiceLayerClient
    private int? GetSapUserIdFromSession()
        => _httpCtx.HttpContext?.Session?.GetInt32("SAP_USERID");
    private string? GetSapUserCodeFromSession()
        => _httpCtx.HttpContext?.Session?.GetString("SAP_USERCODE");
    private async Task<int?> GetSapUserIdAsync(HttpClient client, string sapUserCode)
    {
        var resp = await client.GetAsync($"Users?$select=InternalKey,UserCode&$filter=UserCode eq '{sapUserCode}'");
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var arr = doc.RootElement.GetProperty("value");
        if (arr.GetArrayLength() == 0) return null;

        return arr[0].GetProperty("InternalKey").GetInt32();
    }
    public async Task<int?> ResolveUserIdByCodeAsync(string userCode)
    {
        using var client = CreateClientWithCookies(noCache: true);
        return await GetSapUserIdAsync(client, userCode);
    }
    private async Task<int?> ResolveStageIdAsync(int stageCode)
    {
        using var client = CreateClientWithCookies(noCache: true);

        // Tenta por StageCode
        var try1 = await client.GetAsync($"ApprovalStages?$select=StageID,StageCode&$filter=StageCode eq {stageCode}&$top=1");
        var raw1 = await try1.Content.ReadAsStringAsync();
        if (try1.IsSuccessStatusCode)
        {
            using var d1 = JsonDocument.Parse(raw1);
            if (d1.RootElement.TryGetProperty("value", out var arr1) && arr1.ValueKind == JsonValueKind.Array && arr1.GetArrayLength() > 0)
                return arr1[0].GetProperty("StageID").GetInt32();
            if (d1.RootElement.TryGetProperty("d", out var dd1) && dd1.TryGetProperty("results", out var rs1) &&
                rs1.ValueKind == JsonValueKind.Array && rs1.GetArrayLength() > 0)
                return rs1[0].GetProperty("StageID").GetInt32();
        }

        // Fallback: por SequenceNo (alguns tenants usam CurrentStage=SequenceNo)
        var try2 = await client.GetAsync($"ApprovalStages?$select=StageID,SequenceNo&$filter=SequenceNo eq {stageCode}&$top=1");
        var raw2 = await try2.Content.ReadAsStringAsync();
        if (try2.IsSuccessStatusCode)
        {
            using var d2 = JsonDocument.Parse(raw2);
            if (d2.RootElement.TryGetProperty("value", out var arr2) && arr2.ValueKind == JsonValueKind.Array && arr2.GetArrayLength() > 0)
                return arr2[0].GetProperty("StageID").GetInt32();
            if (d2.RootElement.TryGetProperty("d", out var dd2) && dd2.TryGetProperty("results", out var rs2) &&
                rs2.ValueKind == JsonValueKind.Array && rs2.GetArrayLength() > 0)
                return rs2[0].GetProperty("StageID").GetInt32();
        }

        return null;
    }
    public async Task<string?> ResolveUserCodeByIdAsync(int internalKey)
    {
        using var client = CreateClientWithCookies(noCache: true);
        var resp = await client.GetAsync($"Users({internalKey})?$select=UserCode");
        var raw = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(raw);
        if (doc.RootElement.TryGetProperty("UserCode", out var u) && u.ValueKind == JsonValueKind.String)
            return u.GetString();
        return null;
    }
    public async Task<bool> ApproveAsync(int approvalRequestId, int stageCode, int userId, string status, string? remarks)
    {
        if (!HasSlSession()) return false;

        using var client = CreateClientWithCookies(noCache: true);

        // 1) StageCode -> StageID (o que o serviço de atualização realmente espera)
        var stageId = await ResolveStageIdAsync(stageCode);
        if (stageId is null)
        {
            System.Diagnostics.Debug.WriteLine($"[SL][ApproveAsync] StageID não encontrado p/ StageCode={stageCode}");
            return false;
        }

        // 2) UserID (InternalKey) -> ApproverUserName (UserCode)
        var approverUserName = await ResolveUserCodeByIdAsync(userId);
        if (string.IsNullOrWhiteSpace(approverUserName))
        {
            System.Diagnostics.Debug.WriteLine($"[SL][ApproveAsync] ApproverUserName não resolvido p/ UserID={userId}");
            return false;
        }

        // 3) Payload exatamente no formato do SL
        var payload = new
        {
            ApprovalRequest = new
            {
                ApprovalRequestID = approvalRequestId,
                ApprovalRequestDecisions = new[]
                {
                new
                {
                    StageID = stageId.Value,               // ✔ StageID (não StageCode)
                    ApproverUserName = approverUserName,   // ✔ UserCode (não UserID)
                    Status = status,                       // 'ardApproved' | 'ardRejected'
                    Remarks = remarks
                }
            }
            }
        };

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,                                // mantém PascalCase
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var json = JsonSerializer.Serialize(payload, jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var resp = await client.PostAsync("ApprovalRequestsService_UpdateApprovalRequest", content);

        // Logs pra depurar erros 400
        var respText = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        System.Diagnostics.Debug.WriteLine($"[SL][ApproveAsync] HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
        System.Diagnostics.Debug.WriteLine($"[SL][ApproveAsync] Req: {json}");
        System.Diagnostics.Debug.WriteLine($"[SL][ApproveAsync] Body: {respText}");

        return resp.IsSuccessStatusCode;
    }
    public async Task<bool> RejectAsync(int approvalRequestId, int stageCode, int userId, string? remarks)
    {
        if (!HasSlSession()) return false;
        using var client = CreateClientWithCookies(noCache: true);
        var payload = new
        {
            ApprovalRequest = new
            {
                ApprovalRequestID = approvalRequestId,
                ApprovalRequestDecisions = new[]
                {
                new { StageCode = stageCode, UserID = userId, Status = "ardRejected", Remarks = remarks }
            }
            }
        };
        var resp = await client.PostAsJsonAsync("ApprovalRequestsService_UpdateApprovalRequest", payload);
        return resp.IsSuccessStatusCode;
    }
    private readonly Dictionary<int, (string code, string? name)> _userCache = new();
    public async Task<ApprovalDetailsRaw?> GetApprovalDetailsAsync(int approvalRequestId)
    {
        using var client = CreateClientWithCookies(noCache: true);

        // 1) ApprovalRequest + linhas/estágios (para comentários/autor/etapa)
        var url = $"ApprovalRequests({approvalRequestId})" +
          "?$select=Code,ObjectType,ObjectEntry,OriginatorID,CreationDate,Status,Remarks,Priority" +
          "&$expand=ApprovalRequestDecisions";

        var resp = await client.GetAsync(url);
        var json = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(BuildSlError(json, resp));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Campos base
        var objectType = root.TryGetProperty("ObjectType", out var ot) && ot.ValueKind == JsonValueKind.String
            ? ot.GetString() : null;

        var objectEntry = root.TryGetProperty("ObjectEntry", out var oe) && oe.ValueKind == JsonValueKind.Number
            ? oe.GetInt32() : 0;

        var originatorIdStr = root.TryGetProperty("OriginatorID", out var orig) && orig.ValueKind != JsonValueKind.Null
            ? (orig.ValueKind == JsonValueKind.Number ? orig.GetInt32().ToString() : orig.GetString())
            : null;

        var createdAt = root.TryGetProperty("CreationDate", out var cd) && cd.ValueKind != JsonValueKind.Null
            ? (cd.ValueKind == JsonValueKind.String && DateTime.TryParse(cd.GetString(), out var dt) ? dt : DateTime.MinValue)
            : DateTime.MinValue;

        var status = root.TryGetProperty("Status", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() : null;

        // Nome do solicitante
        string? requesterName = null;
        if (int.TryParse(originatorIdStr, out var originatorUserId))
            requesterName = await ResolveUserNameByIdAsync(originatorUserId);

        var details = new ApprovalDetailsRaw
        {
            DocumentType = objectType,
            DocumentNumber = objectEntry.ToString(),
            RequesterName = requesterName,
            CreatedAt = createdAt,
            Status = status,
            Items = new List<ApprovalItemRaw>(),
            Comments = new List<ApprovalCommentRaw>(),
            Total = 0m
        };

        // 2) Comentários dos estágios (Remarks)
        if (root.TryGetProperty("ApprovalRequestDecisions", out var decs) && decs.ValueKind == JsonValueKind.Array)
        {
            foreach (var d in decs.EnumerateArray())
            {
                var rem = d.TryGetProperty("Remarks", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
                if (string.IsNullOrWhiteSpace(rem)) continue;

                string? author = null;
                int? uid = d.TryGetProperty("UserID", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetInt32() : (int?)null;
                if (uid.HasValue) author = await ResolveUserNameByIdAsync(uid.Value);
                if (string.IsNullOrEmpty(author))
                    author = d.TryGetProperty("ApproverUserName", out var aun) ? aun.GetString() : null;

                details.Comments!.Add(new ApprovalCommentRaw
                {
                    Author = author ?? "",
                    Date = createdAt, // se não houver data por decisão, mantém a do request; ajuste se achar um campo específico
                    Text = rem
                });
            }
        }

        // 3) Itens/total para Requisição de Compra (ObjectType 1470000113 = OPRQ)
        if (objectType == "1470000113" && objectEntry > 0)
        {
            var prUrl = $"PurchaseRequests({objectEntry})?$select=DocNum,DocTotal,CardName,DocTotalFc" +
                        "&$expand=DocumentLines($select=ItemCode,ItemDescription,Quantity,Price,U_TX_DescMatNfse)";
            var prResp = await client.GetAsync(prUrl);
            var prJson = await prResp.Content.ReadAsStringAsync();
            if (prResp.IsSuccessStatusCode)
            {
                using var prDoc = JsonDocument.Parse(prJson);
                var prRoot = prDoc.RootElement;

                if (prRoot.TryGetProperty("DocNum", out var dn) && dn.ValueKind != JsonValueKind.Null)
                    details.DocumentNumber = dn.ValueKind == JsonValueKind.Number ? dn.GetInt32().ToString() : dn.GetString();

                if (prRoot.TryGetProperty("DocTotal", out var dtot) && dtot.ValueKind == JsonValueKind.Number)
                    details.Total = dtot.GetDecimal();

                if (prRoot.TryGetProperty("DocTotalFc", out var dtotfc) && dtot.ValueKind == JsonValueKind.Number)
                    details.TotalFC = dtotfc.GetDecimal();

                if (prRoot.TryGetProperty("DocumentLines", out var lines) && lines.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ln in lines.EnumerateArray())
                    {
                        details.Items!.Add(new ApprovalItemRaw
                        {
                            ItemCode = ln.TryGetProperty("ItemCode", out var ic) ? ic.GetString() : "",
                            ItemName = ln.TryGetProperty("ItemDescription", out var idesc) ? idesc.GetString() : "",
                            Quantity = ln.TryGetProperty("Quantity", out var q) && q.ValueKind == JsonValueKind.Number ? q.GetDecimal() : 0m,
                            Price = ln.TryGetProperty("Price", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDecimal() : 0m
                        });
                    }
                }
            }
        }

        return details;
    }
    // helper: pega apenas o nome pelo ID do usuário
    // 1.1) Resolve StageID a partir do StageCode
    public async Task<int?> ResolveStageIdByCodeAsync(int stageCode, CancellationToken ct = default)
    {
        using var client = CreateClientWithCookies(noCache: true); // BaseAddress = .../b1s/v1/
                                                      // Campos e filtro mínimos; bound à collection ApprovalStages
        var url = $"ApprovalStages?$select=StageID,StageCode&$filter=StageCode eq {stageCode}";
        var resp = await client.GetAsync(url, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            var arr = doc.RootElement.TryGetProperty("value", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Array
                        ? v
                        : default;

            if (arr.ValueKind != System.Text.Json.JsonValueKind.Array || arr.GetArrayLength() == 0)
                return null;

            var first = arr[0];
            if (first.TryGetProperty("StageID", out var sid) && sid.TryGetInt32(out var id))
                return id;
        }
        catch { /* ignore */ }

        return null;
    }

    // 1.2) Envia a decisão usando UserID + StageID (forma mais aceita)
    public async Task<(bool ok, string? error)> PostUpdateApprovalRequest_FinalAsync(
        int approvalRequestId,
        int userId,
        int stageId,
        string status,                  // "ardApproved" | "ardRejected"
        string? remarks,
        CancellationToken ct = default)
    {
        using var client = CreateClientWithCookies(noCache: true); // BaseAddress = .../b1s/v1/
                                                      // Caminho relativo para aproveitar cookies (evita 401)
        var url = "ApprovalRequestsService_UpdateApprovalRequest";

        // Payload em PascalCase (conforme metadata clássico)
        string Escape(string? s) => s is null ? "null" : System.Text.Json.JsonSerializer.Serialize(s);

        var json = $@"
{{
  ""ApprovalRequest"": {{
    ""Code"": {approvalRequestId},
    ""ApprovalRequestDecisions"": [
      {{
        ""UserID"": {userId},
        ""StageID"": {stageId},
        ""Status"": ""{status}"",
        ""Remarks"": {Escape(remarks)}
      }}
    ]
  }}
}}".Trim();

        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var resp = await client.PostAsync(url, content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (resp.IsSuccessStatusCode) return (true, null);

        string Nice(string raw)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("error", out var e) &&
                    e.TryGetProperty("message", out var m))
                {
                    if (m.TryGetProperty("value", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String) return v.GetString()!;
                    if (m.ValueKind == System.Text.Json.JsonValueKind.String) return m.GetString()!;
                }
            }
            catch { }
            return $"{(int)resp.StatusCode} {resp.ReasonPhrase}";
        }

        return (false, Nice(body));
    }
    public async Task<(bool ok, string? error)> ApproveBySessionAsync(
    int approvalRequestId,
    string status,            // "ardApproved" | "ardRejected"
    string? remarks,
    string? approverUserName = null,   // ex.: "MANAGER" (opcional – ajuda em alguns builds)
    bool includeEmptyPassword = false, // se true, manda ApproverPassword: ""
    CancellationToken ct = default)
    {
        using var client = CreateClientWithCookies(noCache: true); // BaseAddress já é .../b1s/v1/
        var url = "ApprovalRequestsService_UpdateApprovalRequest"; // relativo (usa cookies)

        string Esc(string? s) => s is null ? "null" : System.Text.Json.JsonSerializer.Serialize(s);

        string decision =
            approverUserName is { Length: > 0 } && includeEmptyPassword
            ? $@"""ApproverUserName"": ""{approverUserName}"", ""ApproverPassword"": """", ""Status"": ""{status}"", ""Remarks"": {Esc(remarks)}"
            : approverUserName is { Length: > 0 }
                ? $@"""ApproverUserName"": ""{approverUserName}"", ""Status"": ""{status}"", ""Remarks"": {Esc(remarks)}"
                : $@"""Status"": ""{status}"", ""Remarks"": {Esc(remarks)}";

        var json = $@"
{{
  ""ApprovalRequest"": {{
    ""Code"": {approvalRequestId},
    ""ApprovalRequestDecisions"": [
      {{ {decision} }}
    ]
  }}
}}".Trim();

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp = await client.PostAsync(url, content, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);

        if (resp.IsSuccessStatusCode) return (true, null);

        // extrai mensagem amigável
        string Nice(string r)
        {
            try
            {
                using var d = System.Text.Json.JsonDocument.Parse(r);
                if (d.RootElement.TryGetProperty("error", out var e) &&
                    e.TryGetProperty("message", out var m))
                {
                    if (m.TryGetProperty("value", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String) return v.GetString()!;
                    if (m.ValueKind == System.Text.Json.JsonValueKind.String) return m.GetString()!;
                }
            }
            catch { }
            return $"{(int)resp.StatusCode} {resp.ReasonPhrase}";
        }
        return (false, Nice(raw));
    }
    public async Task<string> DebugDumpApprovalActionAsync(CancellationToken ct = default)
    {
        using var client = CreateClientWithCookies(noCache: true); // BaseAddress .../b1s/v1/
        var xml = await client.GetStringAsync("$metadata", ct);

        // Salva num arquivo local (opcional)
        try { System.IO.File.WriteAllText("sl_metadata.xml", xml); } catch { /* ignore */ }

        // Procura namespace principal (Schema Namespace="..."):
        string ns = "SAPB1";
        {
            var tag = "Schema Namespace=\"";
            var i = xml.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
            {
                i += tag.Length;
                var j = xml.IndexOf("\"", i, StringComparison.Ordinal);
                if (j > i) ns = xml.Substring(i, j - i);
            }
        }

        // Procura por qualquer coisa com "ApprovalRequest" e "Update"
        // Ex.: <Action Name="UpdateApprovalRequest" IsBound="true">
        //      <FunctionImport Name="ApprovalRequestsService_UpdateApprovalRequest" Function="SAPB1.ApprovalRequestsService_UpdateApprovalRequest">
        bool isBound = false;
        string actionName = null!;
        string functionImport = null!;
        string functionFull = null!;
        string bindingParamType = null!;
        var bndIndex = xml.IndexOf("Action Name=\"UpdateApprovalRequest\"", StringComparison.OrdinalIgnoreCase);
        if (bndIndex >= 0)
        {
            // checa IsBound
            var isb = xml.IndexOf("IsBound=\"true\"", bndIndex, StringComparison.OrdinalIgnoreCase);
            isBound = isb > 0 && isb < bndIndex + 400; // heurística curta
            actionName = "UpdateApprovalRequest";

            // binding parameter (primeiro <Parameter ... Type="NS.ApprovalRequest"...)
            var pTag = "Parameter Name=\"";
            var pIdx = xml.IndexOf(pTag, bndIndex, StringComparison.OrdinalIgnoreCase);
            if (pIdx > 0)
            {
                var tTag = "Type=\"";
                var tIdx = xml.IndexOf(tTag, pIdx, StringComparison.OrdinalIgnoreCase);
                if (tIdx > 0)
                {
                    tIdx += tTag.Length;
                    var tEnd = xml.IndexOf("\"", tIdx, StringComparison.Ordinal);
                    if (tEnd > tIdx) bindingParamType = xml.Substring(tIdx, tEnd - tIdx);
                }
            }
        }

        // Procura function import com "ApprovalRequestsService_UpdateApprovalRequest"
        var fiIdx = xml.IndexOf("FunctionImport Name=\"ApprovalRequestsService_UpdateApprovalRequest\"", StringComparison.OrdinalIgnoreCase);
        if (fiIdx >= 0)
        {
            // extrai Function="NS.Xxx"
            var fTag = "Function=\"";
            var fPos = xml.IndexOf(fTag, fiIdx, StringComparison.OrdinalIgnoreCase);
            if (fPos > 0)
            {
                fPos += fTag.Length;
                var fEnd = xml.IndexOf("\"", fPos, StringComparison.Ordinal);
                if (fEnd > fPos)
                {
                    functionFull = xml.Substring(fPos, fEnd - fPos); // ex: SAPB1.ApprovalRequestsService_UpdateApprovalRequest
                    functionImport = "ApprovalRequestsService_UpdateApprovalRequest";
                }
            }
        }

        // Descobre o nome do EntitySet de ApprovalRequests (geralmente "ApprovalRequests")
        // <EntitySet Name="ApprovalRequests" EntityType="NS.ApprovalRequest">
        string entitySet = "ApprovalRequests";
        {
            var eTag = "EntitySet Name=\"";
            int start = 0;
            while (true)
            {
                var ei = xml.IndexOf(eTag, start, StringComparison.OrdinalIgnoreCase);
                if (ei < 0) break;
                var nameStart = ei + eTag.Length;
                var nameEnd = xml.IndexOf("\"", nameStart, StringComparison.Ordinal);
                if (nameEnd < 0) break;
                var name = xml.Substring(nameStart, nameEnd - nameStart);

                var etTag = "EntityType=\"";
                var etPos = xml.IndexOf(etTag, nameEnd, StringComparison.OrdinalIgnoreCase);
                if (etPos < 0) { start = nameEnd; continue; }
                etPos += etTag.Length;
                var etEnd = xml.IndexOf("\"", etPos, StringComparison.Ordinal);
                if (etEnd < 0) { start = etPos; continue; }
                var et = xml.Substring(etPos, etEnd - etPos); // ex: SAPB1.ApprovalRequest

                if (et.EndsWith(".ApprovalRequest", StringComparison.Ordinal))
                {
                    entitySet = name; break;
                }
                start = etEnd;
            }
        }

        // Monta um resumo útil
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Namespace: {ns}");
        sb.AppendLine($"EntitySet de ApprovalRequests: {entitySet}");
        sb.AppendLine($"Action bound 'UpdateApprovalRequest' encontrada?: {(actionName != null ? "sim" : "não")}");
        sb.AppendLine($" - IsBound: {isBound}");
        sb.AppendLine($" - Binding Parameter Type: {bindingParamType}");
        sb.AppendLine($"FunctionImport 'ApprovalRequestsService_UpdateApprovalRequest' encontrado?: {(functionImport != null ? "sim" : "não")}");
        sb.AppendLine($" - Function: {functionFull}");

        // Sugere endpoints:
        sb.AppendLine("Endpoints candidatos:");
        if (actionName != null)
        {
            // considerar namespace no caminho para ação bound
            sb.AppendLine($"  1) {entitySet}({{Code}})/{ns}.{actionName}");
            sb.AppendLine($"  2) {entitySet}({{Code}})/{actionName}");
        }
        if (functionImport != null)
        {
            sb.AppendLine($"  3) {functionImport}");
        }

        // Observação sobre parâmetros
        sb.AppendLine("Corpos candidatos:");
        sb.AppendLine("  A) Bound minimal params (se a bound action tiver parâmetros explícitos): { \"ApprovalRequestDecisions\": [ { ... } ] }");
        sb.AppendLine("  B) Import c/ wrapper Pascal: { \"ApprovalRequest\": { \"Code\": <id>, \"ApprovalRequestDecisions\": [ { ... } ] } }");
        sb.AppendLine("  C) Import raiz Pascal:       { \"Code\": <id>, \"ApprovalRequestDecisions\": [ { ... } ] }");

        return sb.ToString();
    }
    public async Task<(bool ok, string? error)> ApproveAutoAsync(
    int approvalRequestId,
    string status,            // "ardApproved" | "ardRejected"
    string? remarks,
    string approverUserName,  // seu SAP_USERCODE (ajuda se o SL exigir)
    CancellationToken ct = default)
    {
        using var client = CreateClientWithCookies(noCache: true);

        string meta = await DebugDumpApprovalActionAsync(ct);

        // extrai heurística simples das linhas
        string ns = "SAPB1", entitySet = "ApprovalRequests";
        bool hasBound = meta.Contains("Action bound 'UpdateApprovalRequest' encontrada?: sim", StringComparison.OrdinalIgnoreCase);
        bool hasImport = meta.Contains("FunctionImport 'ApprovalRequestsService_UpdateApprovalRequest' encontrado?: sim", StringComparison.OrdinalIgnoreCase);

        // Namespace:
        {
            var k = "Namespace: ";
            var i = meta.IndexOf(k, StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
            {
                i += k.Length;
                var j = meta.IndexOf("\n", i, StringComparison.Ordinal);
                if (j > i) ns = meta.Substring(i, j - i).Trim();
            }
        }
        // EntitySet:
        {
            var k = "EntitySet de ApprovalRequests: ";
            var i = meta.IndexOf(k, StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
            {
                i += k.Length;
                var j = meta.IndexOf("\n", i, StringComparison.Ordinal);
                if (j > i) entitySet = meta.Substring(i, j - i).Trim();
            }
        }

        // Endpoints candidatos (só relativos!)
        var endpoints = new List<string>();
        if (hasBound)
        {
            endpoints.Add($"{entitySet}({approvalRequestId})/{ns}.UpdateApprovalRequest");
            endpoints.Add($"{entitySet}({approvalRequestId})/UpdateApprovalRequest");
        }
        if (hasImport)
        {
            endpoints.Add("ApprovalRequestsService_UpdateApprovalRequest");
        }
        if (endpoints.Count == 0)
        {
            // fallback bruto
            endpoints.Add($"{entitySet}({approvalRequestId})/{ns}.UpdateApprovalRequest");
            endpoints.Add("ApprovalRequestsService_UpdateApprovalRequest");
        }

        // Payloads compatíveis (sem Stage/User/StageID — deixe o SL inferir pela sessão+pendência)
        string Esc(string? s) => s is null ? "null" : System.Text.Json.JsonSerializer.Serialize(s);

        var bodies = new (string label, string json)[]
        {
        // A) minimal (bound)
        ("A-minimal",
         $@"{{ ""ApprovalRequestDecisions"": [ {{ ""Status"": ""{status}"", ""Remarks"": {Esc(remarks)} }} ] }}"),

        // B) com ApproverUserName (bound)
        ("B-user",
         $@"{{ ""ApprovalRequestDecisions"": [ {{ ""ApproverUserName"": ""{approverUserName}"", ""Status"": ""{status}"", ""Remarks"": {Esc(remarks)} }} ] }}"),

        // C) import c/ wrapper Pascal
        ("C-import-wrapper",
         $@"{{ ""ApprovalRequest"": {{ ""Code"": {approvalRequestId}, ""ApprovalRequestDecisions"": [ {{ ""Status"": ""{status}"", ""Remarks"": {Esc(remarks)} }} ] }} }}"),

        // D) import c/ wrapper Pascal + user
        ("D-import-wrapper-user",
         $@"{{ ""ApprovalRequest"": {{ ""Code"": {approvalRequestId}, ""ApprovalRequestDecisions"": [ {{ ""ApproverUserName"": ""{approverUserName}"", ""Status"": ""{status}"", ""Remarks"": {Esc(remarks)} }} ] }} }}")
        };

        static string Nice(string raw, HttpResponseMessage resp)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var e) &&
                    e.TryGetProperty("message", out var m))
                {
                    if (m.TryGetProperty("value", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String) return v.GetString()!;
                    if (m.ValueKind == System.Text.Json.JsonValueKind.String) return m.GetString()!;
                }
            }
            catch { }
            return $"{(int)resp.StatusCode} {resp.ReasonPhrase}";
        }

        var attempts = new System.Text.StringBuilder();

        foreach (var ep in endpoints)
        {
            foreach (var (label, json) in bodies)
            {
                // bound actions aceitam A/B; imports aceitam C/D (mas testamos todos por via das dúvidas)
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var resp = await client.PostAsync(ep, content, ct);
                var raw = await resp.Content.ReadAsStringAsync(ct);
                if (resp.IsSuccessStatusCode) return (true, null);

                attempts.AppendLine($"EP={ep} {label}: {Nice(raw, resp)}");
            }
        }

        return (false, "UpdateApprovalRequest falhou.\n" + attempts.ToString());
    }
    private async Task<string?> ResolveUserNameByIdAsync(int userId)
    {
        using var client = CreateClientWithCookies(noCache: true);
        var url = $"Users?$select=InternalKey,UserName&$filter=InternalKey eq {userId}";
        var resp = await client.GetAsync(url);
        var json = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var row in arr.EnumerateArray())
            return row.TryGetProperty("UserName", out var un) ? un.GetString() : null;

        return null;
    }
    public class ApprovalDetailsRaw
    {
        public string? DocumentType { get; set; }
        public string? DocumentNumber { get; set; }
        public string? RequesterName { get; set; }
        public DateTime CreatedAt { get; set; }
        public decimal Total { get; set; }
        public decimal TotalFC { get; set; }
        public string? Status { get; set; }
        public List<ApprovalItemRaw>? Items { get; set; }
        public List<ApprovalCommentRaw>? Comments { get; set; }
    }
    public class ApprovalItemRaw
    {
        public string? ItemCode { get; set; }
        public string? ItemName { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
    }
    public class ApprovalCommentRaw
    {
        public string? Author { get; set; }
        public DateTime Date { get; set; }
        public string? Text { get; set; }
    }
    public class ApprovalRequestDetailsDto
    {
        public int ApprovalRequestID { get; set; }
        public string? Status { get; set; }
        public string? Remarks { get; set; }
        public decimal? DocTotal { get; set; }
        public decimal? DocTotalFC { get; set; }
        public string? CardName { get; set; }
        public DateTime? CreationDate { get; set; }
        public int? PendingStageCode { get; set; }
        public int? PendingUserID { get; set; }
        public string? PendingUserCode { get; set; }
        public string? PendingUserName { get; set; }

        public List<ApprovalRequestDecisionDto>? ApprovalRequestDecisions { get; set; }
        public List<ApprovalRequestLineDto>? ApprovalRequestLines { get; set; }
        public string? RequesterName { get; set; }   // <- NOVO
        public string? DocNum { get; set; }          // opcional
        public List<DocLineDto>? DocumentLines { get; set; }  // <- NOVO
        public string? DocObjCode { get; internal set; }
        public int? ApprovalTemplatesID { get; set; }  // versão “plural”
        public int? ApprovalTemplateID { get; set; }  // versão “singular”
        public int? DraftDocNum { get; set; }
        public List<SlAttachmentLine> Attachments { get; set; } = new();
    }
    public async Task<ApprovalRequestDetailsDto?> GetApprovalRequestDetailsAsync(int code, int? currentUserId = null)
    {
        using var client = CreateClientWithCookies(noCache: true);

        // ApprovalRequest "completo"
        var ar = await GetApprovalRequestFullAsync(client, code);
        if (ar is null) return null;

        var root = ar.Value;

        var dto = new ApprovalRequestDetailsDto
        {
            ApprovalRequestID = code,
            Status = root.TryGetProperty("Status", out var st) ? st.GetString() : null,
            Remarks = root.TryGetProperty("Remarks", out var rem) ? rem.GetString() : null,
            CreationDate = root.TryGetProperty("CreationDate", out var cd)
                           && cd.ValueKind == JsonValueKind.String
                           && DateTime.TryParse(cd.GetString(), out var dt) ? dt : (DateTime?)null,
            ApprovalRequestDecisions = new List<ApprovalRequestDecisionDto>(),
            ApprovalRequestLines = new List<ApprovalRequestLineDto>(),
            DocumentLines = new List<DocLineDto>(),
            // 👇 garante a lista de anexos
            Attachments = new List<SlAttachmentLine>()
        };

        // ---- ApprovalRequestDecisions do payload principal (se vierem)
        if (root.TryGetProperty("ApprovalRequestDecisions", out var decEl) &&
            decEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in decEl.EnumerateArray())
            {
                dto.ApprovalRequestDecisions!.Add(new ApprovalRequestDecisionDto
                {
                    StageCode = el.TryGetProperty("StageCode", out var sc) && sc.ValueKind == JsonValueKind.Number ? sc.GetInt32() : (int?)null,
                    UserID = el.TryGetProperty("UserID", out var uid) && uid.ValueKind == JsonValueKind.Number ? uid.GetInt32() : (int?)null,
                    Status = el.TryGetProperty("Status", out var s) ? s.GetString() : null,
                    Remarks = el.TryGetProperty("Remarks", out var rk) ? rk.GetString() : null
                });
            }
        }

        // ---- ApprovalRequestLines do payload principal (se vierem)
        if (root.TryGetProperty("ApprovalRequestLines", out var linesEl) &&
            linesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var ln in linesEl.EnumerateArray())
            {
                dto.ApprovalRequestLines!.Add(new ApprovalRequestLineDto
                {
                    StageCode = ln.TryGetProperty("StageCode", out var lsc) && lsc.ValueKind == JsonValueKind.Number ? lsc.GetInt32() : (int?)null,
                    UserID = ln.TryGetProperty("UserID", out var luid) && luid.ValueKind == JsonValueKind.Number ? luid.GetInt32() : (int?)null,
                    Status = ln.TryGetProperty("Status", out var ls) ? ls.GetString() : null,
                    Remarks = ln.TryGetProperty("Remarks", out var lrm) ? lrm.GetString() : null,
                    UpdateDate = ln.TryGetProperty("UpdateDate", out var lud) && lud.ValueKind == JsonValueKind.String && DateTime.TryParse(lud.GetString(), out var ludt) ? ludt : (DateTime?)null,
                    UpdateTime = ln.TryGetProperty("UpdateTime", out var lut) ? lut.GetString() : null,
                    CreationDate = ln.TryGetProperty("CreationDate", out var lcd2) && lcd2.ValueKind == JsonValueKind.String && DateTime.TryParse(lcd2.GetString(), out var lcdt2) ? lcdt2 : (DateTime?)null,
                    CreationTime = ln.TryGetProperty("CreationTime", out var lct2) ? lct2.GetString() : null
                });
            }
        }

        // ---- Decisions via navegação (aceita vazio)
        try
        {
            var urlNavJson = $"ApprovalRequests({code})/ApprovalRequestDecisions?$format=json";
            var rNavJson = await client.GetAsync(urlNavJson);
            var jNavJson = await rNavJson.Content.ReadAsStringAsync();

            if (rNavJson.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(jNavJson);
                var rootJson = doc.RootElement;

                JsonElement decArr = default;
                if (rootJson.TryGetProperty("ApprovalRequestDecisions", out var decProp) && decProp.ValueKind == JsonValueKind.Array)
                    decArr = decProp;
                else if (rootJson.TryGetProperty("value", out var valArr) && valArr.ValueKind == JsonValueKind.Array)
                    decArr = valArr;
                else if (rootJson.TryGetProperty("d", out var dObj) && dObj.TryGetProperty("results", out var resArr) && resArr.ValueKind == JsonValueKind.Array)
                    decArr = resArr;
                else if (rootJson.ValueKind == JsonValueKind.Array)
                    decArr = rootJson;

                if (decArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in decArr.EnumerateArray())
                    {
                        dto.ApprovalRequestDecisions!.Add(new ApprovalRequestDecisionDto
                        {
                            StageCode = el.TryGetProperty("StageCode", out var sc2) && sc2.ValueKind == JsonValueKind.Number ? sc2.GetInt32() : (int?)null,
                            StageID = el.TryGetProperty("StageID", out var si) && si.ValueKind == JsonValueKind.Number ? si.GetInt32() : (int?)null,
                            UserID = el.TryGetProperty("UserID", out var uid2) && uid2.ValueKind == JsonValueKind.Number ? uid2.GetInt32() : (int?)null,
                            Status = el.TryGetProperty("Status", out var s2) ? s2.GetString() : null,
                            Remarks = el.TryGetProperty("Remarks", out var rk2) ? rk2.GetString() : null
                        });
                    }
                }
            }
        }
        catch
        {
            // ignora e segue com derivação
        }

        dto.ApprovalTemplatesID =
            root.TryGetProperty("ApprovalTemplatesID", out var atid) && atid.ValueKind == JsonValueKind.Number
                ? atid.GetInt32()
                : (int?)null;

        dto.ApprovalTemplateID =
            root.TryGetProperty("ApprovalTemplateID", out var atid2) && atid2.ValueKind == JsonValueKind.Number
                ? atid2.GetInt32()
                : (int?)null;

        // ---- Descobrir pendência (ordem de preferência)

        // 1) Decision ardPending (se existir)
        var pendFromDec1 = dto.ApprovalRequestDecisions!.FirstOrDefault(d => d.Status == "ardPending");
        if (pendFromDec1?.StageCode != null) dto.PendingStageCode = pendFromDec1.StageCode.Value;
        if (pendFromDec1?.UserID != null) dto.PendingUserID = pendFromDec1.UserID.Value;

        // 2) Lines ardPending (por usuário atual, se informado; senão a primeira)
        if ((!dto.PendingStageCode.HasValue || !dto.PendingUserID.HasValue) && dto.ApprovalRequestLines!.Count > 0)
        {
            var pendFromLines = (currentUserId.HasValue
                ? dto.ApprovalRequestLines.FirstOrDefault(l => l.Status == "ardPending" && l.UserID == currentUserId.Value)
                : dto.ApprovalRequestLines.FirstOrDefault(l => l.Status == "ardPending"));

            if (pendFromLines?.StageCode != null) dto.PendingStageCode ??= pendFromLines.StageCode.Value;
            if (pendFromLines?.UserID != null) dto.PendingUserID ??= pendFromLines.UserID.Value;
        }

        // 3) Fallback: CurrentStage
        if (!dto.PendingStageCode.HasValue &&
            root.TryGetProperty("CurrentStage", out var curStageEl) &&
            curStageEl.ValueKind == JsonValueKind.Number)
        {
            dto.PendingStageCode = curStageEl.GetInt32();
        }

        // 4) Se o request está pendente, amarre PendingUserID ao currentUserId (último recurso)
        if (string.Equals(dto.Status, "arsPending", StringComparison.OrdinalIgnoreCase) && currentUserId.HasValue)
            dto.PendingUserID ??= currentUserId.Value;

        // ---- Solicitante (OriginatorID → UserCode)
        if (root.TryGetProperty("OriginatorID", out var orig) && orig.ValueKind == JsonValueKind.Number)
            dto.RequesterName = await ResolveUserNameByIdAsync(orig.GetInt32());

        // ---- Documento de origem (definitivo ou rascunho)
        string? objectType = root.TryGetProperty("ObjectType", out var ot) ? ot.GetString() : null;
        int? objectEntry = root.TryGetProperty("ObjectEntry", out var oe) && oe.ValueKind == JsonValueKind.Number ? oe.GetInt32() : (int?)null;

        bool isDraft = root.TryGetProperty("IsDraft", out var isDraftEl) &&
                       string.Equals(isDraftEl.GetString(), "Y", StringComparison.OrdinalIgnoreCase);
        int? draftEntry = root.TryGetProperty("DraftEntry", out var de) && de.ValueKind == JsonValueKind.Number ? de.GetInt32() : (int?)null;

        try
        {
            // ------------------------ DOCUMENTO DEFINITIVO ------------------------
            if (objectEntry.HasValue && !string.IsNullOrEmpty(objectType))
            {
                async Task LoadHeaderAndLinesAsync(string entityName)
                {
                    // ---------- Cabeçalho: inclui AtcEntry para pegar anexos ----------
                    var headUrl = $"{entityName}({objectEntry.Value})?$select=DocNum,CardName,DocTotal,DocTotalFc,AtcEntry";
                    var hResp = await client.GetAsync(headUrl);
                    var hJson = await hResp.Content.ReadAsStringAsync();
                    if (hResp.IsSuccessStatusCode)
                    {
                        using var hDoc = JsonDocument.Parse(hJson);
                        var hRoot = hDoc.RootElement;

                        if (hRoot.TryGetProperty("DocNum", out var dn) && dn.ValueKind != JsonValueKind.Null)
                            dto.DocNum = dn.ValueKind == JsonValueKind.Number ? dn.GetInt32().ToString() : dn.GetString();

                        if (hRoot.TryGetProperty("CardName", out var cn))
                            dto.CardName = cn.GetString();

                        if (hRoot.TryGetProperty("DocTotal", out var tot) && tot.ValueKind == JsonValueKind.Number)
                            dto.DocTotal = tot.GetDecimal();

                        if (hRoot.TryGetProperty("DocTotalFc", out var totfc) && totfc.ValueKind == JsonValueKind.Number)
                            dto.DocTotalFC = totfc.GetDecimal();

                        // 👇 NOVO: tenta pegar AtcEntry do doc definitivo
                        if (hRoot.TryGetProperty("AtcEntry", out var atcEl) && atcEl.ValueKind == JsonValueKind.Number)
                        {
                            var atcEntry = atcEl.GetInt32();
                            dto.Attachments = await GetAttachmentsAsync(atcEntry);
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[SL] GET {headUrl} -> {(int)hResp.StatusCode} {hResp.ReasonPhrase}\n{hJson}");

                        // Fallback sem select: tenta também extrair AtcEntry daqui
                        var headUrl2 = $"{entityName}({objectEntry.Value})";
                        var hResp2 = await client.GetAsync(headUrl2);
                        var hJson2 = await hResp2.Content.ReadAsStringAsync();

                        if (hResp2.IsSuccessStatusCode)
                        {
                            using var hDoc2 = JsonDocument.Parse(hJson2);
                            var hRoot2 = hDoc2.RootElement;

                            if (hRoot2.TryGetProperty("DocNum", out var dn2) && dn2.ValueKind != JsonValueKind.Null)
                                dto.DocNum = dn2.ValueKind == JsonValueKind.Number ? dn2.GetInt32().ToString() : dn2.GetString();

                            if (hRoot2.TryGetProperty("CardName", out var cn2))
                                dto.CardName = cn2.GetString();

                            if (hRoot2.TryGetProperty("DocTotal", out var tot2) && tot2.ValueKind == JsonValueKind.Number)
                                dto.DocTotal = tot2.GetDecimal();

                            if (hRoot2.TryGetProperty("DocTotalFc", out var totfc2) && totfc2.ValueKind == JsonValueKind.Number)
                                dto.DocTotalFC = totfc2.GetDecimal();

                            if (hRoot2.TryGetProperty("AtcEntry", out var atc2) && atc2.ValueKind == JsonValueKind.Number)
                            {
                                var atcEntry2 = atc2.GetInt32();
                                dto.Attachments = await GetAttachmentsAsync(atcEntry2);
                            }
                        }
                    }

                    // ---------- Parser genérico de coleções de linhas ----------
                    void ParseLinesFromJson(string json)
                    {
                        using var doc = JsonDocument.Parse(json);
                        var rootAll = doc.RootElement;

                        // Tenta extrair a array
                        bool TryGetArray(JsonElement src, out JsonElement arr)
                        {
                            // 1) value[]
                            if (src.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array)
                            { arr = v; return true; }

                            // 2) d.results[]
                            if (src.TryGetProperty("d", out var d) &&
                                d.ValueKind == JsonValueKind.Object &&
                                d.TryGetProperty("results", out var rs) &&
                                rs.ValueKind == JsonValueKind.Array)
                            { arr = rs; return true; }

                            // 3) DocumentLines (array ou results)
                            if (src.TryGetProperty("DocumentLines", out var dl))
                            {
                                if (dl.ValueKind == JsonValueKind.Array) { arr = dl; return true; }
                                if (dl.ValueKind == JsonValueKind.Object &&
                                    dl.TryGetProperty("results", out var rs2) &&
                                    rs2.ValueKind == JsonValueKind.Array)
                                { arr = rs2; return true; }
                            }

                            // 4) Alguns endpoints retornam a coleção diretamente como array
                            if (src.ValueKind == JsonValueKind.Array)
                            { arr = src; return true; }

                            arr = default;
                            return false;
                        }

                        if (!TryGetArray(rootAll, out var arr)) return;

                        foreach (var ln in arr.EnumerateArray())
                        {
                            var desc = ln.TryGetProperty("ItemDescription", out var idesc) ? idesc.GetString()
                                     : (ln.TryGetProperty("ItemName", out var iname) ? iname.GetString() : null);

                            decimal qty = (ln.TryGetProperty("Quantity", out var q) && q.ValueKind == JsonValueKind.Number)
                                          ? q.GetDecimal() : 0m;

                            decimal price = 0m;
                            if (ln.TryGetProperty("Price", out var p) && p.ValueKind == JsonValueKind.Number)
                                price = p.GetDecimal();
                            else if (ln.TryGetProperty("UnitPrice", out var up) && up.ValueKind == JsonValueKind.Number)
                                price = up.GetDecimal();

                            string? matNfse = ln.TryGetProperty("U_TX_DescMatNfse", out var udf) ? udf.GetString() : null;

                            dto.DocumentLines!.Add(new DocLineDto
                            {
                                ItemCode = ln.TryGetProperty("ItemCode", out var ic) ? ic.GetString() : null,
                                ItemName = desc,
                                ItemDescription = desc,
                                Quantity = qty,
                                Price = price,
                                U_TX_DescMatNfse = matNfse
                            });
                        }
                    }

                    // ---------- Tenta uma série de URLs, na ordem ----------
                    var urls = new List<string>
                {
                    $"{entityName}({objectEntry.Value})/DocumentLines?$select=ItemCode,ItemDescription,ItemName,Quantity,Price,UnitPrice,U_TX_DescMatNfse",
                    $"{entityName}({objectEntry.Value})/DocumentLines?$format=json",
                    $"{entityName}({objectEntry.Value})/DocumentLines",
                    $"{entityName}({objectEntry.Value})/Lines?$select=ItemCode,ItemDescription,ItemName,Quantity,Price,UnitPrice,U_TX_DescMatNfse",
                    $"{entityName}({objectEntry.Value})/Lines?$format=json",
                    $"{entityName}({objectEntry.Value})/Lines",
                    $"{entityName}({objectEntry.Value})?$expand=DocumentLines",
                    $"{entityName}({objectEntry.Value})?$expand=Lines",
                    $"{entityName}({objectEntry.Value})"
                };

                    if (dto.DocumentLines == null) dto.DocumentLines = new List<DocLineDto>();

                    foreach (var url in urls)
                    {
                        var resp = await client.GetAsync(url);
                        var txt = await resp.Content.ReadAsStringAsync();

                        System.Diagnostics.Debug.WriteLine($"[SL] GET {url} -> {(int)resp.StatusCode} {resp.ReasonPhrase}");

                        if (!resp.IsSuccessStatusCode)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SL] Body:\n{txt}");
                            continue;
                        }

                        int before = dto.DocumentLines.Count;
                        ParseLinesFromJson(txt);
                        int added = dto.DocumentLines.Count - before;

                        if (added > 0) break;
                    }
                }

                if (objectType == "1470000113")       // PurchaseRequest
                    await LoadHeaderAndLinesAsync("PurchaseRequests");
                else if (objectType == "22")          // PurchaseOrder
                    await LoadHeaderAndLinesAsync("PurchaseOrders");
            }
            // ------------------------ RASCUNHO (DRAFT) ------------------------
            else if (isDraft && draftEntry.HasValue)
            {
                if (dto.DocumentLines == null) dto.DocumentLines = new List<DocLineDto>();

                // 1) Cabeçalho do rascunho (pega CardName, DocTotal, DocObjectCode, e tenta pegar AttachmentEntry)
                async Task LoadDraftHeaderAsync()
                {
                    var headUrl = $"Drafts({draftEntry.Value})?$select=DocObjectCode,CardName,DocTotal,DocTotalFc,AtcEntry,AttachmentEntry";
                    var hResp = await client.GetAsync(headUrl);
                    var hJson = await hResp.Content.ReadAsStringAsync();

                    System.Diagnostics.Debug.WriteLine($"[SL] Draft header GET {headUrl} -> {(int)hResp.StatusCode} {hResp.ReasonPhrase}");
                    System.Diagnostics.Debug.WriteLine($"[SL] Draft header JSON:\n{hJson}");

                    if (!hResp.IsSuccessStatusCode)
                    {
                        // fallback sem select
                        var headUrl2 = $"Drafts({draftEntry.Value})";
                        hResp = await client.GetAsync(headUrl2);
                        hJson = await hResp.Content.ReadAsStringAsync();
                        System.Diagnostics.Debug.WriteLine($"[SL] Draft header fallback GET {headUrl2} -> {(int)hResp.StatusCode} {hResp.ReasonPhrase}");
                        System.Diagnostics.Debug.WriteLine($"[SL] Draft header fallback JSON:\n{hJson}");
                    }

                    if (hResp.IsSuccessStatusCode)
                    {
                        using var hDoc = JsonDocument.Parse(hJson);
                        var rootH = hDoc.RootElement;

                        // Alguns retornos vêm como { "value": [ { ... } ] }
                        if (rootH.TryGetProperty("value", out var hv) && hv.ValueKind == JsonValueKind.Array && hv.GetArrayLength() > 0)
                            rootH = hv[0];

                        if (rootH.TryGetProperty("CardName", out var cn))
                            dto.CardName = cn.GetString();

                        if (rootH.TryGetProperty("DocTotal", out var tot) && tot.ValueKind != JsonValueKind.Null)
                        {
                            if (tot.ValueKind == JsonValueKind.Number && tot.TryGetDecimal(out var n))
                                dto.DocTotal = n;
                            else if (tot.ValueKind == JsonValueKind.String &&
                                     decimal.TryParse(tot.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s))
                                dto.DocTotal = s;
                        }

                        if (rootH.TryGetProperty("DocTotalFc", out var totfc) && totfc.ValueKind != JsonValueKind.Null)
                        {
                            if (totfc.ValueKind == JsonValueKind.Number && totfc.TryGetDecimal(out var n2))
                                dto.DocTotalFC = n2;
                            else if (totfc.ValueKind == JsonValueKind.String &&
                                     decimal.TryParse(totfc.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s2))
                                dto.DocTotalFC = s2;
                        }

                        if (rootH.TryGetProperty("DocObjectCode", out var docObj) && docObj.ValueKind == JsonValueKind.String)
                            dto.DocObjCode = docObj.GetString();

                        // 👉 AQUI É A "VALIDAÇÃO SE EXISTE ANEXO" NO DRAFT
                        int? atcEntry = null;

                        if (rootH.TryGetProperty("AtcEntry", out var atcEl) && atcEl.ValueKind == JsonValueKind.Number)
                            atcEntry = atcEl.GetInt32();
                        else if (rootH.TryGetProperty("AttachmentEntry", out var attEl) && attEl.ValueKind == JsonValueKind.Number)
                            atcEntry = attEl.GetInt32();

                        if (atcEntry.HasValue)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SL] Draft {draftEntry.Value} com AttachmentEntry = {atcEntry.Value}");
                            dto.Attachments = await GetAttachmentsAsync(atcEntry.Value);
                            System.Diagnostics.Debug.WriteLine($"[SL] Draft {draftEntry.Value} -> dto.Attachments.Count = {dto.Attachments.Count}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[SL] Draft {draftEntry.Value} sem AtcEntry/AttachmentEntry no header.");
                        }
                    }
                }

                void ParseDraftLines(string json)
                {
                    using var doc = JsonDocument.Parse(json);
                    var rootD = doc.RootElement;

                    bool TryGetArray(JsonElement src, out JsonElement arr)
                    {
                        if (src.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array)
                        { arr = v; return true; }

                        if (src.TryGetProperty("d", out var d) &&
                            d.ValueKind == JsonValueKind.Object &&
                            d.TryGetProperty("results", out var rs) &&
                            rs.ValueKind == JsonValueKind.Array)
                        { arr = rs; return true; }

                        if (src.TryGetProperty("DocumentLines", out var dl))
                        {
                            if (dl.ValueKind == JsonValueKind.Array) { arr = dl; return true; }
                            if (dl.ValueKind == JsonValueKind.Object &&
                                dl.TryGetProperty("results", out var rs2) &&
                                rs2.ValueKind == JsonValueKind.Array)
                            { arr = rs2; return true; }
                        }

                        if (src.TryGetProperty("Lines", out var dl3))
                        {
                            if (dl3.ValueKind == JsonValueKind.Array) { arr = dl3; return true; }
                            if (dl3.ValueKind == JsonValueKind.Object &&
                                dl3.TryGetProperty("results", out var rs3) &&
                                rs3.ValueKind == JsonValueKind.Array)
                            { arr = rs3; return true; }
                        }

                        if (src.ValueKind == JsonValueKind.Array) { arr = src; return true; }

                        arr = default;
                        return false;
                    }

                    if (!TryGetArray(rootD, out var arrLines)) return;

                    foreach (var ln in arrLines.EnumerateArray())
                    {
                        var desc = ln.TryGetProperty("ItemDescription", out var idesc) ? idesc.GetString()
                                 : (ln.TryGetProperty("ItemName", out var iname) ? iname.GetString() : null);

                        decimal qty = (ln.TryGetProperty("Quantity", out var q) && q.ValueKind == JsonValueKind.Number) ? q.GetDecimal() : 0m;

                        decimal price = 0m;
                        if (ln.TryGetProperty("Price", out var p) && p.ValueKind == JsonValueKind.Number) price = p.GetDecimal();
                        else if (ln.TryGetProperty("UnitPrice", out var up) && up.ValueKind == JsonValueKind.Number) price = up.GetDecimal();

                        string? mat = ln.TryGetProperty("U_TX_DescMatNfse", out var udf) ? udf.GetString() : null;

                        dto.DocumentLines!.Add(new DocLineDto
                        {
                            ItemCode = ln.TryGetProperty("ItemCode", out var ic) ? ic.GetString() : null,
                            ItemName = desc,
                            ItemDescription = desc,
                            Quantity = qty,
                            Price = price,
                            U_TX_DescMatNfse = mat
                        });
                    }
                }

                async Task LoadDraftLinesAsync()
                {
                    var urls = new[]
                    {
                    $"Drafts({draftEntry.Value})/DocumentLines?$select=ItemCode,ItemDescription,ItemName,Quantity,Price,UnitPrice,U_TX_DescMatNfse",
                    $"Drafts({draftEntry.Value})/DocumentLines?$format=json",
                    $"Drafts({draftEntry.Value})/DocumentLines",
                    $"Drafts({draftEntry.Value})/Lines?$select=ItemCode,ItemDescription,ItemName,Quantity,Price,UnitPrice,U_TX_DescMatNfse",
                    $"Drafts({draftEntry.Value})/Lines?$format=json",
                    $"Drafts({draftEntry.Value})/Lines",
                    $"Drafts({draftEntry.Value})?$expand=DocumentLines",
                    $"Drafts({draftEntry.Value})?$expand=Lines",
                    $"Drafts({draftEntry.Value})"
                };

                    foreach (var u in urls)
                    {
                        var r = await client.GetAsync(u);
                        var t = await r.Content.ReadAsStringAsync();
                        System.Diagnostics.Debug.WriteLine($"[SL] GET {u} -> {(int)r.StatusCode} {r.ReasonPhrase}");
                        if (!r.IsSuccessStatusCode)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SL] Body:\n{t}");
                            continue;
                        }

                        int before = dto.DocumentLines.Count;
                        ParseDraftLines(t);
                        if (dto.DocumentLines.Count > before) break;
                    }
                }

                await LoadDraftHeaderAsync();
                await LoadDraftLinesAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SL] Doc origin fetch EXC: {ex.Message}");
        }

        // Nome do usuário pendente (se já definido)
        if (dto.PendingUserID.HasValue && string.IsNullOrWhiteSpace(dto.PendingUserName))
            dto.PendingUserName = await ResolveUserNameByIdAsync(dto.PendingUserID.Value);

        return dto;
    }
    /*public async Task<ApprovalRequestDetailsDto?> GetApprovalRequestDetailsAsync(int code, int? currentUserId = null)
    {
        using var client = CreateClientWithCookies(noCache: true);

        // ApprovalRequest "completo"
        var ar = await GetApprovalRequestFullAsync(client, code);
        if (ar is null) return null;

        var root = ar.Value;

        var dto = new ApprovalRequestDetailsDto
        {
            ApprovalRequestID = code,
            Status = root.TryGetProperty("Status", out var st) ? st.GetString() : null,
            Remarks = root.TryGetProperty("Remarks", out var rem) ? rem.GetString() : null,
            CreationDate = root.TryGetProperty("CreationDate", out var cd)
                           && cd.ValueKind == JsonValueKind.String
                           && DateTime.TryParse(cd.GetString(), out var dt) ? dt : (DateTime?)null,
            ApprovalRequestDecisions = new List<ApprovalRequestDecisionDto>(),
            ApprovalRequestLines = new List<ApprovalRequestLineDto>(),
            DocumentLines = new List<DocLineDto>()
        };

        // ---- ApprovalRequestDecisions do payload principal (se vierem)
        if (root.TryGetProperty("ApprovalRequestDecisions", out var decEl) &&
            decEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in decEl.EnumerateArray())
            {
                dto.ApprovalRequestDecisions!.Add(new ApprovalRequestDecisionDto
                {
                    StageCode = el.TryGetProperty("StageCode", out var sc) && sc.ValueKind == JsonValueKind.Number ? sc.GetInt32() : (int?)null,
                    UserID = el.TryGetProperty("UserID", out var uid) && uid.ValueKind == JsonValueKind.Number ? uid.GetInt32() : (int?)null,
                    Status = el.TryGetProperty("Status", out var s) ? s.GetString() : null,
                    Remarks = el.TryGetProperty("Remarks", out var rk) ? rk.GetString() : null
                });
            }
        }

        // ---- ApprovalRequestLines do payload principal (se vierem)
        if (root.TryGetProperty("ApprovalRequestLines", out var linesEl) &&
            linesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var ln in linesEl.EnumerateArray())
            {
                dto.ApprovalRequestLines!.Add(new ApprovalRequestLineDto
                {
                    StageCode = ln.TryGetProperty("StageCode", out var lsc) && lsc.ValueKind == JsonValueKind.Number ? lsc.GetInt32() : (int?)null,
                    UserID = ln.TryGetProperty("UserID", out var luid) && luid.ValueKind == JsonValueKind.Number ? luid.GetInt32() : (int?)null,
                    Status = ln.TryGetProperty("Status", out var ls) ? ls.GetString() : null,
                    Remarks = ln.TryGetProperty("Remarks", out var lrm) ? lrm.GetString() : null,
                    UpdateDate = ln.TryGetProperty("UpdateDate", out var lud) && lud.ValueKind == JsonValueKind.String && DateTime.TryParse(lud.GetString(), out var ludt) ? ludt : (DateTime?)null,
                    UpdateTime = ln.TryGetProperty("UpdateTime", out var lut) ? lut.GetString() : null,
                    CreationDate = ln.TryGetProperty("CreationDate", out var lcd2) && lcd2.ValueKind == JsonValueKind.String && DateTime.TryParse(lcd2.GetString(), out var lcdt2) ? lcdt2 : (DateTime?)null,
                    CreationTime = ln.TryGetProperty("CreationTime", out var lct2) ? lct2.GetString() : null
                });
            }
        }

        // ---- Decisions via navegação (aceita vazio)
        try
        {
            var urlNavJson = $"ApprovalRequests({code})/ApprovalRequestDecisions?$format=json";
            var rNavJson = await client.GetAsync(urlNavJson);
            var jNavJson = await rNavJson.Content.ReadAsStringAsync();

            if (rNavJson.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(jNavJson);
                var rootJson = doc.RootElement;

                JsonElement decArr = default;
                if (rootJson.TryGetProperty("ApprovalRequestDecisions", out var decProp) && decProp.ValueKind == JsonValueKind.Array)
                    decArr = decProp;
                else if (rootJson.TryGetProperty("value", out var valArr) && valArr.ValueKind == JsonValueKind.Array)
                    decArr = valArr;
                else if (rootJson.TryGetProperty("d", out var dObj) && dObj.TryGetProperty("results", out var resArr) && resArr.ValueKind == JsonValueKind.Array)
                    decArr = resArr;
                else if (rootJson.ValueKind == JsonValueKind.Array)
                    decArr = rootJson;

                if (decArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in decArr.EnumerateArray())
                    {
                        dto.ApprovalRequestDecisions!.Add(new ApprovalRequestDecisionDto
                        {
                            StageCode = el.TryGetProperty("StageCode", out var sc2) && sc2.ValueKind == JsonValueKind.Number ? sc2.GetInt32() : (int?)null,
                            StageID = el.TryGetProperty("StageID", out var si) && si.ValueKind == JsonValueKind.Number ? si.GetInt32() : (int?)null,
                            UserID = el.TryGetProperty("UserID", out var uid2) && uid2.ValueKind == JsonValueKind.Number ? uid2.GetInt32() : (int?)null,
                            Status = el.TryGetProperty("Status", out var s2) ? s2.GetString() : null,
                            Remarks = el.TryGetProperty("Remarks", out var rk2) ? rk2.GetString() : null
                        });
                    }
                }
            }
        }
        catch {
                //ignora e segue com derivação
        }
        dto.ApprovalTemplatesID =
    root.TryGetProperty("ApprovalTemplatesID", out var atid) && atid.ValueKind == JsonValueKind.Number
        ? atid.GetInt32()
        : (int?)null;

        dto.ApprovalTemplateID =
            root.TryGetProperty("ApprovalTemplateID", out var atid2) && atid2.ValueKind == JsonValueKind.Number
                ? atid2.GetInt32()
                : (int?)null;

        // ---- Descobrir pendência (ordem de preferência)

        // 1) Decision ardPending (se existir)
        var pendFromDec1 = dto.ApprovalRequestDecisions!.FirstOrDefault(d => d.Status == "ardPending");
        if (pendFromDec1?.StageCode != null) dto.PendingStageCode = pendFromDec1.StageCode.Value;
        if (pendFromDec1?.UserID != null) dto.PendingUserID = pendFromDec1.UserID.Value;

        // 2) Lines ardPending (por usuário atual, se informado; senão a primeira)
        if ((!dto.PendingStageCode.HasValue || !dto.PendingUserID.HasValue) && dto.ApprovalRequestLines!.Count > 0)
        {
            var pendFromLines = (currentUserId.HasValue
                ? dto.ApprovalRequestLines.FirstOrDefault(l => l.Status == "ardPending" && l.UserID == currentUserId.Value)
                : dto.ApprovalRequestLines.FirstOrDefault(l => l.Status == "ardPending"));

            if (pendFromLines?.StageCode != null) dto.PendingStageCode ??= pendFromLines.StageCode.Value;
            if (pendFromLines?.UserID != null) dto.PendingUserID ??= pendFromLines.UserID.Value;
        }

        // 3) Fallback: CurrentStage
        if (!dto.PendingStageCode.HasValue &&
            root.TryGetProperty("CurrentStage", out var curStageEl) &&
            curStageEl.ValueKind == JsonValueKind.Number)
        {
            dto.PendingStageCode = curStageEl.GetInt32();
        }

        // 4) Se o request está pendente, amarre PendingUserID ao currentUserId (último recurso)
        if (string.Equals(dto.Status, "arsPending", StringComparison.OrdinalIgnoreCase) && currentUserId.HasValue)
            dto.PendingUserID ??= currentUserId.Value;

        // ---- Solicitante (OriginatorID → UserCode)
        if (root.TryGetProperty("OriginatorID", out var orig) && orig.ValueKind == JsonValueKind.Number)
            dto.RequesterName = await ResolveUserNameByIdAsync(orig.GetInt32());

        // ---- Documento de origem (definitivo ou rascunho)
        string? objectType = root.TryGetProperty("ObjectType", out var ot) ? ot.GetString() : null;
        int? objectEntry = root.TryGetProperty("ObjectEntry", out var oe) && oe.ValueKind == JsonValueKind.Number ? oe.GetInt32() : (int?)null;

        bool isDraft = root.TryGetProperty("IsDraft", out var isDraftEl) &&
                       string.Equals(isDraftEl.GetString(), "Y", StringComparison.OrdinalIgnoreCase);
        int? draftEntry = root.TryGetProperty("DraftEntry", out var de) && de.ValueKind == JsonValueKind.Number ? de.GetInt32() : (int?)null;

        try
        {
            // Documento definitivo
            // Documento definitivo
            if (objectEntry.HasValue && !string.IsNullOrEmpty(objectType))
            {
                async Task LoadHeaderAndLinesAsync(string entityName)
                {
                    // ---------- Cabeçalho enxuto ----------
                    var headUrl = $"{entityName}({objectEntry.Value})?$select=DocNum,CardName,DocTotal,DocTotalFc";
                    var hResp = await client.GetAsync(headUrl);
                    var hJson = await hResp.Content.ReadAsStringAsync();
                    if (hResp.IsSuccessStatusCode)
                    {
                        using var hDoc = JsonDocument.Parse(hJson);
                        var hRoot = hDoc.RootElement;

                        if (hRoot.TryGetProperty("DocNum", out var dn) && dn.ValueKind != JsonValueKind.Null)
                            dto.DocNum = dn.ValueKind == JsonValueKind.Number ? dn.GetInt32().ToString() : dn.GetString();

                        if (hRoot.TryGetProperty("CardName", out var cn))
                            dto.CardName = cn.GetString();

                        if (hRoot.TryGetProperty("DocTotal", out var tot) && tot.ValueKind == JsonValueKind.Number)
                            dto.DocTotal = tot.GetDecimal();

                        if (hRoot.TryGetProperty("DocTotalFc", out var totfc) && tot.ValueKind == JsonValueKind.Number)
                            dto.DocTotalFC = totfc.GetDecimal();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[SL] GET {headUrl} -> {(int)hResp.StatusCode} {hResp.ReasonPhrase}\n{hJson}");
                    }

                    // ---------- Parser genérico de coleções de linhas ----------
                    void ParseLinesFromJson(string json)
                    {
                        using var doc = JsonDocument.Parse(json);
                        var rootAll = doc.RootElement;

                        // Tenta extrair uma "array view" (value[], d.results[], ou array puro)
                        bool TryGetArray(JsonElement src, out JsonElement arr)
                        {
                            // 1) value[]
                            if (src.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array)
                            { arr = v; return true; }

                            // 2) d.results[]
                            if (src.TryGetProperty("d", out var d) &&
                                d.ValueKind == JsonValueKind.Object &&
                                d.TryGetProperty("results", out var rs) &&
                                rs.ValueKind == JsonValueKind.Array)
                            { arr = rs; return true; }

                            // 3) Se veio um objeto com DocumentLines (array)
                            if (src.TryGetProperty("DocumentLines", out var dl) && dl.ValueKind == JsonValueKind.Array)
                            { arr = dl; return true; }

                            // 3b) Se DocumentLines veio como objeto com results[]
                            if (src.TryGetProperty("DocumentLines", out var dl2) &&
                                dl2.ValueKind == JsonValueKind.Object &&
                                dl2.TryGetProperty("results", out var rs2) &&
                                rs2.ValueKind == JsonValueKind.Array)
                            { arr = rs2; return true; }

                            // 4) Alguns endpoints retornam a coleção diretamente como array
                            if (src.ValueKind == JsonValueKind.Array)
                            { arr = src; return true; }

                            arr = default;
                            return false;
                        }

                        if (!TryGetArray(rootAll, out var arr)) return;

                        foreach (var ln in arr.EnumerateArray())
                        {
                            var desc = ln.TryGetProperty("ItemDescription", out var idesc) ? idesc.GetString()
                                     : (ln.TryGetProperty("ItemName", out var iname) ? iname.GetString() : null);

                            decimal qty = (ln.TryGetProperty("Quantity", out var q) && q.ValueKind == JsonValueKind.Number)
                                          ? q.GetDecimal() : 0m;

                            // Aceita Price OU UnitPrice (varia por entidade/versão)
                            decimal price = 0m;
                            if (ln.TryGetProperty("Price", out var p) && p.ValueKind == JsonValueKind.Number)
                                price = p.GetDecimal();
                            else if (ln.TryGetProperty("UnitPrice", out var up) && up.ValueKind == JsonValueKind.Number)
                                price = up.GetDecimal();

                            string? matNfse = ln.TryGetProperty("U_TX_DescMatNfse", out var udf) ? udf.GetString() : null;

                            dto.DocumentLines!.Add(new DocLineDto
                            {
                                ItemCode = ln.TryGetProperty("ItemCode", out var ic) ? ic.GetString() : null,
                                ItemName = desc,
                                ItemDescription = desc,
                                Quantity = qty,
                                Price = price,
                                U_TX_DescMatNfse = matNfse
                            });
                        }
                    }

                    // ---------- Tenta uma série de URLs, na ordem ----------
                    var urls = new List<string>
        {
            // Navegação direta (mais previsível) — com select
            $"{entityName}({objectEntry.Value})/DocumentLines?$select=ItemCode,ItemDescription,ItemName,Quantity,Price,UnitPrice,U_TX_DescMatNfse",
            // Variante com $format=json (já salvou a gente em ApprovalRequestDecisions)
            $"{entityName}({objectEntry.Value})/DocumentLines?$format=json",
            // Sem select (evita 400 em alguns builds)
            $"{entityName}({objectEntry.Value})/DocumentLines",

            // Alguns tenants expõem apenas /Lines
            $"{entityName}({objectEntry.Value})/Lines?$select=ItemCode,ItemDescription,ItemName,Quantity,Price,UnitPrice,U_TX_DescMatNfse",
            $"{entityName}({objectEntry.Value})/Lines?$format=json",
            $"{entityName}({objectEntry.Value})/Lines",

            // Expand no cabeçalho — SEM $select dentro do $expand (menos chance de 400)
            $"{entityName}({objectEntry.Value})?$expand=DocumentLines",
            $"{entityName}({objectEntry.Value})?$expand=Lines",

            // Último recurso: GET do cabeçalho completo e extrai DocumentLines do mesmo objeto
            $"{entityName}({objectEntry.Value})"
        };

                    // Garante a lista de linhas
                    if (dto.DocumentLines == null) dto.DocumentLines = new List<DocLineDto>();

                    foreach (var url in urls)
                    {
                        var resp = await client.GetAsync(url);
                        var txt = await resp.Content.ReadAsStringAsync();

                        System.Diagnostics.Debug.WriteLine($"[SL] GET {url} -> {(int)resp.StatusCode} {resp.ReasonPhrase}");

                        if (!resp.IsSuccessStatusCode)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SL] Body:\n{txt}");
                            continue;
                        }

                        int before = dto.DocumentLines.Count;
                        ParseLinesFromJson(txt);
                        int added = dto.DocumentLines.Count - before;

                        // Se conseguiu adicionar ao menos 1 linha, encerra a busca
                        if (added > 0) break;

                        // Se foi o cabeçalho completo (último) e ainda não achou nada, segue tentando as próximas (se houver)
                    }
                }

                if (objectType == "1470000113")       // PurchaseRequest
                    await LoadHeaderAndLinesAsync("PurchaseRequests");
                else if (objectType == "22")          // PurchaseOrder
                    await LoadHeaderAndLinesAsync("PurchaseOrders");
            }
            // Rascunho
            // --- RASCUNHO (IsDraft = Y, usar Drafts(DraftEntry)) ---
            else if (isDraft && draftEntry.HasValue)
            {
                // Garante lista
                if (dto.DocumentLines == null) dto.DocumentLines = new List<DocLineDto>();

                // 1) Cabeçalho do rascunho (pega CardName, DocTotal, DocObjectCode)
                async Task LoadDraftHeaderAsync()
                {
                    var headUrl = $"Drafts({draftEntry.Value})?$select=DocObjectCode,CardName,DocTotal,DocTotalFc";
                    var hResp = await client.GetAsync(headUrl);
                    var hJson = await hResp.Content.ReadAsStringAsync();

                    if (!hResp.IsSuccessStatusCode)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SL] GET {headUrl} -> {(int)hResp.StatusCode} {hResp.ReasonPhrase}\n{hJson}");
                        // fallback sem select
                        headUrl = $"Drafts({draftEntry.Value})";
                        hResp = await client.GetAsync(headUrl);
                        hJson = await hResp.Content.ReadAsStringAsync();
                    }

                    if (hResp.IsSuccessStatusCode)
                    {
                        using var hDoc = JsonDocument.Parse(hJson);
                        var root = hDoc.RootElement;

                        // Suporta value[0] ou objeto direto
                        if (root.TryGetProperty("value", out var hv) && hv.ValueKind == JsonValueKind.Array && hv.GetArrayLength() > 0)
                            root = hv[0];

                        if (root.TryGetProperty("CardName", out var cn)) dto.CardName = cn.GetString();

                        if (root.TryGetProperty("DocTotal", out var tot) && tot.ValueKind != JsonValueKind.Null)
                        {
                            if (tot.ValueKind == JsonValueKind.Number && tot.TryGetDecimal(out var n))
                                dto.DocTotal = n;
                            else if (tot.ValueKind == JsonValueKind.String &&
                                     decimal.TryParse(tot.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s))
                                dto.DocTotal = s;
                        }

                        if (root.TryGetProperty("DocTotalFc", out var totfc) && tot.ValueKind != JsonValueKind.Null)
                        {
                            if (tot.ValueKind == JsonValueKind.Number && totfc.TryGetDecimal(out var n))
                                dto.DocTotalFC = n;
                            else if (tot.ValueKind == JsonValueKind.String &&
                                     decimal.TryParse(totfc.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s))
                                dto.DocTotalFC = s;
                        }

                        if (root.TryGetProperty("DocObjectCode", out var docObj) && docObj.ValueKind == JsonValueKind.String)
                            dto.DocObjCode = docObj.GetString();
                    }
                }

                // 2) Parser genérico de LINHAS
                void ParseDraftLines(string json)
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    bool TryGetArray(JsonElement src, out JsonElement arr)
                    {
                        // value[]
                        if (src.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array)
                        { arr = v; return true; }

                        // d.results[]
                        if (src.TryGetProperty("d", out var d) &&
                            d.ValueKind == JsonValueKind.Object &&
                            d.TryGetProperty("results", out var rs) &&
                            rs.ValueKind == JsonValueKind.Array)
                        { arr = rs; return true; }

                        // Embedded no objeto: DocumentLines (array ou results)
                        if (src.TryGetProperty("DocumentLines", out var dl))
                        {
                            if (dl.ValueKind == JsonValueKind.Array) { arr = dl; return true; }
                            if (dl.ValueKind == JsonValueKind.Object &&
                                dl.TryGetProperty("results", out var rs2) && rs2.ValueKind == JsonValueKind.Array)
                            { arr = rs2; return true; }
                        }

                        // Alguns builds usam "Lines" em vez de "DocumentLines"
                        if (src.TryGetProperty("Lines", out var dl3))
                        {
                            if (dl3.ValueKind == JsonValueKind.Array) { arr = dl3; return true; }
                            if (dl3.ValueKind == JsonValueKind.Object &&
                                dl3.TryGetProperty("results", out var rs3) && rs3.ValueKind == JsonValueKind.Array)
                            { arr = rs3; return true; }
                        }

                        // Array puro
                        if (src.ValueKind == JsonValueKind.Array) { arr = src; return true; }

                        arr = default;
                        return false;
                    }

                    if (!TryGetArray(root, out var arr)) return;

                    foreach (var ln in arr.EnumerateArray())
                    {
                        var desc = ln.TryGetProperty("ItemDescription", out var idesc) ? idesc.GetString()
                                 : (ln.TryGetProperty("ItemName", out var iname) ? iname.GetString() : null);

                        decimal qty = (ln.TryGetProperty("Quantity", out var q) && q.ValueKind == JsonValueKind.Number) ? q.GetDecimal() : 0m;

                        // Draft pode expor Price OU UnitPrice
                        decimal price = 0m;
                        if (ln.TryGetProperty("Price", out var p) && p.ValueKind == JsonValueKind.Number) price = p.GetDecimal();
                        else if (ln.TryGetProperty("UnitPrice", out var up) && up.ValueKind == JsonValueKind.Number) price = up.GetDecimal();

                        string? mat = ln.TryGetProperty("U_TX_DescMatNfse", out var udf) ? udf.GetString() : null;

                        dto.DocumentLines!.Add(new DocLineDto
                        {
                            ItemCode = ln.TryGetProperty("ItemCode", out var ic) ? ic.GetString() : null,
                            ItemName = desc,
                            ItemDescription = desc,
                            Quantity = qty,
                            Price = price,
                            U_TX_DescMatNfse = mat
                        });
                    }
                }

                // 3) Tenta várias rotas de linhas do Draft
                async Task LoadDraftLinesAsync()
                {
                    var urls = new[]
                    {
            // Navegação clássica
            $"Drafts({draftEntry.Value})/DocumentLines?$select=ItemCode,ItemDescription,ItemName,Quantity,Price,UnitPrice,U_TX_DescMatNfse",
            $"Drafts({draftEntry.Value})/DocumentLines?$format=json",
            $"Drafts({draftEntry.Value})/DocumentLines",

            // Variante "Lines"
            $"Drafts({draftEntry.Value})/Lines?$select=ItemCode,ItemDescription,ItemName,Quantity,Price,UnitPrice,U_TX_DescMatNfse",
            $"Drafts({draftEntry.Value})/Lines?$format=json",
            $"Drafts({draftEntry.Value})/Lines",

            // Expand no header (alguns builds só retornam assim)
            $"Drafts({draftEntry.Value})?$expand=DocumentLines",
            $"Drafts({draftEntry.Value})?$expand=Lines",

            // Último recurso: header completo e extrair as linhas de dentro
            $"Drafts({draftEntry.Value})"
        };

                    foreach (var u in urls)
                    {
                        var r = await client.GetAsync(u);
                        var t = await r.Content.ReadAsStringAsync();
                        System.Diagnostics.Debug.WriteLine($"[SL] GET {u} -> {(int)r.StatusCode} {r.ReasonPhrase}");
                        if (!r.IsSuccessStatusCode)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SL] Body:\n{t}");
                            continue;
                        }

                        int before = dto.DocumentLines.Count;
                        ParseDraftLines(t);
                        if (dto.DocumentLines.Count > before) break; // já trouxe linhas, pode sair
                    }
                }

                await LoadDraftHeaderAsync();
                await LoadDraftLinesAsync();
            }

        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SL] Doc origin fetch EXC: {ex.Message}");
        }

        // Nome do usuário pendente (se já definido)
        if (dto.PendingUserID.HasValue && string.IsNullOrWhiteSpace(dto.PendingUserName))
            dto.PendingUserName = await ResolveUserNameByIdAsync(dto.PendingUserID.Value);

        return dto;
    }*/
    private async Task<HttpResponseMessage> GetWithLog(HttpClient c, string relativeUrl)
    {
        var resp = await c.GetAsync(relativeUrl);
        var body = await resp.Content.ReadAsStringAsync();
        System.Diagnostics.Debug.WriteLine($"[SL] GET {c.BaseAddress}{relativeUrl} -> {(int)resp.StatusCode} {resp.ReasonPhrase}");
        if (!resp.IsSuccessStatusCode)
            System.Diagnostics.Debug.WriteLine($"[SL] Error body: {body}");
        else
            System.Diagnostics.Debug.WriteLine($"[SL] OK body (trunc): {body.Substring(0, Math.Min(body.Length, 500))}");
        return resp;
    }
    public async Task<bool> DebugSmokeAsync(int id)
    {
        using var client = CreateClientWithCookies(noCache: true);

        // 1) Ping raiz
        var r0 = await GetWithLog(client, ""); // GET /b1s/v1/
        if (!r0.IsSuccessStatusCode) return false;

        // 2) Lista top 1
        var r1 = await GetWithLog(client, "ApprovalRequests?$top=1");
        if (!r1.IsSuccessStatusCode) return false;

        // 3) Um ID sem expand
        var r2 = await GetWithLog(client, $"ApprovalRequests({id})");
        if (!r2.IsSuccessStatusCode) return false;

        return true;
    }
    public async Task<string> DebugHLGAsync(string? sapUserCode, int? sampleId = null)
    {
        using var client = CreateClientWithCookies(noCache: true);

        async Task<string> GetBody(string rel)
        {
            var r = await client.GetAsync(rel);
            var body = await r.Content.ReadAsStringAsync();
            return $"GET {rel} -> {(int)r.StatusCode}\n{body}";
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(await GetBody("")); // ping /b1s/v1/
        sb.AppendLine();

        // 1) Existem aprovações nesse DB?
        sb.AppendLine(await GetBody("ApprovalRequests?$top=1&$orderby=CreationDate desc"));
        sb.AppendLine();

        // 2) Usuário existe?
        if (!string.IsNullOrWhiteSpace(sapUserCode))
            sb.AppendLine(await GetBody($"Users?$select=InternalKey,UserCode,UserName&$filter=UserCode eq '{sapUserCode.Replace("'", "''")}'&$top=1"));

        sb.AppendLine();

        // 3) Se quiser testar um ID específico (sem expand)
        if (sampleId.HasValue)
            sb.AppendLine(await GetBody($"ApprovalRequests({sampleId.Value})"));

        return sb.ToString();
    }
    public async Task<string> DebugApprovalActionAsync(int code)
    {
        using var client = CreateClientWithCookies(noCache: true);
        var postOpts = new JsonSerializerOptions { PropertyNamingPolicy = null };
        var variants = new (string endpoint, object payload)[]
        {
        ("ApprovalRequestsService_GetApprovalRequest", new { ApprovalRequestID = code }),
        ("ApprovalRequestsService_GetApprovalRequest", new { ApprovalRequest = new { ApprovalRequestID = code } }),
        ("ApprovalRequestsService_GetApprovalRequest", new { Code = code }),
        ("ApprovalRequestsService_GetApprovalRequest", new { ApprovalRequest = new { Code = code } }),
        ("ApprovalRequestsService_GetApprovalRequestByID", new { ApprovalRequestID = code }),
        };

        var sb = new System.Text.StringBuilder();
        foreach (var (ep, pl) in variants)
        {
            var resp = await client.PostAsJsonAsync(ep, pl, postOpts);
            var body = await resp.Content.ReadAsStringAsync();
            sb.AppendLine($"POST {ep} => {(int)resp.StatusCode} {resp.ReasonPhrase}");
            sb.AppendLine(body.Length > 600 ? body[..600] + "..." : body);
            sb.AppendLine();
            if (resp.IsSuccessStatusCode) break;
        }
        return sb.ToString();
    }
    private async Task<string?> GetMetadataAsync(HttpClient client)
    {
        var resp = await client.GetAsync("$metadata");
        var xml = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            System.Diagnostics.Debug.WriteLine($"[SL] GET $metadata -> {(int)resp.StatusCode} {resp.ReasonPhrase}\n{xml}");
            return null;
        }
        return xml;
    }
    private (string endpoint, string paramName)? FindApprovalGetSignature(string metadataXml)
    {
        // busca candidates comuns
        var candidates = new[]{
        "ApprovalRequestsService_GetApprovalRequest",
        "ApprovalRequestsService_GetApprovalRequestByID",
        "ApprovalRequestsService_GetApproval",
        "ApprovalRequestsService_Get" // fallback bem amplo
    };

        foreach (var name in candidates)
        {
            var idx = metadataXml.IndexOf($"FunctionImport Name=\"{name}\"", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;

            // dentro do trecho do FunctionImport, procure o <Parameter Name="...">
            var cut = metadataXml.Substring(idx, Math.Min(2000, metadataXml.Length - idx));
            // pega o primeiro parâmetro (geralmente é 1 mesmo)
            // Ex: <Parameter Name="ApprovalRequestID" Type="Edm.Int32" Mode="In" />
            var pIdx = cut.IndexOf("<Parameter", StringComparison.OrdinalIgnoreCase);
            if (pIdx >= 0)
            {
                var pCut = cut.Substring(pIdx, Math.Min(200, cut.Length - pIdx));
                var nameAttrIdx = pCut.IndexOf("Name=\"", StringComparison.OrdinalIgnoreCase);
                if (nameAttrIdx > 0)
                {
                    var start = nameAttrIdx + "Name=\"".Length;
                    var end = pCut.IndexOf("\"", start, StringComparison.Ordinal);
                    if (end > start)
                    {
                        var paramName = pCut.Substring(start, end - start);
                        return (name, paramName);
                    }
                }
            }

            // se não achou parâmetro, ainda assim devolve o endpoint e tenta padrões de nome
            return (name, "ApprovalRequestID");
        }

        return null;
    }
    private async Task<JsonElement?> GetApprovalRequestFullAsync(HttpClient client, int code)
    {
        var url1 =
            "ApprovalRequests?" +
            $"$filter=Code eq {code}&$top=1" +
            "&$select = Code,ApprovalTemplatesID,ApprovalTemplateID,ObjectType,IsDraft,ObjectEntry,Status,Remarks,CurrentStage,OriginatorID,CreationDate,CreationTime,DraftEntry,DraftType" +
            "&$expand=ApprovalRequestLines($select=StageCode,UserID,Status,Remarks,UpdateDate,UpdateTime,CreationDate,CreationTime)";

        foreach (var url in new[] { url1, $"ApprovalRequests?$filter=Code eq {code}&$top=1" })
        {
            var resp = await client.GetAsync(url);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[SL] GET {url} -> {(int)resp.StatusCode} {resp.ReasonPhrase}\n{body}");
                continue;
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // value[]
            if (root.TryGetProperty("value", out var arr) &&
                arr.ValueKind == JsonValueKind.Array &&
                arr.GetArrayLength() > 0)
            {
                return arr[0].Clone(); // <<< IMPORTANTE
            }

            // d/results[]
            if (root.TryGetProperty("d", out var d) &&
                d.TryGetProperty("results", out var results) &&
                results.ValueKind == JsonValueKind.Array &&
                results.GetArrayLength() > 0)
            {
                return results[0].Clone(); // <<< IMPORTANTE
            }

            System.Diagnostics.Debug.WriteLine($"[SL] GET {url} retornou 200 mas sem item.");
        }

        return null;
    }
    private async Task<string?> ResolveStageNameAsync(HttpClient client, int stageCode)
    {
        if (_stageCache.TryGetValue(stageCode, out var cached))
            return cached;

        // Tentativas compatíveis com variações do SL
        var urls = new[]
        {
        $"ApprovalStages({stageCode})?$select=StageCode,Name,Description",
        $"ApprovalStages?$filter=StageCode eq {stageCode}&$select=StageCode,Name,Description",
        // fallback amplo, caso a propriedade se chame diferente em alguma versão
        $"ApprovalStages({stageCode})",
        $"ApprovalStages?$filter=StageCode eq {stageCode}"
    };

        foreach (var u in urls)
        {
            try
            {
                var resp = await client.GetAsync(u);
                var txt = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode) continue;

                using var doc = JsonDocument.Parse(txt);
                JsonElement root = doc.RootElement;

                // suporta objeto direto e wrappers value[] / d.results[]
                if (root.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
                    root = arr[0];
                else if (root.TryGetProperty("d", out var d) && d.TryGetProperty("results", out var rs) && rs.ValueKind == JsonValueKind.Array && rs.GetArrayLength() > 0)
                    root = rs[0];

                string? name =
                      (root.TryGetProperty("Name", out var n) && n.ValueKind == JsonValueKind.String) ? n.GetString()
                    : (root.TryGetProperty("StageName", out var sn) && sn.ValueKind == JsonValueKind.String) ? sn.GetString()
                    : null;

                string? desc =
                      (root.TryGetProperty("Description", out var de) && de.ValueKind == JsonValueKind.String) ? de.GetString()
                    : (root.TryGetProperty("Remarks", out var re) && re.ValueKind == JsonValueKind.String) ? re.GetString()
                    : null;

                // Formato amigável: "AP-010 — Gestor TI" se vier nome e descrição
                var friendly = !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(desc)
                    //? $"{name} — {desc}"
                    ? $"{desc}"
                    : (name ?? desc);

                if (!string.IsNullOrWhiteSpace(friendly))
                {
                    _stageCache[stageCode] = friendly!;
                    //return friendly;
                    return desc;
                }
            }
            catch { /* ignora e tenta próxima variação */ }
        }

        return null;
    }
    private string? _sessionKeyOverride;
    public void SetSessionKey(string key) => _sessionKeyOverride = key;
    public async Task<(bool ok, string? error)> DecideUsingCurrentStageAsync(
    int approvalRequestId, bool approve, string? remarks)
    {
        var myUserCode = _httpCtx.HttpContext?.Session?.GetString("SAP_USERCODE");
        if (string.IsNullOrWhiteSpace(myUserCode))
            return (false, "Usuário SAP não encontrado na sessão.");

        // (opcional) confirmar que está pendente
        using var client = CreateClientWithCookies(noCache: true);
        var basicResp = await client.GetAsync($"ApprovalRequests({approvalRequestId})?$select=Code,Status");
        var basicTxt = await basicResp.Content.ReadAsStringAsync();
        if (!basicResp.IsSuccessStatusCode)
            return (false, BuildSlError(basicTxt, basicResp));

        using (var doc = JsonDocument.Parse(basicTxt))
        {
            var status = doc.RootElement.TryGetProperty("Status", out var st) ? st.GetString() : null;
            if (!string.Equals(status, "arsPending", StringComparison.OrdinalIgnoreCase))
                return (false, "Solicitação não está pendente.");
        }

        var (ok, err) = await PostUpdateApprovalRequestSlimAsync(
            approvalRequestId: approvalRequestId,
            approverUserName: myUserCode,
            status: approve ? "ardApproved" : "ardRejected",
            remarks: remarks
        );
        return (ok, err);
    }
    public async Task<(bool ok, string? error, string? raw)> PostHandleApprovalRequestAsync(
    int draftDocEntry,
    string docObjectCode = "1470000113", // oPurchaseRequest
    CancellationToken ct = default)
    {
        using var client = CreateClientWithCookies(noCache: true); // <= BaseAddress deve ser .../b1s/v1/ (ou v2/)

        // Tenta com Document (DocEntry + DocObjectCode)
        var p1 = new { Document = new { DocEntry = draftDocEntry, DocObjectCode = docObjectCode } };
        using (var req1 = JsonContent.Create(p1))
        {
            var resp1 = await client.PostAsync("DraftsService_HandleApprovalRequest", req1, ct);
            var raw1 = await resp1.Content.ReadAsStringAsync(ct);
            if (resp1.IsSuccessStatusCode) return (true, null, raw1);
        }

        // Tenta só com DocEntry (alguns builds ignoram DocObjectCode)
        var p2 = new { Document = new { DocEntry = draftDocEntry } };
        using (var req2 = JsonContent.Create(p2))
        {
            var resp2 = await client.PostAsync("DraftsService_HandleApprovalRequest", req2, ct);
            var raw2 = await resp2.Content.ReadAsStringAsync(ct);
            if (resp2.IsSuccessStatusCode) return (true, null, raw2);
        }

        // Tenta com corpo vazio (há builds que aceitam vazio)
        using (var req3 = new StringContent(string.Empty, Encoding.UTF8, "application/json"))
        {
            var resp3 = await client.PostAsync("DraftsService_HandleApprovalRequest", req3, ct);
            var raw3 = await resp3.Content.ReadAsStringAsync(ct);
            if (resp3.IsSuccessStatusCode) return (true, null, raw3);

            // Se chegou aqui, montamos um erro simples
            return (false, $"HandleApprovalRequest falhou ({(int)resp3.StatusCode} {resp3.ReasonPhrase}): {raw3}", raw3);
        }
    }
    public async Task<(bool ok, string? error)> PatchOrderIndicadorAsync(
    int docEntry,
    string indicador,
    CancellationToken ct = default)
    {
        if (docEntry <= 0)
            return (false, "DocEntry inválido.");

        indicador = (indicador ?? "").Trim();
        if (string.IsNullOrWhiteSpace(indicador))
            return (false, "Status/Indicador é obrigatório.");

        // precisa de sessão ativa (cookies)
        if (!HasSlSession())
            return (false, "Sem sessão ativa no Service Layer. Faça login no SL antes de chamar este endpoint.");

        using var client = CreateClientWithCookies(noCache: true);

        // PATCH /Orders(<DocEntry>)
        var url = $"Orders({docEntry})";

        // Manter o nome exato do campo UDF
        var payload = new Dictionary<string, object?>
        {
            ["U_CVA_Indicador"] = indicador
        };

        // Para manter as chaves como estão (sem camelCase), use PropertyNamingPolicy = null
        var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = null };
        var json = JsonSerializer.Serialize(payload, jsonOpts);

        using var req = new HttpRequestMessage(new HttpMethod("PATCH"), url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        // Recomendado pelo SL em PATCH (principalmente quando tem coleções no objeto)
        req.Headers.Remove("B1S-ReplaceCollectionsOnPatch");
        req.Headers.Add("B1S-ReplaceCollectionsOnPatch", "true");

        using var resp = await client.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);

        if (resp.IsSuccessStatusCode)
            return (true, null);

        // Se a sessão expirou, geralmente volta 401/403
        if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden)
            return (false, "Service Layer retornou não autorizado. Sessão expirada ou inválida. Refaça o login no SL.");

        return (false, BuildSlError(raw, resp));
    }
    public async Task<(bool ok, string? error)> PatchOrderIndicadorAutoLoginAsync(
    int docEntry,
    string indicador,
    CancellationToken ct = default)
    {
        if (docEntry <= 0)
            return (false, "DocEntry inválido.");

        indicador = (indicador ?? "").Trim();
        if (string.IsNullOrWhiteSpace(indicador))
            return (false, "Status é obrigatório.");

        // garante login
        var (loginOk, loginErr) = await LoginPadraoAsync(ct);
        if (!loginOk)
            return (false, loginErr);

        using var client = CreateClientWithCookies(noCache: true);

        var payload = new Dictionary<string, object?>
        {
            ["U_CVA_Indicador"] = indicador
        };

        var json = JsonSerializer.Serialize(payload,
            new JsonSerializerOptions { PropertyNamingPolicy = null });

        using var req = new HttpRequestMessage(new HttpMethod("PATCH"), $"Orders({docEntry})")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        req.Headers.Add("B1S-ReplaceCollectionsOnPatch", "true");

        using var resp = await client.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);

        if (resp.IsSuccessStatusCode)
            return (true, null);

        return (false, BuildSlError(raw, resp));
    }

    public async Task<(bool ok, string? error)> LoginPadraoAsync(CancellationToken ct = default)
    {
        var user = _cfg["ServiceLayer:User"];
        var pass = _cfg["ServiceLayer:Password"];

        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
            return (false, "ServiceLayer:User/Password não configurados.");

        return await LoginAsync(user!, pass!);
    }
    public async Task<(bool ok, string? error)> PatchBusinessPartnerSegVendedorAutoLoginAsync(
    string cardCode,
    int slpCodeNovo,
    CancellationToken ct = default)
    {
        cardCode = (cardCode ?? "").Trim();
        if (string.IsNullOrWhiteSpace(cardCode))
            return (false, "CardCode inválido.");

        if (slpCodeNovo <= 0)
            return (false, "SlpCodeNovo inválido.");

        // garante login
        var (loginOk, loginErr) = await LoginPadraoAsync(ct);
        if (!loginOk)
            return (false, loginErr);

        using var client = CreateClientWithCookies(noCache: true);

        var payload = new Dictionary<string, object?>
        {
            ["U_SegVendedor"] = slpCodeNovo
        };

        // manter o nome exato da propriedade (U_SegVendedor)
        var json = JsonSerializer.Serialize(payload,
            new JsonSerializerOptions { PropertyNamingPolicy = null });

        // ✅ atenção: BusinessPartners usa chave string: BusinessPartners('C0001')
        var escaped = cardCode.Replace("'", "''");
        using var req = new HttpRequestMessage(new HttpMethod("PATCH"), $"BusinessPartners('{escaped}')")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        req.Headers.Add("B1S-ReplaceCollectionsOnPatch", "true");

        using var resp = await client.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);

        if (resp.IsSuccessStatusCode)
            return (true, null);

        return (false, BuildSlError(raw, resp));
    }

    public async Task<(bool ok, string? error, string? body)> GetDraftAsync(int docEntry, CancellationToken ct = default)
    {
        using var client = CreateClientWithCookies(noCache: true);

        var resp = await client.GetAsync($"Drafts({docEntry})", ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            return (false, BuildSlError(body, resp), body);

        return (true, null, body);
    }

    public async Task<(bool ok, string? error, string? body)> EfetivarDraftAsync(int docEntry, CancellationToken ct = default)
    {
        using var client = CreateClientWithCookies(noCache: true);

        var payload = new
        {
            Document = new
            {
                DocEntry = docEntry
            }
        };

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var json = JsonSerializer.Serialize(payload, jsonOptions);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var resp = await client.PostAsync("DraftsService_SaveDraftToDocument", content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            return (false, BuildSlError(body, resp), body);

        return (true, null, body);
    }

    public async Task<(bool ok, string? error, string? body)> AplicarLotesDraftAsync(
        int docEntry,
        IReadOnlyCollection<ItemOrdemEntradaDto> itens,
        string draftBody,
        CancellationToken ct = default)
    {
        var itensComLote = itens
            .Where(i => !string.IsNullOrWhiteSpace(i.CodProduto) &&
                        i.ListaLoteOrdemEntrada.Any(l => !string.IsNullOrWhiteSpace(l.NumLote) && l.Qtde > 0))
            .ToList();

        if (itensComLote.Count == 0)
            return (true, null, null);

        using var draftJson = JsonDocument.Parse(draftBody);

        if (!draftJson.RootElement.TryGetProperty("DocumentLines", out var linhasJson) ||
            linhasJson.ValueKind != JsonValueKind.Array)
        {
            return (false, "Esboço retornado pelo SAP não contém DocumentLines para aplicar lotes.", null);
        }

        var linhasDraft = linhasJson.EnumerateArray()
            .Select(l => new DraftLineBatchInfo(
                GetJsonInt(l, "LineNum", "LineNumber"),
                GetJsonString(l, "ItemCode"),
                GetJsonDecimal(l, "Quantity")))
            .Where(l => l.LineNum >= 0 && !string.IsNullOrWhiteSpace(l.ItemCode))
            .ToList();

        var linhasUsadas = new HashSet<int>();
        var linhasPatch = new List<Dictionary<string, object?>>();

        foreach (var item in itensComLote)
        {
            var codigoProduto = item.CodProduto!.Trim();
            var lotesValidos = item.ListaLoteOrdemEntrada
                .Where(l => !string.IsNullOrWhiteSpace(l.NumLote) && l.Qtde > 0)
                .ToList();

            var qtdLotes = lotesValidos.Sum(l => l.Qtde);

            if (!QuantidadesIguais(qtdLotes, item.Qtde))
            {
                return (false,
                    $"Quantidade dos lotes do item {codigoProduto} ({qtdLotes}) difere da quantidade recebida ({item.Qtde}).",
                    null);
            }

            var candidatos = linhasDraft
                .Where(l => !linhasUsadas.Contains(l.LineNum) &&
                            string.Equals(l.ItemCode, codigoProduto, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (candidatos.Count == 0)
                return (false, $"Item {codigoProduto} recebido do WMS não foi encontrado nas linhas do esboço {docEntry}.", null);

            var linha = candidatos.FirstOrDefault(l => QuantidadesIguais(l.Quantity, item.Qtde));

            if (linha is null)
            {
                if (candidatos.Count != 1)
                {
                    return (false,
                        $"Não foi possível identificar a linha do item {codigoProduto} no esboço {docEntry}. Há múltiplas linhas e nenhuma tem quantidade {item.Qtde}.",
                        null);
                }

                linha = candidatos[0];
            }

            linhasUsadas.Add(linha.LineNum);

            linhasPatch.Add(new Dictionary<string, object?>
            {
                ["LineNum"] = linha.LineNum,
                ["ItemCode"] = linha.ItemCode,
                ["BatchNumbers"] = lotesValidos.Select(l => new Dictionary<string, object?>
                {
                    ["BatchNumber"] = l.NumLote!.Trim(),
                    ["Quantity"] = l.Qtde,
                    ["BaseLineNumber"] = linha.LineNum,
                    ["ExpiryDate"] = FormatarDataSap(l.DataValidade),
                    ["ManufacturingDate"] = FormatarDataSap(l.DataFabricacao)
                }).ToList()
            });
        }

        var payload = new Dictionary<string, object?>
        {
            ["DocumentLines"] = linhasPatch
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        using var client = CreateClientWithCookies(noCache: true);
        using var req = new HttpRequestMessage(new HttpMethod("PATCH"), $"Drafts({docEntry})")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            return (false, BuildSlError(body, resp), body);

        return (true, null, body);
    }

    private sealed record DraftLineBatchInfo(int LineNum, string ItemCode, decimal Quantity);

    private static bool QuantidadesIguais(decimal a, decimal b)
        => Math.Abs(a - b) < 0.000001m;

    private static int GetJsonInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
                return number;

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
                return number;
        }

        return -1;
    }

    private static string GetJsonString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";

    private static decimal GetJsonDecimal(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return 0;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number))
            return number;

        return 0;
    }

    private static string? FormatarDataSap(DateTime? data)
        => data?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTime ResolverDataVencimentoNota(JsonElement order, DateTime dataEmissao)
    {
        var vencimentoPedido = TryGetJsonDate(order, "DocDueDate");

        if (!vencimentoPedido.HasValue || vencimentoPedido.Value.Date < dataEmissao.Date)
            return dataEmissao.Date;

        return vencimentoPedido.Value.Date;
    }

    private static DateTime? TryGetJsonDate(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;

        if (value.ValueKind != JsonValueKind.String)
            return null;

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
            return parsed.Date;

        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out parsed))
            return parsed.Date;

        return null;
    }

    public async Task<(bool ok, string? error)> LeiDKXoxe5gV1qnzUEeg7LY9FMP71k7ALp()
    {
        var user = _cfg["ServiceLayer:User"];
        var password = _cfg["ServiceLayer:Password"];

        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
            return (false, "Usuário ou senha do Service Layer não configurados em ServiceLayer:User / ServiceLayer:Password.");

        return await LoginAsync(user, password);
    }

    public async Task<(bool ok, string? error, string? body)> GetDraftDocumentReferencesAsync(int docEntry)
    {
        using var client = CreateClientWithCookies(noCache: true);

        var resp = await client.GetAsync(
            $"Drafts({docEntry})?$select=DocEntry&$expand=DocumentReferences"
        );

        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            return (false, BuildSlError(body, resp), body);

        return (true, null, body);
    }
    public async Task<(bool ok, string? error, int docEntryCriado)> LocalizarCreditNoteCriadaAsync(
    int numEsboco,
    string? cardCode,
    decimal valorNF,
    CancellationToken ct = default)
    {
        try
        {
            var schema = _cfg["SapB1:CompanyDB"] ?? "SBO_BRW_PRD";

            var sql = $@"
                        SELECT TOP 1
                            T0.""DocEntry""
                        FROM ""{schema}"".""ORIN"" T0
                        WHERE T0.""draftKey"" = {numEsboco}
                        ORDER BY T0.""DocEntry"" DESC";

            var dt = await _hana.QueryToDataTableAsync(sql, null);

            if (dt.Rows.Count > 0)
                return (true, null, Convert.ToInt32(dt.Rows[0]["DocEntry"]));

            var card = (cardCode ?? "").Replace("'", "''");
            var valor = valorNF.ToString(System.Globalization.CultureInfo.InvariantCulture);

            sql = $@"
SELECT TOP 1
    T0.""DocEntry""
FROM ""{schema}"".""ORIN"" T0
WHERE T0.""CardCode"" = '{card}'
  AND ABS(T0.""DocTotal"" - {valor}) < 0.01
ORDER BY T0.""DocEntry"" DESC";

            dt = await _hana.QueryToDataTableAsync(sql, null, ct);

            if (dt.Rows.Count == 0)
                return (false, "Não foi possível localizar a nota de crédito criada na ORIN.", 0);

            return (true, null, Convert.ToInt32(dt.Rows[0]["DocEntry"]));
        }
        catch (Exception ex)
        {
            return (false, ex.Message, 0);
        }
    }

    public async Task<(bool ok, string? error, int qtdCopiada)> CopiarReferenciasDraftParaCreditNoteAsync(
        int docEntryDraft,
        int docEntryCreditNote,
        CancellationToken ct = default)
    {
        try
        {
            var schema = _cfg["SapB1:CompanyDB"] ?? "SBO_BRW_PRD";

            var sqlCheck = $@"
SELECT COUNT(*) AS ""Qtd""
FROM ""{schema}"".""DRF21""
WHERE ""DocEntry"" = {docEntryDraft}";

            var dtCheck = await _hana.QueryToDataTableAsync(sqlCheck, null, ct);

            var qtdDrf21 = Convert.ToInt32(dtCheck.Rows[0]["Qtd"]);

            if (qtdDrf21 == 0)
                return (true, null, 0);

            var sqlDelete = $@"
DELETE FROM ""{schema}"".""RIN21""
WHERE ""DocEntry"" = {docEntryCreditNote}";

            await ExecutarSqlHanaAsync(sqlDelete, ct);

            var colunasComuns = await ObterColunasComunsReferenciasFiscaisAsync(schema);

            if (colunasComuns.Count == 0)
                return (false, "Não foi encontrada nenhuma coluna comum entre DRF21 e RIN21 para copiar as referências fiscais.", 0);

            var colunasDestino = string.Join(",\n    ", new[] { @"""DocEntry""" }.Concat(colunasComuns.Select(QuoteHanaIdentifier)));
            var colunasOrigem = string.Join(",\n    ", new[] { docEntryCreditNote.ToString(CultureInfo.InvariantCulture) }.Concat(colunasComuns.Select(c => $"T0.{QuoteHanaIdentifier(c)}")));

            var sqlInsert = $@"
INSERT INTO ""{schema}"".""RIN21""
(
    {colunasDestino}
)
SELECT
    {colunasOrigem}
FROM ""{schema}"".""DRF21"" T0
WHERE T0.""DocEntry"" = {docEntryDraft}";

            await ExecutarSqlHanaAsync(sqlInsert, ct);

            return (true, null, qtdDrf21);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, 0);
        }
    }

    public async Task<(bool ok, string? error, string? body, int docEntryNota, int docNumNota)> CriarNfSaidaDePedidoVendaAsync(
        WmsRetornoOrdemSaidaRequest request,
        CancellationToken ct = default,
        int tentativaAjusteLote = 0)
    {
        if (request.NumOrdSaida <= 0)
            return (false, "NUM_ORD_SAI inválido.", null, 0, 0);

        if (string.Equals(request.OsCancelada, "S", StringComparison.OrdinalIgnoreCase))
            return (false, "Ordem de saída cancelada pelo WMS. A NF de saída não será criada.", null, 0, 0);

        var itensAtendidos = request.ListaItens
            .Where(i => !string.IsNullOrWhiteSpace(i.CodProduto) && i.QtdeAtendida > 0)
            .ToList();

        if (itensAtendidos.Count == 0)
            return (false, "Nenhum item atendido recebido do WMS.", null, 0, 0);

        var login = await LoginPadraoAsync(ct);
        if (!login.ok)
            return (false, login.error, null, 0, 0);

        var docEntryPedido = request.NumOrdSaida;
        var pedido = await GetOrderForInvoiceAsync(docEntryPedido, ct);

        if (!pedido.ok)
        {
            var docEntryPorDocNum = await ResolverPedidoVendaDocEntryPorDocNumAsync(request.NumOrdSaida, ct);
            if (docEntryPorDocNum > 0 && docEntryPorDocNum != docEntryPedido)
            {
                docEntryPedido = docEntryPorDocNum;
                pedido = await GetOrderForInvoiceAsync(docEntryPedido, ct);
            }
        }

        if (!pedido.ok || string.IsNullOrWhiteSpace(pedido.body))
            return (false, $"Pedido de venda {request.NumOrdSaida} não encontrado no SAP. {pedido.error}", pedido.body, 0, 0);

        var existente = await LocalizarInvoiceCriadaPorPedidoAsync(docEntryPedido);
        if (existente.ok && existente.docEntry > 0)
        {
            return (true, null, JsonSerializer.Serialize(new
            {
                mensagem = "NF de saída já existia para o pedido informado.",
                docEntryNota = existente.docEntry,
                docNumNota = existente.docNum
            }), existente.docEntry, existente.docNum);
        }

        using var orderJson = JsonDocument.Parse(pedido.body);
        var order = orderJson.RootElement;

        if (GetJsonString(order, "DocumentStatus") == "bost_Close")
            return (false, $"Pedido de venda {docEntryPedido} está fechado.", pedido.body, 0, 0);

        if (!order.TryGetProperty("DocumentLines", out var linesJson) || linesJson.ValueKind != JsonValueKind.Array)
            return (false, $"Pedido de venda {docEntryPedido} não retornou linhas.", pedido.body, 0, 0);

        var orderLines = linesJson.EnumerateArray()
            .Select(l => new PedidoVendaLineInfo(
                GetJsonInt(l, "LineNum"),
                GetJsonString(l, "ItemCode"),
                GetJsonDecimal(l, "RemainingOpenQuantity", "OpenQuantity", "Quantity"),
                GetJsonDecimal(l, "UnitPrice", "Price"),
                GetJsonDecimal(l, "DiscountPercent"),
                GetJsonString(l, "WarehouseCode"),
                TryGetJsonInt(l, "Usage")))
            .Where(l => l.LineNum >= 0 && !string.IsNullOrWhiteSpace(l.ItemCode) && l.OpenQuantity > 0)
            .ToList();

        var linhasUsadas = new HashSet<int>();
        var documentLines = new List<Dictionary<string, object?>>();

        foreach (var item in itensAtendidos)
        {
            var codigoProduto = item.CodProduto!.Trim();

            var lotesValidos = item.ListaLotes
                .Where(l => !string.IsNullOrWhiteSpace(l.NumLote) && l.QtdeAtendida > 0)
                .ToList();

            if (lotesValidos.Count > 0)
            {
                var qtdLotes = lotesValidos.Sum(l => l.QtdeAtendida);
                if (!QuantidadesIguais(qtdLotes, item.QtdeAtendida))
                {
                    return (false,
                        $"Quantidade dos lotes do item {codigoProduto} ({qtdLotes}) difere da quantidade atendida ({item.QtdeAtendida}).",
                        null,
                        0,
                        0);
                }
            }

            var candidatos = orderLines
                .Where(l => !linhasUsadas.Contains(l.LineNum) &&
                            string.Equals(l.ItemCode, codigoProduto, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (candidatos.Count == 0)
                return (false, $"Item {codigoProduto} recebido do WMS não foi encontrado aberto no pedido {docEntryPedido}.", pedido.body, 0, 0);

            var linha = candidatos.FirstOrDefault(l => QuantidadesIguais(l.OpenQuantity, item.QtdeAtendida));

            if (linha is null)
            {
                if (candidatos.Count != 1)
                {
                    return (false,
                        $"Não foi possível identificar a linha do item {codigoProduto} no pedido {docEntryPedido}. Há múltiplas linhas abertas e nenhuma tem quantidade {item.QtdeAtendida}.",
                        pedido.body,
                        0,
                        0);
                }

                linha = candidatos[0];
            }

            if (item.QtdeAtendida > linha.OpenQuantity)
            {
                return (false,
                    $"Quantidade atendida do item {codigoProduto} ({item.QtdeAtendida}) é maior que o saldo aberto do pedido ({linha.OpenQuantity}).",
                    pedido.body,
                    0,
                    0);
            }

            linhasUsadas.Add(linha.LineNum);

            var invoiceLineIndex = documentLines.Count;
            var linePayload = new Dictionary<string, object?>
            {
                ["BaseType"] = 17,
                ["BaseEntry"] = docEntryPedido,
                ["BaseLine"] = linha.LineNum,
                ["Quantity"] = item.QtdeAtendida
            };

            if (linha.Usage.HasValue)
                linePayload["Usage"] = linha.Usage.Value;

            if (linha.UnitPrice > 0)
                linePayload["UnitPrice"] = linha.UnitPrice;

            linePayload["DiscountPercent"] = linha.DiscountPercent;

            if (!string.IsNullOrWhiteSpace(linha.WarehouseCode))
                linePayload["WarehouseCode"] = linha.WarehouseCode;

            if (lotesValidos.Count > 0)
            {
                linePayload["BatchNumbers"] = lotesValidos.Select(l => new Dictionary<string, object?>
                {
                    ["BatchNumber"] = l.NumLote!.Trim(),
                    ["Quantity"] = l.QtdeAtendida,
                    ["BaseLineNumber"] = invoiceLineIndex,
                    ["ExpiryDate"] = FormatarDataSap(l.DataValidade),
                    ["ManufacturingDate"] = FormatarDataSap(l.DataFabricacao)
                }).ToList();

            }

            documentLines.Add(linePayload);
        }

        var dataEmissao = DateTime.Today;
        var groupCodePedido = await ObterGroupNumPedidoVendaAsync(docEntryPedido, order, ct);
        var nomeCondicaoPagamento = groupCodePedido.HasValue
            ? await ObterNomeCondicaoPagamentoAsync(groupCodePedido.Value)
            : null;
        var dataVencimentoPadrao = ResolverDataVencimentoNota(order, dataEmissao);
        var parcelasPagamentoAPrazo = await CriarParcelasDaCondicaoPagamentoAsync(
            order,
            groupCodePedido,
            dataEmissao,
            dataVencimentoPadrao,
            nomeCondicaoPagamento,
            ct);
        var ehPagamentoAVista = EhCondicaoPagamentoAVista(order, nomeCondicaoPagamento)
            && !CondicaoPagamentoTemPrazoFuturo(parcelasPagamentoAPrazo, dataEmissao);
        var dataVencimento = ehPagamentoAVista
            ? dataEmissao.Date
            : ObterPrimeiroVencimentoDasParcelas(parcelasPagamentoAPrazo) ?? dataVencimentoPadrao;

        var downPaymentsToDraw = await CriarAdiantamentosParaBaixaNaNotaAsync(docEntryPedido);
        var possuiAdiantamento = downPaymentsToDraw.Count > 0;
        var formaPagamento = ResolverFormaPagamentoNota(order, groupCodePedido, ehPagamentoAVista, possuiAdiantamento);
        var totalVolumesWms = ResolverTotalVolumesWms(request);

        var payload = new Dictionary<string, object?>
        {
            ["CardCode"] = GetJsonString(order, "CardCode"),
            ["DocDate"] = FormatarDataSap(dataEmissao),
            ["TaxDate"] = FormatarDataSap(dataEmissao),
            ["DocDueDate"] = FormatarDataSap(dataVencimento),
            ["NumAtCard"] = GetJsonStringOrNull(order, "NumAtCard"),
            ["Comments"] = GetJsonStringOrNull(order, "Comments"),
            ["SalesPersonCode"] = TryGetJsonInt(order, "SalesPersonCode"),
            ["PaymentGroupCode"] = groupCodePedido,
            ["PaymentMethod"] = formaPagamento,
            ["Project"] = GetJsonStringOrNull(order, "Project"),
            ["BPL_IDAssignedToInvoice"] = TryGetJsonInt(order, "BPL_IDAssignedToInvoice"),
            ["Indicator"] = GetJsonStringOrNull(order, "Indicator"),
            ["DocumentLines"] = documentLines
        };

        if (totalVolumesWms > 0 || request.PesoTotal > 0)
        {
            var taxExtension = new Dictionary<string, object?>();

            if (totalVolumesWms > 0)
                taxExtension["PackQuantity"] = totalVolumesWms;

            if (request.PesoTotal > 0)
                taxExtension["GrossWeight"] = request.PesoTotal;

            if (taxExtension.Count > 0)
            {
                payload["TaxExtension"] = taxExtension;
            }
        }

        if (downPaymentsToDraw.Count > 0)
            payload["DownPaymentsToDraw"] = downPaymentsToDraw;

        if (ehPagamentoAVista)
            AplicarDadosPagamentoAVista(payload, dataVencimento);
        else
        {
            AplicarDadosPagamentoAPrazo(payload);

            if (possuiAdiantamento)
                AplicarDadosSemGeracaoDeBoleto(payload);

            AplicarParcelasDaCondicaoPagamento(payload, parcelasPagamentoAPrazo);
        }

        var documentAdditionalExpenses = CriarDespesasAdicionaisParaNota(order);
        if (documentAdditionalExpenses.Count > 0)
            payload["DocumentAdditionalExpenses"] = documentAdditionalExpenses;

        RemoverNulos(payload);

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var json = JsonSerializer.Serialize(payload, jsonOptions);

        using var client = CreateClientWithCookies(noCache: true);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var resp = await client.PostAsync("Invoices", content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            var erroSl = BuildSlError(body, resp);

            if (tentativaAjusteLote < 5)
            {
                var ajuste = await TentarAjustarLoteIndisponivelAsync(request, erroSl, ct);
                if (ajuste.ok)
                {
                    return await CriarNfSaidaDePedidoVendaAsync(
                        request,
                        ct,
                        tentativaAjusteLote + 1);
                }

                if (ajuste.erroAplicavel)
                    erroSl = $"{erroSl} {ajuste.mensagem}";
            }

            return (false, erroSl, body, 0, 0);
        }

        var docEntryNota = 0;
        var docNumNota = 0;

        try
        {
            using var invoiceJson = JsonDocument.Parse(body);
            docEntryNota = TryGetJsonInt(invoiceJson.RootElement, "DocEntry") ?? 0;
            docNumNota = TryGetJsonInt(invoiceJson.RootElement, "DocNum") ?? 0;
        }
        catch
        {
            // O retorno bruto ainda será gravado no log.
        }

        return (true, null, body, docEntryNota, docNumNota);
    }

    private static int ResolverTotalVolumesWms(WmsRetornoOrdemSaidaRequest request)
    {
        if (request.VolumesTotal > 0)
            return request.VolumesTotal;

        return request.ListaVolumes?.Count(v => !string.IsNullOrWhiteSpace(v.NumVolumeExp)) ?? 0;
    }

    private async Task<(bool ok, bool erroAplicavel, string? mensagem)> TentarAjustarLoteIndisponivelAsync(
        WmsRetornoOrdemSaidaRequest request,
        string erro,
        CancellationToken ct)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            erro,
            @"Insufficient quantity for item\s+(?<item>\S+)\s+with batch\s+(?<batch>\S+)\s+in warehouse",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!match.Success)
            return (false, false, null);

        var itemCode = match.Groups["item"].Value.Trim();
        var loteAtual = match.Groups["batch"].Value.Trim();

        var item = request.ListaItens.FirstOrDefault(i =>
            string.Equals(i.CodProduto?.Trim(), itemCode, StringComparison.OrdinalIgnoreCase) &&
            i.ListaLotes.Any(l => string.Equals(l.NumLote?.Trim(), loteAtual, StringComparison.OrdinalIgnoreCase)));

        var lote = item?.ListaLotes.FirstOrDefault(l =>
            string.Equals(l.NumLote?.Trim(), loteAtual, StringComparison.OrdinalIgnoreCase) &&
            l.QtdeAtendida > 0);

        if (item is null || lote is null)
            return (false, true, $"Não foi possível localizar o item {itemCode} lote {loteAtual} no JSON para ajuste automático.");

        var loteDisponivel = await ObterLoteDisponivelDeposito31Async(itemCode, loteAtual, lote.QtdeAtendida, ct);
        if (loteDisponivel is null)
            return (false, true, $"Não foi encontrado lote disponível no depósito 31 para {itemCode} com quantidade mínima {lote.QtdeAtendida.ToString(CultureInfo.InvariantCulture)}.");

        lote.NumLote = loteDisponivel.Lote;
        lote.DataFabricacao = loteDisponivel.DataFabricacao ?? lote.DataFabricacao;
        lote.DataValidade = loteDisponivel.DataValidade ?? lote.DataValidade;

        return (true, true, null);
    }

    private async Task<LoteDisponivelSap?> ObterLoteDisponivelDeposito31Async(
        string itemCode,
        string loteAtual,
        decimal quantidadeNecessaria,
        CancellationToken ct)
    {
        try
        {
            var schema = _cfg["SapB1:CompanyDB"] ?? "SBO_BRW_PRD";
            var itemEscapado = EscaparLiteralSql(itemCode);
            var loteEscapado = EscaparLiteralSql(loteAtual);
            var quantidade = quantidadeNecessaria.ToString(CultureInfo.InvariantCulture);

            var sql = $@"
SELECT TOP 1
    T0.""DistNumber"" AS ""Lote"",
    T0.""MnfDate"" AS ""DataFabricacao"",
    T0.""ExpDate"" AS ""DataValidade"",
    (IFNULL(T1.""Quantity"", 0) - IFNULL(T1.""CommitQty"", 0)) AS ""Disponivel"",
    IFNULL(T1.""Quantity"", 0) AS ""Estoque"",
    IFNULL(T1.""CommitQty"", 0) AS ""Comprometido""
FROM ""{schema}"".""OBTN"" T0
INNER JOIN ""{schema}"".""OBTQ"" T1
    ON T0.""ItemCode"" = T1.""ItemCode""
   AND T0.""SysNumber"" = T1.""SysNumber""
WHERE T0.""ItemCode"" = '{itemEscapado}'
  AND T1.""WhsCode"" = '31'
  AND IFNULL(T0.""DistNumber"", '') <> '{loteEscapado}'
  AND (
        (IFNULL(T1.""Quantity"", 0) - IFNULL(T1.""CommitQty"", 0)) >= {quantidade}
        OR (
            T0.""DistNumber"" = '9999999999'
            AND IFNULL(T1.""Quantity"", 0) >= {quantidade}
        )
  )
ORDER BY
    CASE
        WHEN (IFNULL(T1.""Quantity"", 0) - IFNULL(T1.""CommitQty"", 0)) >= {quantidade} THEN 0
        ELSE 1
    END,
    CASE WHEN T0.""DistNumber"" = '9999999999' THEN 0 ELSE 1 END,
    CASE WHEN T0.""MnfDate"" IS NULL THEN 1 ELSE 0 END,
    T0.""MnfDate"" ASC,
    (IFNULL(T1.""Quantity"", 0) - IFNULL(T1.""CommitQty"", 0)) DESC,
    IFNULL(T1.""Quantity"", 0) DESC";

            var dt = await _hana.QueryToDataTableAsync(sql, null, ct);
            if (dt.Rows.Count == 0)
                return null;

            var row = dt.Rows[0];
            var lote = Convert.ToString(row["Lote"]);
            if (string.IsNullOrWhiteSpace(lote))
                return null;

            return new LoteDisponivelSap(
                lote,
                ConverterDataNullable(row["DataFabricacao"]),
                ConverterDataNullable(row["DataValidade"]));
        }
        catch
        {
            return null;
        }
    }

    private async Task<(bool ok, string? error, string? body)> GetOrderForInvoiceAsync(int docEntry, CancellationToken ct)
    {
        using var client = CreateClientWithCookies(noCache: true);

        var url = $"Orders({docEntry})";
        var resp = await client.GetAsync(url, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            return (false, BuildSlError(body, resp), body);

        return (true, null, body);
    }

    private async Task<int> ResolverPedidoVendaDocEntryPorDocNumAsync(int docNum, CancellationToken ct = default)
    {
        try
        {
            var schema = _cfg["SapB1:CompanyDB"] ?? "SBO_BRW_PRD";
            var sql = $@"
SELECT TOP 1 ""DocEntry""
FROM ""{schema}"".""ORDR""
WHERE ""DocNum"" = {docNum}
ORDER BY ""DocEntry"" DESC";

            var dt = await _hana.QueryToDataTableAsync(sql, null, ct);
            if (dt.Rows.Count == 0)
                return 0;

            return Convert.ToInt32(dt.Rows[0]["DocEntry"]);
        }
        catch
        {
            return 0;
        }
    }

    private async Task<(bool ok, int docEntry, int docNum)> LocalizarInvoiceCriadaPorPedidoAsync(int docEntryPedido)
    {
        try
        {
            var schema = _cfg["SapB1:CompanyDB"] ?? "SBO_BRW_PRD";
            var sql = $@"
SELECT TOP 1
    I.""DocEntry"",
    I.""DocNum""
FROM ""{schema}"".""OINV"" I
INNER JOIN ""{schema}"".""INV1"" L
    ON L.""DocEntry"" = I.""DocEntry""
WHERE L.""BaseType"" = 17
  AND L.""BaseEntry"" = {docEntryPedido}
  AND IFNULL(I.""CANCELED"", 'N') = 'N'
ORDER BY I.""DocEntry"" DESC";

            var dt = await _hana.QueryToDataTableAsync(sql, null);
            if (dt.Rows.Count == 0)
                return (true, 0, 0);

            return (
                true,
                Convert.ToInt32(dt.Rows[0]["DocEntry"]),
                Convert.ToInt32(dt.Rows[0]["DocNum"]));
        }
        catch
        {
            return (false, 0, 0);
        }
    }

    private async Task<List<Dictionary<string, object?>>> CriarAdiantamentosParaBaixaNaNotaAsync(int docEntryPedido)
    {
        var resultado = new List<Dictionary<string, object?>>();

        try
        {
            var schema = _cfg["SapB1:CompanyDB"] ?? "SBO_BRW_PRD";
            var sql = $@"
SELECT DISTINCT
    D.""DocEntry"",
    D.""DocNum"",
    D.""DocTotal""
FROM ""{schema}"".""ODPI"" D
INNER JOIN ""{schema}"".""DPI1"" L
    ON L.""DocEntry"" = D.""DocEntry""
WHERE L.""BaseType"" = 17
  AND L.""BaseEntry"" = {docEntryPedido}
  AND IFNULL(D.""CANCELED"", 'N') = 'N'
ORDER BY D.""DocEntry""";

            var dt = await _hana.QueryToDataTableAsync(sql, null);

            foreach (DataRow row in dt.Rows)
            {
                var docEntry = Convert.ToInt32(row["DocEntry"]);
                var docTotal = ConverterDecimal(row["DocTotal"]);

                if (docEntry <= 0 || docTotal <= 0)
                    continue;

                resultado.Add(new Dictionary<string, object?>
                {
                    ["DocEntry"] = docEntry,
                    ["AmountToDraw"] = docTotal,
                    ["DownPaymentType"] = "dptInvoice"
                });
            }
        }
        catch
        {
            return new List<Dictionary<string, object?>>();
        }

        return resultado;
    }

    private static void AplicarDadosPagamentoAVista(Dictionary<string, object?> payload, DateTime dataVencimento)
    {
        payload["U_TX_indPag"] = "0";
        AplicarDadosSemGeracaoDeBoleto(payload);
        payload.Remove("NumberOfInstallments");
        payload.Remove("DocumentInstallments");
    }

    private static void AplicarDadosPagamentoAPrazo(Dictionary<string, object?> payload)
    {
        payload["U_TX_indPag"] = "1";
    }

    private static void AplicarDadosSemGeracaoDeBoleto(Dictionary<string, object?> payload)
    {
        payload["U_GenerateBoeByServ"] = "0";
        payload["BillOfExchangeReserved"] = "tNO";
    }

    private static string? ResolverFormaPagamentoNota(
        JsonElement order,
        int? groupCode,
        bool ehPagamentoAVista,
        bool possuiAdiantamento)
    {
        if (CondicaoPagamentoExigeDeposito(groupCode))
            return "Depósito";

        return ehPagamentoAVista || possuiAdiantamento
            ? null
            : GetJsonStringOrNull(order, "PaymentMethod");
    }

    private static bool CondicaoPagamentoExigeDeposito(int? groupCode)
        => groupCode is 20 or -1;

    private static void AplicarParcelasDaCondicaoPagamento(
        Dictionary<string, object?> payload,
        List<Dictionary<string, object?>> parcelas)
    {
        payload["NumberOfInstallments"] = parcelas.Count;
        payload["DocumentInstallments"] = parcelas;
    }

    private static DateTime? ObterPrimeiroVencimentoDasParcelas(List<Dictionary<string, object?>> parcelas)
    {
        var vencimentos = parcelas
            .Select(p =>
                p.TryGetValue("DueDate", out var dueDate) && dueDate is string text
                    ? TryParseDataSap(text)
                    : null)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .OrderBy(d => d)
            .ToList();

        return vencimentos.Count > 0 ? vencimentos[0] : null;
    }

    private static bool CondicaoPagamentoTemPrazoFuturo(
        List<Dictionary<string, object?>> parcelas,
        DateTime dataEmissao)
    {
        return parcelas.Any(p =>
            p.TryGetValue("DueDate", out var dueDate) &&
            dueDate is string text &&
            TryParseDataSap(text) is DateTime dataParcela &&
            dataParcela.Date > dataEmissao.Date);
    }

    private static DateTime? TryParseDataSap(string text)
    {
        return DateTime.TryParseExact(
            text,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed.Date
            : null;
    }

    private async Task<List<Dictionary<string, object?>>> CriarParcelasDaCondicaoPagamentoAsync(
        JsonElement order,
        int? groupCode,
        DateTime dataEmissao,
        DateTime dataVencimentoPadrao,
        string? nomeCondicaoPagamento,
        CancellationToken ct)
    {
        if (groupCode.HasValue)
        {
            var parcelasCondicao = await CriarParcelasPelaCtg1Async(groupCode.Value, dataEmissao, ct);
            if (parcelasCondicao.Count > 0)
                return parcelasCondicao;
        }

        var parcelasPedido = CriarParcelasProporcionaisDoPedido(order, dataVencimentoPadrao);
        if (parcelasPedido.Count > 1)
            return parcelasPedido;

        var textoCondicao = ObterTextoCondicaoPagamento(order, nomeCondicaoPagamento);
        if (!groupCode.HasValue)
        {
            var parcelasTexto = CriarParcelasPeloTextoCondicaoPagamento(textoCondicao, dataEmissao);
            return parcelasTexto.Count > 0 ? parcelasTexto : parcelasPedido;
        }

        var parcelasNomeCondicao = CriarParcelasPeloTextoCondicaoPagamento(textoCondicao, dataEmissao);
        return parcelasNomeCondicao.Count > 0 ? parcelasNomeCondicao : parcelasPedido;
    }

    private static List<Dictionary<string, object?>> CriarParcelasProporcionaisDoPedido(
        JsonElement order,
        DateTime dataVencimentoPadrao)
    {
        if (!order.TryGetProperty("DocumentInstallments", out var installmentsJson) ||
            installmentsJson.ValueKind != JsonValueKind.Array)
        {
            return CriarParcelaUnica(dataVencimentoPadrao);
        }

        var origem = installmentsJson.EnumerateArray()
            .Select(i => new ParcelaPedidoInfo(
                TryGetJsonDate(i, "DueDate") ?? dataVencimentoPadrao,
                GetJsonDecimal(i, "Percentage"),
                GetJsonDecimal(i, "Total", "InsTotal", "InstallmentTotal")))
            .Where(i => i.Percentage > 0 || i.Total > 0)
            .ToList();

        if (origem.Count == 0)
            return CriarParcelaUnica(dataVencimentoPadrao);

        var somaPercentual = origem.Sum(i => i.Percentage);
        var somaTotal = origem.Sum(i => i.Total);

        var parcelas = new List<Dictionary<string, object?>>();
        var percentualAcumulado = 0m;

        for (var i = 0; i < origem.Count; i++)
        {
            var percentual = somaPercentual > 0
                ? origem[i].Percentage
                : origem[i].Total / somaTotal * 100m;

            percentual = Math.Round(percentual, 6, MidpointRounding.AwayFromZero);

            if (i == origem.Count - 1)
                percentual = Math.Round(100m - percentualAcumulado, 6, MidpointRounding.AwayFromZero);

            percentualAcumulado += percentual;

            parcelas.Add(new Dictionary<string, object?>
            {
                ["DueDate"] = FormatarDataSap(origem[i].DueDate),
                ["Percentage"] = percentual
            });
        }

        return parcelas.Count > 0 ? parcelas : CriarParcelaUnica(dataVencimentoPadrao);
    }

    private async Task<List<Dictionary<string, object?>>> CriarParcelasPelaCtg1Async(
        int groupCode,
        DateTime dataBase,
        CancellationToken ct)
    {
        try
        {
            var schema = _cfg["SapB1:CompanyDB"] ?? "SBO_BRW_PRD";
            var sql = $@"
SELECT
    IFNULL(T0.""ExtraMonth"", 0) + IFNULL(T1.""InstMonth"", 0) AS ""Meses"",
    IFNULL(T0.""ExtraDays"", 0) + IFNULL(T1.""InstDays"", 0) AS ""Dias"",
    CASE
        WHEN T1.""InstNo"" IS NULL THEN 100
        ELSE IFNULL(T1.""InstPrcnt"", 0)
    END AS ""Percentual"",
    CASE
        WHEN T1.""InstNo"" IS NULL THEN 0
        ELSE 1
    END AS ""TemParcela""
FROM ""{schema}"".""OCTG"" T0
LEFT JOIN ""{schema}"".""CTG1"" T1
    ON T1.""CTGCode"" = T0.""GroupNum""
WHERE T0.""GroupNum"" = {groupCode}
ORDER BY IFNULL(T1.""InstNo"", 1)";

            var dt = await _hana.QueryToDataTableAsync(sql, null, ct);
            if (dt.Rows.Count == 0)
                return new List<Dictionary<string, object?>>();

            var origem = dt.Rows
                .Cast<DataRow>()
                .Select(row => new ParcelaCondicaoPagamentoInfo(
                    Convert.ToInt32(row["Meses"]),
                    Convert.ToInt32(row["Dias"]),
                    ConverterDecimal(row["Percentual"]),
                    Convert.ToInt32(row["TemParcela"]) == 1))
                .Where(p => p.TemParcela || p.Meses > 0 || p.Dias > 0)
                .ToList();

            if (origem.Count == 0)
                return new List<Dictionary<string, object?>>();

            var parcelas = new List<Dictionary<string, object?>>();
            var percentualAcumulado = 0m;
            var somaPercentual = origem.Sum(p => p.Percentual);
            var percentualIgual = origem.Count > 0
                ? Math.Round(100m / origem.Count, 6, MidpointRounding.AwayFromZero)
                : 0m;

            for (var i = 0; i < origem.Count; i++)
            {
                var percentual = somaPercentual > 0
                    ? Math.Round(origem[i].Percentual / somaPercentual * 100m, 6, MidpointRounding.AwayFromZero)
                    : percentualIgual;

                if (i == origem.Count - 1)
                    percentual = Math.Round(100m - percentualAcumulado, 6, MidpointRounding.AwayFromZero);

                percentualAcumulado += percentual;

                parcelas.Add(new Dictionary<string, object?>
                {
                    ["DueDate"] = FormatarDataSap(dataBase.Date.AddMonths(origem[i].Meses).AddDays(origem[i].Dias)),
                    ["Percentage"] = percentual
                });
            }

            return parcelas;
        }
        catch
        {
            return new List<Dictionary<string, object?>>();
        }
    }

    private static List<Dictionary<string, object?>> CriarParcelasPeloTextoCondicaoPagamento(
        string? nomeCondicao,
        DateTime dataBase)
    {
        if (string.IsNullOrWhiteSpace(nomeCondicao))
            return new List<Dictionary<string, object?>>();

        var dias = System.Text.RegularExpressions.Regex
            .Matches(nomeCondicao, @"\d+")
            .Select(m => int.TryParse(m.Value, out var dia) ? dia : 0)
            .Where(dia => dia > 0)
            .Distinct()
            .OrderBy(dia => dia)
            .ToList();

        if (dias.Count == 0)
            return new List<Dictionary<string, object?>>();

        if (dias.Count == 1 && !EhTextoCondicaoPrazoUnico(nomeCondicao, dias[0]))
            return new List<Dictionary<string, object?>>();

        var parcelas = new List<Dictionary<string, object?>>();
        var percentualBase = Math.Round(100m / dias.Count, 6, MidpointRounding.AwayFromZero);
        var percentualAcumulado = 0m;

        for (var i = 0; i < dias.Count; i++)
        {
            var percentual = i == dias.Count - 1
                ? Math.Round(100m - percentualAcumulado, 6, MidpointRounding.AwayFromZero)
                : percentualBase;

            percentualAcumulado += percentual;

            parcelas.Add(new Dictionary<string, object?>
            {
                ["DueDate"] = FormatarDataSap(dataBase.Date.AddDays(dias[i])),
                ["Percentage"] = percentual
            });
        }

        return parcelas;
    }

    private static bool EhTextoCondicaoPrazoUnico(string nomeCondicao, int dias)
    {
        var texto = RemoverDiacriticos(nomeCondicao).ToUpperInvariant().Trim();
        var prazo = dias.ToString(CultureInfo.InvariantCulture);

        return System.Text.RegularExpressions.Regex.IsMatch(texto, @"^\d+$") ||
               System.Text.RegularExpressions.Regex.IsMatch(texto, $@"(^|\D){prazo}\s*(D|DD|DDL|DIA|DIAS)\b");
    }

    private static List<Dictionary<string, object?>> CriarParcelaUnica(DateTime dataVencimento)
    {
        return new List<Dictionary<string, object?>>
        {
            new()
            {
                ["DueDate"] = FormatarDataSap(dataVencimento),
                ["Percentage"] = 100m
            }
        };
    }

    private static bool EhCondicaoPagamentoAVista(JsonElement order, string? nomeCondicaoPagamento)
    {
        return EhTextoCondicaoAVista(ObterTextoCondicaoPagamento(order, nomeCondicaoPagamento));
    }

    private static string ObterTextoCondicaoPagamento(JsonElement order, string? nomeCondicaoPagamento)
    {
        return string.Join(' ',
            GetJsonString(order, "PaymentGroupName"),
            GetJsonString(order, "PaymentTermsGroupName"),
            GetJsonString(order, "PaymentMethod"),
            GetJsonString(order, "U_CondicaoPagamento"),
            nomeCondicaoPagamento);
    }

    private async Task<string?> ObterNomeCondicaoPagamentoAsync(int groupCode)
    {
        try
        {
            var schema = _cfg["SapB1:CompanyDB"] ?? "SBO_BRW_PRD";
            var sql = $@"
SELECT TOP 1 ""PymntGroup""
FROM ""{schema}"".""OCTG""
WHERE ""GroupNum"" = {groupCode}";

            var dt = await _hana.QueryToDataTableAsync(sql, null);
            if (dt.Rows.Count == 0)
                return null;

            return Convert.ToString(dt.Rows[0]["PymntGroup"]);
        }
        catch
        {
            return null;
        }
    }

    private async Task<int?> ObterGroupNumPedidoVendaAsync(int docEntryPedido, JsonElement order, CancellationToken ct)
    {
        try
        {
            var schema = _cfg["SapB1:CompanyDB"] ?? "SBO_BRW_PRD";
            var sql = $@"
SELECT TOP 1 ""GroupNum""
FROM ""{schema}"".""ORDR""
WHERE ""DocEntry"" = {docEntryPedido}";

            var dt = await _hana.QueryToDataTableAsync(sql, null, ct);
            if (dt.Rows.Count > 0 && dt.Rows[0]["GroupNum"] != DBNull.Value)
                return Convert.ToInt32(dt.Rows[0]["GroupNum"]);
        }
        catch
        {
            // Mantem fallback pelo payload do Service Layer se a consulta ao HANA falhar.
        }

        return TryGetJsonInt(order, "GroupNum", "PaymentGroupCode");
    }

    private static bool EhTextoCondicaoAVista(string texto)
    {
        texto = RemoverDiacriticos(texto).ToUpperInvariant();

        return texto.Contains("AVISTA", StringComparison.OrdinalIgnoreCase) ||
               texto.Contains("A VISTA", StringComparison.OrdinalIgnoreCase) ||
               texto.Contains("PAGAMENTO_ANTECIPADO", StringComparison.OrdinalIgnoreCase) ||
               texto.Contains("PAGAMENTO ANTECIPADO", StringComparison.OrdinalIgnoreCase) ||
               texto.Contains("ANTECIPADO", StringComparison.OrdinalIgnoreCase);
    }

    private static string RemoverDiacriticos(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return string.Empty;

        var normalizado = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalizado.Length);

        foreach (var ch in normalizado)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static decimal ConverterDecimal(object? valor)
    {
        if (valor is null || valor == DBNull.Value)
            return 0;

        if (valor is decimal d)
            return d;

        if (valor is IConvertible)
            return Convert.ToDecimal(valor, CultureInfo.InvariantCulture);

        return 0;
    }

    private static DateTime? ConverterDataNullable(object? valor)
    {
        if (valor is null || valor == DBNull.Value)
            return null;

        if (valor is DateTime dt)
            return dt;

        if (DateTime.TryParse(Convert.ToString(valor, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var data))
            return data;

        return null;
    }

    private static string EscaparLiteralSql(string valor)
    {
        return valor.Replace("'", "''");
    }

    private sealed record LoteDisponivelSap(
        string Lote,
        DateTime? DataFabricacao,
        DateTime? DataValidade);

    private sealed record ParcelaPedidoInfo(
        DateTime DueDate,
        decimal Percentage,
        decimal Total);

    private sealed record ParcelaCondicaoPagamentoInfo(
        int Meses,
        int Dias,
        decimal Percentual,
        bool TemParcela);

    private sealed record PedidoVendaLineInfo(
        int LineNum,
        string ItemCode,
        decimal OpenQuantity,
        decimal UnitPrice,
        decimal DiscountPercent,
        string WarehouseCode,
        int? Usage);

    private static List<Dictionary<string, object?>> CriarDespesasAdicionaisParaNota(JsonElement order)
    {
        var despesas = new List<Dictionary<string, object?>>();

        if (!order.TryGetProperty("DocumentAdditionalExpenses", out var expensesJson) ||
            expensesJson.ValueKind != JsonValueKind.Array)
        {
            return despesas;
        }

        foreach (var expense in expensesJson.EnumerateArray())
        {
            var expenseCode = TryGetJsonInt(expense, "ExpenseCode", "ExpensesCode");
            var lineTotal = GetJsonDecimal(expense, "LineTotal");

            if (!expenseCode.HasValue || expenseCode.Value <= 0 || lineTotal <= 0)
                continue;

            var payload = new Dictionary<string, object?>
            {
                ["ExpenseCode"] = expenseCode.Value,
                ["LineTotal"] = lineTotal
            };

            CopiarStringSeExistir(expense, payload, "TaxCode");
            CopiarStringSeExistir(expense, payload, "VatGroup");
            CopiarStringSeExistir(expense, payload, "DistributionMethod");
            CopiarStringSeExistir(expense, payload, "Remarks");
            CopiarStringSeExistir(expense, payload, "Project");
            CopiarStringSeExistir(expense, payload, "DistributionRule");
            CopiarStringSeExistir(expense, payload, "DistributionRule2");
            CopiarStringSeExistir(expense, payload, "DistributionRule3");
            CopiarStringSeExistir(expense, payload, "DistributionRule4");
            CopiarStringSeExistir(expense, payload, "DistributionRule5");

            CopiarIntSeExistir(expense, payload, "LocationCode");

            RemoverNulos(payload);
            despesas.Add(payload);
        }

        return despesas;
    }

    private static decimal GetJsonDecimal(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
                return number;

            if (value.ValueKind == JsonValueKind.String &&
                decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number))
            {
                return number;
            }
        }

        return 0;
    }

    private static int? TryGetJsonInt(JsonElement element, string name)
        => TryGetJsonInt(element, [name]);

    private static int? TryGetJsonInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
                continue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
                return number;

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
                return number;
        }

        return null;
    }

    private static void CopiarStringSeExistir(
        JsonElement origem,
        Dictionary<string, object?> destino,
        string propriedade)
    {
        if (!origem.TryGetProperty(propriedade, out var value) || value.ValueKind == JsonValueKind.Null)
            return;

        if (value.ValueKind != JsonValueKind.String)
            return;

        var texto = value.GetString();
        if (!string.IsNullOrWhiteSpace(texto))
            destino[propriedade] = texto;
    }

    private static void CopiarIntSeExistir(
        JsonElement origem,
        Dictionary<string, object?> destino,
        string propriedade)
    {
        var valor = TryGetJsonInt(origem, propriedade);
        if (valor.HasValue)
            destino[propriedade] = valor.Value;
    }

    private static string? GetJsonStringOrNull(JsonElement element, string name)
    {
        var value = GetJsonString(element, name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void RemoverNulos(Dictionary<string, object?> payload)
    {
        foreach (var key in payload.Where(kv => kv.Value is null).Select(kv => kv.Key).ToList())
            payload.Remove(key);
    }

    private async Task<List<string>> ObterColunasComunsReferenciasFiscaisAsync(string schema)
    {
        var schemaEscapado = schema.Replace("'", "''");

        var sql = $@"
SELECT RIN.""COLUMN_NAME""
FROM ""SYS"".""TABLE_COLUMNS"" RIN
INNER JOIN ""SYS"".""TABLE_COLUMNS"" DRF
    ON DRF.""SCHEMA_NAME"" = RIN.""SCHEMA_NAME""
   AND DRF.""TABLE_NAME"" = 'DRF21'
   AND DRF.""COLUMN_NAME"" = RIN.""COLUMN_NAME""
WHERE RIN.""SCHEMA_NAME"" = '{schemaEscapado}'
  AND RIN.""TABLE_NAME"" = 'RIN21'
  AND RIN.""COLUMN_NAME"" <> 'DocEntry'
ORDER BY RIN.""POSITION""";

        var dt = await _hana.QueryToDataTableAsync(sql, null);

        return dt.Rows
            .Cast<DataRow>()
            .Select(row => Convert.ToString(row["COLUMN_NAME"]))
            .Where(coluna => !string.IsNullOrWhiteSpace(coluna))
            .Select(coluna => coluna!)
            .ToList();
    }

    private static string QuoteHanaIdentifier(string identifier)
    {
        return $@"""{identifier.Replace(@"""", @"""""")}""";
    }

    private async Task ExecutarSqlHanaAsync(string sql, CancellationToken ct = default)
    {
        var connStr = _cfg.GetConnectionString("HanaConn");

        if (string.IsNullOrWhiteSpace(connStr))
            throw new InvalidOperationException("ConnectionStrings:HanaConn não configurada.");

        await using var conn = new OdbcConnection(connStr);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 120;

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<HttpResponseMessage> PatchAsync(string endpoint, object body)
    {
        using var client = CreateClientWithCookies(noCache: true);

        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions
        {
            PropertyNamingPolicy = null
        });

        var request = new HttpRequestMessage(
            new HttpMethod("PATCH"),
            endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        return await client.SendAsync(request);
    }
}
