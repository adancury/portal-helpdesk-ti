using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using PortalHelpdeskTI.Models.Inventario;

namespace PortalHelpdeskTI.Services.Inventario;

public class InventarioRedeService
{
    private readonly string? _windowsUsername;
    private readonly string? _windowsPassword;

    private static readonly Regex ArpLinhaRegex = new(
        @"(?<ip>(?:\d{1,3}\.){3}\d{1,3})\s+(?<mac>(?:[0-9a-fA-F]{2}[-:]){5}[0-9a-fA-F]{2})",
        RegexOptions.Compiled);

    public InventarioRedeService(IConfiguration configuration)
    {
        _windowsUsername = LimparValor(configuration["Inventario:WindowsUsername"]);
        _windowsPassword = LimparValor(configuration["Inventario:WindowsPassword"]);
    }

    public string ObterFaixaPadrao()
    {
        var ip = Dns.GetHostEntry(Dns.GetHostName())
            .AddressList
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));

        if (ip == null) return "192.168.0.1-254";

        var partes = ip.ToString().Split('.');
        return $"{partes[0]}.{partes[1]}.{partes[2]}.1-254";
    }

    public async Task<List<InventarioDescobertaResultado>> DescobrirAsync(string faixa, CancellationToken ct = default)
    {
        var ips = ExpandirFaixa(faixa).Distinct().Take(512).ToList();
        var encontrados = new ConcurrentBag<InventarioDescobertaResultado>();
        using var semaforo = new SemaphoreSlim(64);

        var tarefas = ips.Select(async ip =>
        {
            await semaforo.WaitAsync(ct);
            try
            {
                if (!await EstaOnlineAsync(ip, ct)) return;

                encontrados.Add(new InventarioDescobertaResultado
                {
                    EnderecoIp = ip,
                    Online = true,
                    Hostname = await ResolverHostnameAsync(ip)
                });
            }
            finally
            {
                semaforo.Release();
            }
        });

        await Task.WhenAll(tarefas);

        var macs = ObterTabelaArp();
        var lista = encontrados.OrderBy(x => IpParaNumero(x.EnderecoIp)).ToList();

        foreach (var item in lista)
        {
            if (macs.TryGetValue(item.EnderecoIp, out var mac))
                item.EnderecoMac = NormalizarMac(mac);
        }

        await ColetarDetalhesAvancadosAsync(lista, ct);

        return lista;
    }

    private static async Task<bool> EstaOnlineAsync(string ip, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var resposta = await ping.SendPingAsync(ip, 800);
            ct.ThrowIfCancellationRequested();
            return resposta.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string?> ResolverHostnameAsync(string ip)
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(ip);
            return entry.HostName;
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, string> ObterTabelaArp()
    {
        var tabela = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var processo = Process.Start(new ProcessStartInfo
            {
                FileName = "arp",
                Arguments = "-a",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            });

            if (processo == null) return tabela;

            var saida = processo.StandardOutput.ReadToEnd();
            processo.WaitForExit(3000);

            foreach (Match match in ArpLinhaRegex.Matches(saida))
            {
                tabela[match.Groups["ip"].Value] = match.Groups["mac"].Value;
            }
        }
        catch
        {
            // Sem ARP a descoberta ainda retorna IP/hostname.
        }

        return tabela;
    }

    private async Task ColetarDetalhesAvancadosAsync(List<InventarioDescobertaResultado> equipamentos, CancellationToken ct)
    {
        using var semaforo = new SemaphoreSlim(8);

        var tarefas = equipamentos.Select(async equipamento =>
        {
            await semaforo.WaitAsync(ct);
            try
            {
                var detalhes = await ColetarDetalhesWindowsAsync(equipamento.EnderecoIp, ct);
                if (detalhes == null) return;

                equipamento.Hostname = PrimeiroValor(detalhes.Hostname, equipamento.Hostname);
                equipamento.SistemaOperacional = detalhes.SistemaOperacional;
                equipamento.Fabricante = detalhes.Fabricante;
                equipamento.Modelo = detalhes.Modelo;
                equipamento.NumeroSerie = detalhes.NumeroSerie;
            }
            finally
            {
                semaforo.Release();
            }
        });

        await Task.WhenAll(tarefas);
    }

    private async Task<DetalhesWindows?> ColetarDetalhesWindowsAsync(string ip, CancellationToken ct)
    {
        try
        {
            var script = string.Join(' ',
                "$ErrorActionPreference='Stop';",
                "$usuario=$env:PORTAL_INVENTARIO_WIN_USER;",
                "$senha=$env:PORTAL_INVENTARIO_WIN_PASSWORD;",
                "$cred=$null;",
                "if(-not [string]::IsNullOrWhiteSpace($usuario) -and -not [string]::IsNullOrWhiteSpace($senha)){",
                "$segura=ConvertTo-SecureString $senha -AsPlainText -Force;",
                "$cred=New-Object System.Management.Automation.PSCredential($usuario,$segura);",
                "}",
                $"$params=@{{ComputerName='{ip}'}};",
                "if($cred -ne $null){$params.Credential=$cred}",
                "$cs=Get-CimInstance Win32_ComputerSystem @params;",
                "$os=Get-CimInstance Win32_OperatingSystem @params;",
                "$bios=Get-CimInstance Win32_BIOS @params;",
                "[pscustomobject]@{",
                "Hostname=$cs.Name;",
                "Fabricante=$cs.Manufacturer;",
                "Modelo=$cs.Model;",
                "NumeroSerie=$bios.SerialNumber;",
                "SistemaOperacional=(($os.Caption,$os.Version) -join ' ')",
                "} | ConvertTo-Json -Compress");

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            if (!string.IsNullOrWhiteSpace(_windowsUsername) && !string.IsNullOrWhiteSpace(_windowsPassword))
            {
                startInfo.Environment["PORTAL_INVENTARIO_WIN_USER"] = _windowsUsername;
                startInfo.Environment["PORTAL_INVENTARIO_WIN_PASSWORD"] = _windowsPassword;
            }

            using var processo = Process.Start(startInfo);
            if (processo == null) return null;

            var saidaTask = processo.StandardOutput.ReadToEndAsync(ct);
            _ = processo.StandardError.ReadToEndAsync(ct);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            try
            {
                await processo.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { processo.Kill(entireProcessTree: true); } catch { }
                return null;
            }

            if (processo.ExitCode != 0) return null;

            var saida = await saidaTask;
            if (string.IsNullOrWhiteSpace(saida)) return null;

            using var json = JsonDocument.Parse(saida);
            var root = json.RootElement;

            return new DetalhesWindows
            {
                Hostname = LerString(root, "Hostname"),
                Fabricante = LerString(root, "Fabricante"),
                Modelo = LerString(root, "Modelo"),
                NumeroSerie = LerString(root, "NumeroSerie"),
                SistemaOperacional = LerString(root, "SistemaOperacional")
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? LerString(JsonElement root, string propriedade)
    {
        return root.TryGetProperty(propriedade, out var valor)
            ? LimparValor(valor.GetString())
            : null;
    }

    private static string? PrimeiroValor(params string?[] valores)
    {
        return valores.Select(LimparValor).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }

    private static string? LimparValor(string? valor)
    {
        valor = valor?.Trim();
        return string.IsNullOrWhiteSpace(valor) ? null : valor;
    }

    private static IEnumerable<string> ExpandirFaixa(string faixa)
    {
        faixa = (faixa ?? "").Trim();
        if (string.IsNullOrWhiteSpace(faixa))
            yield break;

        if (faixa.Contains('/'))
        {
            foreach (var ip in ExpandirCidr24(faixa))
                yield return ip;
            yield break;
        }

        var partes = faixa.Split('.');
        if (partes.Length == 4 && partes[3].Contains('-'))
        {
            var prefixo = string.Join('.', partes.Take(3));
            var intervalo = partes[3].Split('-', 2);

            if (int.TryParse(intervalo[0], out var inicio) &&
                int.TryParse(intervalo[1], out var fim))
            {
                foreach (var ultimo in Enumerable.Range(Math.Max(1, inicio), Math.Min(254, fim) - Math.Max(1, inicio) + 1))
                    yield return $"{prefixo}.{ultimo}";
            }

            yield break;
        }

        if (IPAddress.TryParse(faixa, out _))
            yield return faixa;
    }

    private static IEnumerable<string> ExpandirCidr24(string cidr)
    {
        var partes = cidr.Split('/', 2);
        if (partes.Length != 2 || partes[1] != "24") yield break;
        if (!IPAddress.TryParse(partes[0], out var ip)) yield break;

        var octetos = ip.ToString().Split('.');
        var prefixo = $"{octetos[0]}.{octetos[1]}.{octetos[2]}";
        for (var i = 1; i <= 254; i++)
            yield return $"{prefixo}.{i}";
    }

    private static long IpParaNumero(string ip)
    {
        var partes = ip.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        return partes.Aggregate(0L, (atual, parte) => (atual << 8) + parte);
    }

    public static string? NormalizarMac(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return null;
        return mac.Trim().Replace('-', ':').ToUpperInvariant();
    }

    private sealed class DetalhesWindows
    {
        public string? Hostname { get; set; }
        public string? SistemaOperacional { get; set; }
        public string? Fabricante { get; set; }
        public string? Modelo { get; set; }
        public string? NumeroSerie { get; set; }
    }
}
