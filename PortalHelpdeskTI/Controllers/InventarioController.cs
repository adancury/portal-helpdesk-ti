using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PortalHelpdeskTI.Models.Inventario;
using PortalHelpdeskTI.Services.Inventario;
using System.Security.Cryptography;
using System.Text;

namespace PortalHelpdeskTI.Controllers;

public class InventarioController : Controller
{
    private const string ApiKeyHeaderName = "X-Inventario-Api-Key";
    private const long TamanhoMaximoAnexoBytes = 20 * 1024 * 1024;

    private static readonly string[] Tipos =
    [
        "Computador",
        "Celular",
        "Switch",
        "Coletor",
        "Impressora",
        "Roteador",
        "Access Point",
        "Servidor",
        "Outro"
    ];

    private static readonly string[] Status =
    [
        "Ativo",
        "Em manutenção",
        "Reserva",
        "Inativo",
        "Descartado"
    ];

    private readonly AppDbContext _context;
    private readonly InventarioRedeService _redeService;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public InventarioController(AppDbContext context, InventarioRedeService redeService, IConfiguration configuration, IWebHostEnvironment environment)
    {
        _context = context;
        _redeService = redeService;
        _configuration = configuration;
        _environment = environment;
    }

    [HttpGet("/Inventario")]
    [HttpGet("/Inventario/Index")]
    public async Task<IActionResult> Index(string? filtro, string? tipo, string? status, string? origem, CancellationToken ct)
    {
        var query = _context.InventarioEquipamentos
            .Include(e => e.ProprietarioUsuario)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filtro))
        {
            var termo = filtro.Trim();
            query = query.Where(e =>
                (e.NomeEquipamento ?? "").Contains(termo) ||
                (e.Hostname ?? "").Contains(termo) ||
                (e.EnderecoIp ?? "").Contains(termo) ||
                (e.EnderecoMac ?? "").Contains(termo) ||
                (e.Patrimonio ?? "").Contains(termo) ||
                (e.NumeroSerie ?? "").Contains(termo) ||
                (e.ProprietarioNomeManual ?? "").Contains(termo) ||
                (e.ProprietarioUsuario != null && e.ProprietarioUsuario.Nome.Contains(termo)));
        }

        if (!string.IsNullOrWhiteSpace(tipo))
            query = query.Where(e => e.Tipo == tipo);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(e => e.Status == status);

        if (!string.IsNullOrWhiteSpace(origem))
            query = query.Where(e => e.OrigemCadastro == origem);

        ViewBag.Tipos = Tipos;
        ViewBag.Status = Status;
        ViewBag.Filtro = filtro;
        ViewBag.TipoSelecionado = tipo;
        ViewBag.StatusSelecionado = status;
        ViewBag.OrigemSelecionada = origem;

        var equipamentos = await query
            .OrderBy(e => e.Tipo)
            .ThenBy(e => e.NomeEquipamento ?? e.Hostname ?? e.EnderecoIp)
            .ToListAsync(ct);

        return View(equipamentos);
    }

    [HttpGet]
    public async Task<IActionResult> Novo(CancellationToken ct)
    {
        await CarregarCombosAsync(ct);
        return View("Form", new EquipamentoInventario());
    }

    [HttpGet]
    public async Task<IActionResult> Editar(int id, CancellationToken ct)
    {
        var equipamento = await _context.InventarioEquipamentos
            .Include(e => e.Anexos.OrderByDescending(a => a.CriadoEm))
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (equipamento == null) return NotFound();

        await CarregarCombosAsync(ct);
        return View("Form", equipamento);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Salvar(EquipamentoInventario model, List<IFormFile>? anexos, CancellationToken ct)
    {
        Normalizar(model);

        if (!ModelState.IsValid)
        {
            if (model.Id > 0)
            {
                model.Anexos = await _context.InventarioAnexos
                    .AsNoTracking()
                    .Where(a => a.EquipamentoInventarioId == model.Id)
                    .OrderByDescending(a => a.CriadoEm)
                    .ToListAsync(ct);
            }

            await CarregarCombosAsync(ct);
            return View("Form", model);
        }

        EquipamentoInventario equipamento;

        if (model.Id == 0)
        {
            model.CriadoEm = DateTime.Now;
            model.AtualizadoEm = DateTime.Now;
            _context.InventarioEquipamentos.Add(model);
            equipamento = model;
            TempData["ToastMsg"] = "Equipamento cadastrado no inventário.";
        }
        else
        {
            var existente = await _context.InventarioEquipamentos.FirstOrDefaultAsync(e => e.Id == model.Id, ct);
            if (existente == null) return NotFound();

            existente.Tipo = model.Tipo;
            existente.Status = model.Status;
            existente.OrigemCadastro = model.OrigemCadastro;
            existente.NomeEquipamento = model.NomeEquipamento;
            existente.Fabricante = model.Fabricante;
            existente.Modelo = model.Modelo;
            existente.NumeroSerie = model.NumeroSerie;
            existente.Patrimonio = model.Patrimonio;
            existente.EnderecoIp = model.EnderecoIp;
            existente.EnderecoMac = model.EnderecoMac;
            existente.Hostname = model.Hostname;
            existente.SistemaOperacional = model.SistemaOperacional;
            existente.Localizacao = model.Localizacao;
            existente.ProprietarioUsuarioId = model.ProprietarioUsuarioId;
            existente.ProprietarioNomeManual = model.ProprietarioNomeManual;
            existente.ProprietarioEmailManual = model.ProprietarioEmailManual;
            existente.ProprietarioDepartamentoManual = model.ProprietarioDepartamentoManual;
            existente.Observacoes = model.Observacoes;
            existente.AtualizadoEm = DateTime.Now;
            equipamento = existente;
            TempData["ToastMsg"] = "Equipamento atualizado.";
        }

        await _context.SaveChangesAsync(ct);
        await SalvarAnexosAsync(equipamento.Id, anexos, ct);

        TempData["ToastTipo"] = "success";
        await _context.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Editar), new { id = equipamento.Id });
    }

    [HttpGet]
    public async Task<IActionResult> BaixarAnexo(int id, CancellationToken ct)
    {
        var anexo = await _context.InventarioAnexos.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (anexo == null) return NotFound();

        var caminho = ObterCaminhoFisicoAnexo(anexo.CaminhoRelativo);
        if (!System.IO.File.Exists(caminho)) return NotFound();

        var contentType = string.IsNullOrWhiteSpace(anexo.ContentType)
            ? "application/octet-stream"
            : anexo.ContentType;

        return PhysicalFile(caminho, contentType, anexo.NomeOriginal);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExcluirAnexo(int id, CancellationToken ct)
    {
        var anexo = await _context.InventarioAnexos.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (anexo == null) return NotFound();

        var equipamentoId = anexo.EquipamentoInventarioId;
        var caminho = ObterCaminhoFisicoAnexo(anexo.CaminhoRelativo);

        _context.InventarioAnexos.Remove(anexo);
        await _context.SaveChangesAsync(ct);

        if (System.IO.File.Exists(caminho))
            System.IO.File.Delete(caminho);

        TempData["ToastTipo"] = "warning";
        TempData["ToastMsg"] = "Anexo removido.";
        return RedirectToAction(nameof(Editar), new { id = equipamentoId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Desativar(int id, CancellationToken ct)
    {
        var equipamento = await _context.InventarioEquipamentos.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (equipamento == null) return NotFound();

        equipamento.Status = "Inativo";
        equipamento.AtualizadoEm = DateTime.Now;
        await _context.SaveChangesAsync(ct);

        TempData["ToastTipo"] = "warning";
        TempData["ToastMsg"] = "Equipamento marcado como inativo.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Descobrir()
    {
        return View(new InventarioDescobertaViewModel
        {
            Faixa = _redeService.ObterFaixaPadrao()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Descobrir(InventarioDescobertaViewModel model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.Faixa))
            model.Faixa = _redeService.ObterFaixaPadrao();

        model.Resultados = await _redeService.DescobrirAsync(model.Faixa, ct);
        await MarcarJaCadastradosAsync(model.Resultados, ct);
        model.Mensagem = $"{model.Resultados.Count} equipamento(s) online encontrado(s).";

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportarDescoberto(
        string enderecoIp,
        string? enderecoMac,
        string? hostname,
        string? sistemaOperacional,
        string? fabricante,
        string? modelo,
        string? numeroSerie,
        string tipo,
        CancellationToken ct)
    {
        enderecoMac = InventarioRedeService.NormalizarMac(enderecoMac);
        var equipamento = await BuscarPorRedeAsync(enderecoIp, enderecoMac, ct);

        if (equipamento == null)
        {
            equipamento = new EquipamentoInventario
            {
                Tipo = string.IsNullOrWhiteSpace(tipo) ? "Computador" : tipo,
                Status = "Ativo",
                OrigemCadastro = "Rede",
                EnderecoIp = enderecoIp,
                EnderecoMac = enderecoMac,
                Hostname = hostname,
                NomeEquipamento = hostname,
                SistemaOperacional = Limpar(sistemaOperacional),
                Fabricante = Limpar(fabricante),
                Modelo = Limpar(modelo),
                NumeroSerie = Limpar(numeroSerie),
                UltimaDescobertaEm = DateTime.Now
            };
            _context.InventarioEquipamentos.Add(equipamento);
            TempData["ToastMsg"] = "Equipamento importado da rede.";
        }
        else
        {
            equipamento.EnderecoIp = enderecoIp;
            equipamento.EnderecoMac = enderecoMac ?? equipamento.EnderecoMac;
            equipamento.Hostname = hostname ?? equipamento.Hostname;
            equipamento.NomeEquipamento ??= hostname;
            AplicarDadosDescobertos(equipamento, new InventarioDescobertaResultado
            {
                SistemaOperacional = sistemaOperacional,
                Fabricante = fabricante,
                Modelo = modelo,
                NumeroSerie = numeroSerie
            });
            equipamento.UltimaDescobertaEm = DateTime.Now;
            equipamento.AtualizadoEm = DateTime.Now;
            TempData["ToastMsg"] = "Equipamento já existia e foi atualizado pela descoberta.";
        }

        TempData["ToastTipo"] = "success";
        await _context.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Editar), new { id = equipamento.Id });
    }

    [HttpPost("/api/inventario/equipamentos/coleta")]
    public async Task<IActionResult> ReceberColeta([FromBody] InventarioColetaRequest request, CancellationToken ct)
    {
        if (!ApiKeyValida())
            return Unauthorized(new { mensagem = "Chave de API do inventario invalida ou nao configurada." });

        if (request == null)
            return BadRequest(new { mensagem = "Payload invalido." });

        request.EnderecoMac = InventarioRedeService.NormalizarMac(request.EnderecoMac);
        NormalizarColeta(request);

        if (string.IsNullOrWhiteSpace(request.EnderecoMac) &&
            string.IsNullOrWhiteSpace(request.NumeroSerie) &&
            string.IsNullOrWhiteSpace(request.Hostname) &&
            string.IsNullOrWhiteSpace(request.NomeComputador))
        {
            return BadRequest(new { mensagem = "Informe ao menos MAC, numero de serie ou hostname." });
        }

        var equipamento = await BuscarPorColetaAsync(request, ct);
        var criado = equipamento == null;
        var agora = DateTime.Now;

        if (equipamento == null)
        {
            equipamento = new EquipamentoInventario
            {
                Tipo = "Computador",
                Status = "Ativo",
                OrigemCadastro = "Script",
                CriadoEm = agora
            };

            _context.InventarioEquipamentos.Add(equipamento);
        }

        equipamento.OrigemCadastro = "Script";
        equipamento.AtualizadoEm = agora;
        equipamento.UltimaDescobertaEm = agora;

        equipamento.NomeEquipamento = PrimeiroValor(request.NomeComputador, request.Hostname, equipamento.NomeEquipamento);
        equipamento.Hostname = PrimeiroValor(request.Hostname, request.NomeComputador, equipamento.Hostname);
        equipamento.SistemaOperacional = PrimeiroValor(request.SistemaOperacional, equipamento.SistemaOperacional);
        equipamento.Fabricante = PrimeiroValor(request.Fabricante, equipamento.Fabricante);
        equipamento.Modelo = PrimeiroValor(request.Modelo, equipamento.Modelo);
        equipamento.NumeroSerie = PrimeiroValor(request.NumeroSerie, equipamento.NumeroSerie);
        equipamento.EnderecoIp = PrimeiroValor(request.EnderecoIp, equipamento.EnderecoIp);
        equipamento.EnderecoMac = PrimeiroValor(request.EnderecoMac, equipamento.EnderecoMac);
        equipamento.Localizacao = PrimeiroValor(request.Localizacao, equipamento.Localizacao);

        await _context.SaveChangesAsync(ct);

        return Ok(new
        {
            mensagem = criado ? "Equipamento cadastrado pelo script." : "Equipamento atualizado pelo script.",
            equipamento.Id,
            criado
        });
    }

    private async Task MarcarJaCadastradosAsync(List<InventarioDescobertaResultado> resultados, CancellationToken ct)
    {
        var macs = resultados
            .Select(r => InventarioRedeService.NormalizarMac(r.EnderecoMac))
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .ToList();
        var ips = resultados.Select(r => r.EnderecoIp).ToList();

        var cadastrados = await _context.InventarioEquipamentos
            .Where(e => (e.EnderecoMac != null && macs.Contains(e.EnderecoMac)) || (e.EnderecoIp != null && ips.Contains(e.EnderecoIp)))
            .ToListAsync(ct);

        foreach (var resultado in resultados)
        {
            var mac = InventarioRedeService.NormalizarMac(resultado.EnderecoMac);
            var existente = cadastrados.FirstOrDefault(e =>
                (!string.IsNullOrWhiteSpace(mac) && e.EnderecoMac == mac) ||
                (!string.IsNullOrWhiteSpace(resultado.EnderecoIp) && e.EnderecoIp == resultado.EnderecoIp));

            if (existente == null) continue;

            resultado.JaCadastrado = true;
            resultado.EquipamentoId = existente.Id;
            resultado.NomeCadastrado = existente.NomeEquipamento ?? existente.Hostname;
            resultado.TipoCadastrado = existente.Tipo;
            AplicarDadosDescobertos(existente, resultado);
        }

        if (_context.ChangeTracker.HasChanges())
            await _context.SaveChangesAsync(ct);
    }

    private Task<EquipamentoInventario?> BuscarPorRedeAsync(string enderecoIp, string? enderecoMac, CancellationToken ct)
    {
        return _context.InventarioEquipamentos.FirstOrDefaultAsync(e =>
            (!string.IsNullOrWhiteSpace(enderecoMac) && e.EnderecoMac == enderecoMac) ||
            (!string.IsNullOrWhiteSpace(enderecoIp) && e.EnderecoIp == enderecoIp), ct);
    }

    private Task<EquipamentoInventario?> BuscarPorColetaAsync(InventarioColetaRequest request, CancellationToken ct)
    {
        var hostname = PrimeiroValor(request.Hostname, request.NomeComputador);

        return _context.InventarioEquipamentos.FirstOrDefaultAsync(e =>
            (!string.IsNullOrWhiteSpace(request.EnderecoMac) && e.EnderecoMac == request.EnderecoMac) ||
            (!string.IsNullOrWhiteSpace(request.NumeroSerie) && e.NumeroSerie == request.NumeroSerie) ||
            (!string.IsNullOrWhiteSpace(hostname) && (e.Hostname == hostname || e.NomeEquipamento == hostname)), ct);
    }

    private async Task CarregarCombosAsync(CancellationToken ct)
    {
        ViewBag.Tipos = Tipos.Select(t => new SelectListItem(t, t)).ToList();
        ViewBag.Status = Status.Select(s => new SelectListItem(s, s)).ToList();
        ViewBag.Origens = new[] { "Manual", "Rede", "Script" }.Select(o => new SelectListItem(o, o)).ToList();
        ViewBag.Usuarios = await _context.Usuarios
            .AsNoTracking()
            .Where(u => u.Ativo)
            .OrderBy(u => u.Nome)
            .Select(u => new SelectListItem($"{u.Nome} ({u.Email})", u.Id.ToString()))
            .ToListAsync(ct);
    }

    private async Task SalvarAnexosAsync(int equipamentoId, List<IFormFile>? anexos, CancellationToken ct)
    {
        if (anexos == null || anexos.Count == 0) return;

        var pastaRelativa = Path.Combine("uploads", "inventario", equipamentoId.ToString());
        var pastaFisica = Path.Combine(_environment.WebRootPath, pastaRelativa);
        Directory.CreateDirectory(pastaFisica);

        foreach (var arquivo in anexos.Where(a => a is { Length: > 0 }))
        {
            if (arquivo.Length > TamanhoMaximoAnexoBytes)
            {
                TempData["ToastTipo"] = "warning";
                TempData["ToastMsg"] = "Um ou mais anexos excedem 20 MB e foram ignorados.";
                continue;
            }

            var nomeOriginal = Path.GetFileName(arquivo.FileName);
            var extensao = Path.GetExtension(nomeOriginal);
            var nomeArquivo = $"{Guid.NewGuid():N}{extensao}";
            var caminhoFisico = Path.Combine(pastaFisica, nomeArquivo);

            await using (var stream = System.IO.File.Create(caminhoFisico))
            {
                await arquivo.CopyToAsync(stream, ct);
            }

            _context.InventarioAnexos.Add(new InventarioAnexo
            {
                EquipamentoInventarioId = equipamentoId,
                NomeOriginal = nomeOriginal,
                NomeArquivo = nomeArquivo,
                CaminhoRelativo = Path.Combine(pastaRelativa, nomeArquivo).Replace('\\', '/'),
                ContentType = arquivo.ContentType,
                TamanhoBytes = arquivo.Length,
                CriadoEm = DateTime.Now
            });
        }
    }

    private string ObterCaminhoFisicoAnexo(string caminhoRelativo)
    {
        var relativoNormalizado = caminhoRelativo
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        var raiz = Path.GetFullPath(_environment.WebRootPath);
        var caminho = Path.GetFullPath(Path.Combine(raiz, relativoNormalizado));

        if (!caminho.StartsWith(raiz, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Caminho de anexo invalido.");

        return caminho;
    }

    private static void Normalizar(EquipamentoInventario model)
    {
        model.EnderecoMac = InventarioRedeService.NormalizarMac(model.EnderecoMac);
        model.Tipo = string.IsNullOrWhiteSpace(model.Tipo) ? "Outro" : model.Tipo.Trim();
        model.Status = string.IsNullOrWhiteSpace(model.Status) ? "Ativo" : model.Status.Trim();
        model.OrigemCadastro = string.IsNullOrWhiteSpace(model.OrigemCadastro) ? "Manual" : model.OrigemCadastro.Trim();

        if (model.ProprietarioUsuarioId.HasValue)
        {
            model.ProprietarioNomeManual = null;
            model.ProprietarioEmailManual = null;
            model.ProprietarioDepartamentoManual = null;
        }
    }

    private void NormalizarColeta(InventarioColetaRequest request)
    {
        request.NomeComputador = Limpar(request.NomeComputador);
        request.Hostname = Limpar(request.Hostname);
        request.SistemaOperacional = Limpar(request.SistemaOperacional);
        request.Fabricante = Limpar(request.Fabricante);
        request.Modelo = Limpar(request.Modelo);
        request.NumeroSerie = Limpar(request.NumeroSerie);
        request.EnderecoIp = Limpar(request.EnderecoIp);
        request.EnderecoMac = Limpar(request.EnderecoMac);
        request.Localizacao = Limpar(request.Localizacao);
        request.UsuarioLogado = Limpar(request.UsuarioLogado);
        request.Dominio = Limpar(request.Dominio);
    }

    private bool ApiKeyValida()
    {
        var chaveConfigurada = _configuration["Inventario:ApiKey"];
        if (string.IsNullOrWhiteSpace(chaveConfigurada))
            return false;

        if (!Request.Headers.TryGetValue(ApiKeyHeaderName, out var chaveRecebida))
            return false;

        var esperada = Encoding.UTF8.GetBytes(chaveConfigurada);
        var recebida = Encoding.UTF8.GetBytes(chaveRecebida.ToString());

        return esperada.Length == recebida.Length &&
            CryptographicOperations.FixedTimeEquals(esperada, recebida);
    }

    private static void AplicarDadosDescobertos(EquipamentoInventario equipamento, InventarioDescobertaResultado descoberta)
    {
        equipamento.Hostname = PrimeiroValor(descoberta.Hostname, equipamento.Hostname);
        equipamento.NomeEquipamento = PrimeiroValor(equipamento.NomeEquipamento, descoberta.Hostname);
        equipamento.SistemaOperacional = PrimeiroValor(descoberta.SistemaOperacional, equipamento.SistemaOperacional);
        equipamento.Fabricante = PrimeiroValor(descoberta.Fabricante, equipamento.Fabricante);
        equipamento.Modelo = PrimeiroValor(descoberta.Modelo, equipamento.Modelo);
        equipamento.NumeroSerie = PrimeiroValor(descoberta.NumeroSerie, equipamento.NumeroSerie);

        if (!string.IsNullOrWhiteSpace(descoberta.SistemaOperacional) ||
            !string.IsNullOrWhiteSpace(descoberta.Fabricante) ||
            !string.IsNullOrWhiteSpace(descoberta.Modelo) ||
            !string.IsNullOrWhiteSpace(descoberta.NumeroSerie))
        {
            equipamento.UltimaDescobertaEm = DateTime.Now;
            equipamento.AtualizadoEm = DateTime.Now;
        }
    }

    private static string? PrimeiroValor(params string?[] valores)
    {
        return valores.Select(Limpar).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }

    private static string? Limpar(string? valor)
    {
        valor = valor?.Trim();
        return string.IsNullOrWhiteSpace(valor) ? null : valor;
    }
}
