using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PortalHelpdeskTI.Models;
using PortalHelpdeskTI.Models.Permissoes;
using PortalHelpdeskTI.Services;
using PortalHelpdeskTI.Services.Permissoes;
using PortalHelpdeskTI.ViewModels.Permissoes;
using System.Linq;
using static PortalHelpdeskTI.Controllers.CategoriaChamadoController;

namespace PortalHelpdeskTI.Controllers
{
    public class ConfiguracoesController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IRelatorioPermissaoService _relPerms;

        public ConfiguracoesController(AppDbContext context, IRelatorioPermissaoService relPerms)
        {
            _context = context;
            _relPerms = relPerms;
        }

        public class CatalogoInput
        {
            public int Id { get; set; } // 0 = novo
            public string Key { get; set; } = "";
            public string Titulo { get; set; } = "";
            public string? Descricao { get; set; }
            public string? Departamento { get; set; }
            public string? UrlVisualizar { get; set; }
            public int Ordem { get; set; } = 0;
            public bool Ativo { get; set; } = true;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SalvarRelatorioCatalogo(RelatorioCatalogo model, bool embed = false, CancellationToken ct = default)
        {
            var (logadoId, deptLogado) = await GetUsuarioLogadoAsync(ct);
            if (logadoId <= 0) return RedirectToAction("Login", "Account");
            if (!IsDepartamentoTi(deptLogado)) return RedirectToAction("AcessoNegado", "Account");

            // Normaliza Key
            model.Key = (model.Key ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(model.Key))
                return BadRequest("Key é obrigatória.");

            // Se Id == 0: criar
            if (model.Id <= 0)
            {
                var jaExiste = await _context.RelatoriosCatalogo.AnyAsync(r => r.Key == model.Key, ct);
                if (jaExiste)
                {
                    TempData["ToastTipo"] = "danger";
                    TempData["ToastMsg"] = $"Já existe um relatório com a Key '{model.Key}'.";
                    return RedirectToAction(nameof(PermissoesRelatorios), new { embed = embed ? 1 : 0 });
                }

                var novo = new RelatorioCatalogo
                {
                    Key = model.Key,
                    Titulo = (model.Titulo ?? "").Trim(),
                    Descricao = (model.Descricao ?? "").Trim(),
                    Departamento = (model.Departamento ?? "").Trim(),
                    UrlVisualizar = (model.UrlVisualizar ?? "").Trim(),
                    Ordem = model.Ordem,
                    Ativo = true // criar sempre ativo
                };

                _context.RelatoriosCatalogo.Add(novo);
                await _context.SaveChangesAsync(ct);

                TempData["ToastTipo"] = "success";
                TempData["ToastMsg"] = "Relatório adicionado ao catálogo.";
                return RedirectToAction(nameof(PermissoesRelatorios), new { embed = embed ? 1 : 0 });
            }

            // Se Id > 0: atualizar
            var existente = await _context.RelatoriosCatalogo.FirstOrDefaultAsync(r => r.Id == model.Id, ct);
            if (existente == null) return NotFound();

            // Segurança: não permite mudar a Key de um registro existente via form (mantém a do banco)
            existente.Titulo = (model.Titulo ?? "").Trim();
            existente.Descricao = (model.Descricao ?? "").Trim();
            existente.Departamento = (model.Departamento ?? "").Trim();
            existente.UrlVisualizar = (model.UrlVisualizar ?? "").Trim();
            existente.Ordem = model.Ordem;

            // Mantém Ativo conforme banco (ou, se você quiser permitir reativar no futuro, você pode aceitar model.Ativo)
            // existente.Ativo = existente.Ativo;

            await _context.SaveChangesAsync(ct);

            TempData["ToastTipo"] = "success";
            TempData["ToastMsg"] = "Relatório atualizado no catálogo.";

            return RedirectToAction(nameof(PermissoesRelatorios), new { embed = embed ? 1 : 0 });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DesativarRelatorioCatalogo(int id, bool embed = false, CancellationToken ct = default)
        {
            var (logadoId, deptLogado) = await GetUsuarioLogadoAsync(ct);
            if (logadoId <= 0) return RedirectToAction("Login", "Account");
            if (!IsDepartamentoTi(deptLogado)) return RedirectToAction("AcessoNegado", "Account");

            var r = await _context.RelatoriosCatalogo.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r == null) return NotFound();

            r.Ativo = false;
            await _context.SaveChangesAsync(ct);

            TempData["ToastTipo"] = "warning";
            TempData["ToastMsg"] = "Relatório desativado. (Não foi removido do banco.)";

            return RedirectToAction(nameof(PermissoesRelatorios), new { embed = embed ? 1 : 0 });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleRelatorioCatalogo(int id, [FromForm] bool embed = false, CancellationToken ct = default)
        {
            var (uid, deptId) = await GetUsuarioLogadoAsync(ct);
            if (uid <= 0) return RedirectToAction("Login", "Account");

            if (deptId != 1 && deptId != 8) return RedirectToAction("AcessoNegado", "Account");

            var item = await _context.RelatoriosCatalogo.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (item == null) return NotFound();

            item.Ativo = !item.Ativo; // remover = desativar
            await _context.SaveChangesAsync(ct);

            TempData["ToastTipo"] = "success";
            TempData["ToastMsg"] = item.Ativo ? "Relatório reativado." : "Relatório desativado.";

            return RedirectToAction(nameof(PermissoesRelatorios), new { embed = embed ? 1 : 0 });
        }


        // =======================================================
        // CONFIGURAÇÕES > PERMISSÕES DE RELATÓRIOS
        // =======================================================
        [HttpGet]
        public async Task<IActionResult> PermissoesRelatorios(int? usuarioId, bool embed = false, CancellationToken ct = default)
        {
            var (logadoId, deptLogado) = await GetUsuarioLogadoAsync(ct);
            if (logadoId <= 0) return RedirectToAction("Login", "Account");

            if (!IsDepartamentoTi(deptLogado))
                return RedirectToAction("AcessoNegado", "Account");

            // ✅ Catálogo completo (ativos + inativos) para a aba Catálogo
            var catalogoDb = await _context.RelatoriosCatalogo
                .AsNoTracking()
                .OrderBy(r => r.Ordem)
                .ThenBy(r => r.Titulo)
                .Select(r => new RelatoriosPermissoesVm.CatalogoItemVm
                {
                    Id = r.Id,
                    Key = r.Key,
                    Titulo = r.Titulo,
                    Descricao = r.Descricao,
                    Departamento = r.Departamento,
                    UrlVisualizar = r.UrlVisualizar,
                    Ativo = r.Ativo,
                    Ordem = r.Ordem
                })
                .ToListAsync(ct);

            var catalogoCompleto = MesclarCatalogoComPadroes(catalogoDb);

            // ✅ Matriz usa SOMENTE ATIVOS
            var relatoriosAtivos = catalogoCompleto
                .Where(r => r.Ativo)
                .OrderBy(r => r.Ordem)
                .ThenBy(r => r.Titulo)
                .Select(r => new RelatoriosPermissoesVm.RelatorioDefVm
                {
                    Key = r.Key,
                    Titulo = r.Titulo,
                    Departamento = r.Departamento
                })
                .ToList();

            var vm = new RelatoriosPermissoesVm
            {
                UsuarioId = usuarioId,
                Departamentos = await _context.Departamentos.OrderBy(d => d.Nome).ToListAsync(ct),
                Usuarios = await _context.Usuarios.Where(u => u.Ativo).OrderBy(u => u.Nome).ToListAsync(ct),
                Relatorios = relatoriosAtivos,
                Catalogo = catalogoCompleto
            };

            // Permissões por departamento (db)
            var deptPerms = await _context.RelatorioPermissaoDepartamento
                .AsNoTracking()
                .ToListAsync(ct);

            foreach (var p in deptPerms)
            {
                var k = $"{p.DepartamentoId}|{(p.RelatorioKey ?? "").Trim().ToUpperInvariant()}";
                vm.DeptPerms[k] = p.PodeVer;
            }

            // ✅ Força TI a ver tudo (na tela sempre marcado)
            foreach (var d in vm.Departamentos)
            {
                if (!IsDepartamentoTi(d.Id)) continue;

                foreach (var r in vm.Relatorios)
                {
                    var k = $"{d.Id}|{(r.Key ?? "").Trim().ToUpperInvariant()}";
                    vm.DeptPerms[k] = true;
                }
            }

            // Overrides por usuário
            vm.UserOverrides = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);

            if (usuarioId.HasValue)
            {
                var ups = await _context.RelatorioPermissaoUsuario
                    .AsNoTracking()
                    .Where(x => x.UsuarioId == usuarioId.Value)
                    .ToListAsync(ct);

                foreach (var r in vm.Relatorios)
                {
                    var relKey = (r.Key ?? "").Trim().ToUpperInvariant();
                    var u = ups.FirstOrDefault(x => (x.RelatorioKey ?? "").Trim().ToUpperInvariant() == relKey);
                    vm.UserOverrides[r.Key] = u == null ? (bool?)null : u.PodeVer;
                }
            }
            else
            {
                foreach (var r in vm.Relatorios)
                    vm.UserOverrides[r.Key] = null;
            }

            // embed/ajax
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
            ViewData["Embed"] = embed || isAjax;

            return View("~/Views/Configuracoes/PermissoesRelatorios.cshtml", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SalvarPermissoesRelatoriosDept(
    [FromForm] List<DeptPermInput> perms,
    [FromForm] bool embed = false,
    CancellationToken ct = default)
        {
            if (!await GarantirAcessoTiAsync(ct))
                return RedirectToAction("AcessoNegado", "Account");

            foreach (var p in perms)
            {
                var relKey = (p.RelatorioKey ?? "").Trim().ToUpperInvariant();

                // ✅ TI (dept 1 ou 8) SEMPRE pode ver tudo: não deixa gravar false
                var podeVer = IsDepartamentoTi(p.DepartamentoId) ? true : p.PodeVer;

                var existe = await _context.RelatorioPermissaoDepartamento
                    .FirstOrDefaultAsync(x => x.DepartamentoId == p.DepartamentoId && x.RelatorioKey == relKey, ct);

                if (existe == null)
                {
                    _context.RelatorioPermissaoDepartamento.Add(new RelatorioPermissaoDepartamento
                    {
                        DepartamentoId = p.DepartamentoId,
                        RelatorioKey = relKey,
                        PodeVer = podeVer
                    });
                }
                else
                {
                    existe.PodeVer = podeVer;
                }
            }

            await _context.SaveChangesAsync(ct);

            TempData["Sucesso"] = "Permissões por departamento salvas com sucesso.";

            if (embed)
            {
                // No modo embed, devolve mensagem imediata para o JS mostrar toast
                return Ok(new { ok = true, message = "Permissões por departamento salvas com sucesso." });
            }

            return RedirectToAction(nameof(PermissoesRelatorios));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SalvarPermissoesRelatoriosUsuario(
    [FromForm] int usuarioId,
    [FromForm] List<UserPermInput> perms,
    [FromForm] bool embed = false,
    CancellationToken ct = default)
        {
            if (!await GarantirAcessoTiAsync(ct))
                return RedirectToAction("AcessoNegado", "Account");

            foreach (var p in perms)
            {
                var relKey = (p.RelatorioKey ?? "").Trim().ToUpperInvariant();

                // "" => remove override (volta padrão)
                if (string.IsNullOrWhiteSpace(p.Value))
                {
                    var del = await _context.RelatorioPermissaoUsuario
                        .FirstOrDefaultAsync(x => x.UsuarioId == usuarioId && x.RelatorioKey == relKey, ct);

                    if (del != null) _context.RelatorioPermissaoUsuario.Remove(del);
                    continue;
                }

                var podeVer = p.Value == "1";

                var existe = await _context.RelatorioPermissaoUsuario
                    .FirstOrDefaultAsync(x => x.UsuarioId == usuarioId && x.RelatorioKey == relKey, ct);

                if (existe == null)
                {
                    _context.RelatorioPermissaoUsuario.Add(new RelatorioPermissaoUsuario
                    {
                        UsuarioId = usuarioId,
                        RelatorioKey = relKey,
                        PodeVer = podeVer
                    });
                }
                else
                {
                    existe.PodeVer = podeVer;
                }
            }

            await _context.SaveChangesAsync(ct);

            TempData["ToastTipo"] = "success";
            TempData["ToastMsg"] = "Permissões por usuário salvas.";

            return RedirectToAction(nameof(PermissoesRelatorios), new { usuarioId, embed = embed ? 1 : 0 });
        }


        // ----------------------------
        // Inputs
        // ----------------------------
        public class DeptPermInput
        {
            public int DepartamentoId { get; set; }
            public string RelatorioKey { get; set; } = "";
            public bool PodeVer { get; set; }
        }

        public class UserPermInput
        {
            public string RelatorioKey { get; set; } = "";
            public string? Value { get; set; } // "", "1", "0"
        }

        // ----------------------------
        // Definição dos relatórios (keys)
        // IMPORTANTE: use as MESMAS keys que você valida no RelatoriosController.Index
        // ----------------------------
        private static List<RelatoriosPermissoesVm.RelatorioDefVm> GetRelatoriosDefinidos()
        {
            return new()
            {
                new() { Key="DADOS_PRODUTOS",         Titulo="Dados Produtos",                         Departamento="Produtos" },
                new() { Key="REPRESENTANTES",         Titulo="Representantes",                         Departamento="Comercial" },
                new() { Key="MUDANCA_CARTEIRA",       Titulo="Mudança de Carteira / Inativação de PN", Departamento="Comercial" },
                new() { Key="RUPTURAS_HISTORICO",     Titulo="Rupturas - Histórico",                   Departamento="Análises" },
                new() { Key="RUPTURAS_PREVISAO",      Titulo="Rupturas - Previsão",                    Departamento="Análises" },
                new() { Key="STATUS_INDICADOR",       Titulo="Status de Indicador",                    Departamento="Análises" },
                new() { Key="COLABORADORES",          Titulo="Colaboradores",                          Departamento="RH" },
                new() { Key="DASH_LIBERACAO_PEDIDOS", Titulo="Liberação de Pedidos",                    Departamento="Comercial" },
                new() { Key="DASH_SAVING_COMPRAS",    Titulo="Pedidos de Compras",                     Departamento="Comercial" },
                new() { Key="COMISSOES",              Titulo="Comissão de Vendas",                     Departamento="Comercial" },
                new() { Key="RELATORIO_TEMPO",         Titulo="Relatório de Tempo",                     Departamento="TI" },
                new() { Key="INDICADOR_TI",            Titulo="Indicador TI",                           Departamento="TI" },
                new() { Key="REDISTRIBUICAO_CARTEIRA", Titulo="Redistribuição de Carteira",             Departamento="Comercial" },
                new() { Key="REDISTRIBUICAO_CARTEIRA_APLICAR", Titulo="Redistribuição de Carteira - Aplicar", Departamento="Comercial" },
            };
        }

        private static List<RelatoriosPermissoesVm.CatalogoItemVm> MesclarCatalogoComPadroes(
            List<RelatoriosPermissoesVm.CatalogoItemVm> catalogoDb)
        {
            var result = catalogoDb
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .Select(x =>
                {
                    x.Key = x.Key.Trim().ToUpperInvariant();
                    return x;
                })
                .ToList();

            var keys = result
                .Select(x => x.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var ordem = result.Count == 0 ? 0 : result.Max(x => x.Ordem);

            foreach (var r in RelatoriosCatalogo.Todos)
            {
                var key = (r.Key ?? "").Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(key) || keys.Contains(key))
                    continue;

                result.Add(new RelatoriosPermissoesVm.CatalogoItemVm
                {
                    Id = 0,
                    Key = key,
                    Titulo = r.Titulo,
                    Descricao = r.Descricao,
                    Departamento = r.Departamento,
                    UrlVisualizar = r.UrlVisualizar,
                    Ativo = true,
                    Ordem = ++ordem
                });
            }

            return result
                .OrderByDescending(x => x.Ativo)
                .ThenBy(x => x.Ordem)
                .ThenBy(x => x.Titulo)
                .ToList();
        }

        // ==========================
        // CONFIGURAÇÕES (tela “shell”)
        // ==========================
        public IActionResult Index()
        {
            return View();
        }

        // ==========================
        // SERVIDOR EMAIL
        // ==========================
        [HttpGet]
        public IActionResult ServidorEmail()
        {
            var config = _context.SmtpConfiguracoes.FirstOrDefault();

            var model = new EmailSettingsViewModel
            {
                SmtpServer = config?.SmtpServer,
                SmtpPort = config?.SmtpPort ?? 587,
                SmtpUser = config?.SmtpUser,
                SmtpPass = config?.SmtpPass,
                FromEmail = config?.FromEmail
            };

            // Se você também quiser embutir o ServidorEmail via AJAX, mantenha como está (se já funciona no seu embed)
            return View("ServidorEmail", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ServidorEmail(EmailSettingsViewModel model)
        {
            if (!ModelState.IsValid)
                return View("ServidorEmail", model);

            var config = _context.SmtpConfiguracoes.FirstOrDefault();
            if (config == null)
            {
                config = new SmtpConfiguracao();
                _context.SmtpConfiguracoes.Add(config);
            }

            config.SmtpServer = model.SmtpServer;
            config.SmtpPort = model.SmtpPort;
            config.SmtpUser = model.SmtpUser;
            config.SmtpPass = model.SmtpPass;
            config.FromEmail = model.FromEmail;

            _context.SaveChanges();

            TempData["MensagemSucesso"] = "Configurações salvas com sucesso!";
            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> TestarEnvioEmail(string email, [FromServices] IEmailService emailService)
        {
            try
            {
                await emailService.EnviarEmailAsync(email, "Teste de Envio - Helpdesk",
                    "<p>Este é um teste de envio de e-mail do sistema Helpdesk.</p>");
                return Json(new { sucesso = true });
            }
            catch (Exception ex)
            {
                return Json(new { sucesso = false, mensagem = ex.Message });
            }
        }

        // ==========================
        // CATEGORIAS (seu código original)
        // ==========================
        public class CategoriaCompletaDTO
        {
            public string TipoNome { get; set; }
            public string CategoriaNome { get; set; }
            public string SubcategoriaNome { get; set; }
        }

        public class CategoriaEdicaoCompletaDTO
        {
            public int Id { get; set; }
            public string TipoNome { get; set; }
            public string CategoriaNome { get; set; }
            public string SubcategoriaNome { get; set; }
        }

        [HttpPost]
        public IActionResult InserirCompleto([FromBody] CategoriaCompletaDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.TipoNome) ||
                string.IsNullOrWhiteSpace(dto.CategoriaNome) ||
                string.IsNullOrWhiteSpace(dto.SubcategoriaNome))
            {
                return BadRequest("Campos obrigatórios não preenchidos.");
            }

            var tipo = _context.TipoChamado.FirstOrDefault(t => t.Nome == dto.TipoNome);
            if (tipo == null)
            {
                tipo = new TipoChamado { Nome = dto.TipoNome };
                _context.TipoChamado.Add(tipo);
                _context.SaveChanges();
            }

            var categoria = _context.CategoriaChamado
                .FirstOrDefault(c => c.Nome == dto.CategoriaNome && c.TipoChamadoId == tipo.Id);

            if (categoria == null)
            {
                categoria = new CategoriaChamado
                {
                    Nome = dto.CategoriaNome,
                    TipoChamadoId = tipo.Id
                };
                _context.CategoriaChamado.Add(categoria);
                _context.SaveChanges();
            }

            var subExistente = _context.SubcategoriaChamado
                .FirstOrDefault(s => s.Nome == dto.SubcategoriaNome && s.CategoriaId == categoria.Id);

            if (subExistente == null)
            {
                var sub = new SubcategoriaChamado
                {
                    Nome = dto.SubcategoriaNome,
                    CategoriaId = categoria.Id
                };
                _context.SubcategoriaChamado.Add(sub);
                _context.SaveChanges();
            }

            return Ok();
        }

        [HttpGet]
        public IActionResult EditarCategoria(int id)
        {
            var categoria = _context.CategoriaChamado
                .Include(c => c.TipoChamado)
                .FirstOrDefault(c => c.Id == id);

            if (categoria == null)
                return NotFound();

            ViewBag.Tipos = _context.TipoChamado.OrderBy(t => t.Nome).ToList();
            return View("EditarCategoria", categoria);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EditarCategoria(CategoriaChamado categoria)
        {
            if (ModelState.IsValid)
            {
                _context.CategoriaChamado.Update(categoria);
                _context.SaveChanges();
                return RedirectToAction("ListarCategorias");
            }

            ViewBag.Tipos = _context.TipoChamado.OrderBy(t => t.Nome).ToList();
            return View("EditarCategoria", categoria);
        }

        [HttpPost]
        public IActionResult SalvarEdicaoCategoria([FromBody] CategoriaEdicaoDTO dto)
        {
            var categoria = _context.CategoriaChamado.FirstOrDefault(c => c.Id == dto.Id);
            if (categoria == null)
                return NotFound();

            categoria.Nome = dto.CategoriaNome;

            var tipo = _context.TipoChamado.FirstOrDefault(t => t.Nome == dto.TipoNome);
            if (tipo == null)
            {
                tipo = new TipoChamado { Nome = dto.TipoNome };
                _context.TipoChamado.Add(tipo);
                _context.SaveChanges();
            }

            categoria.TipoChamadoId = tipo.Id;

            _context.CategoriaChamado.Update(categoria);
            _context.SaveChanges();

            return Ok();
        }

        [HttpGet]
        public IActionResult Categorias(string filtro)
        {
            var tipos = _context.TipoChamado
                .Include(t => t.Categorias)
                    .ThenInclude(c => c.Subcategorias)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filtro))
            {
                tipos = tipos.Where(t =>
                    t.Nome.Contains(filtro) ||
                    t.Categorias.Any(c => c.Nome.Contains(filtro) ||
                        c.Subcategorias.Any(s => s.Nome.Contains(filtro))));
            }

            var lista = tipos.OrderBy(t => t.Nome).ToList();

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest" && !string.IsNullOrWhiteSpace(filtro))
            {
                return PartialView("_GridCategorias", lista);
            }

            return View("ListarCategorias", lista);
        }

        [HttpPost]
        public IActionResult SalvarEdicaoSubcategoria([FromBody] SubcategoriaChamado model)
        {
            var sub = _context.SubcategoriaChamado.FirstOrDefault(s => s.Id == model.Id);
            if (sub == null) return NotFound();

            sub.Nome = model.Nome;
            _context.SaveChanges();
            return Ok();
        }

        public IActionResult SalvarEdicaoCompleta([FromBody] CategoriaEdicaoCompletaDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.TipoNome) ||
                string.IsNullOrWhiteSpace(dto.CategoriaNome) ||
                string.IsNullOrWhiteSpace(dto.SubcategoriaNome))
            {
                return BadRequest("Todos os campos são obrigatórios.");
            }

            var sub = _context.SubcategoriaChamado
                .Include(s => s.Categoria)
                    .ThenInclude(c => c.TipoChamado)
                .Include(s => s.Categoria.Subcategorias)
                .FirstOrDefault(s => s.Id == dto.Id);

            if (sub == null)
                return NotFound("Subcategoria não encontrada.");

            var categoria = sub.Categoria;

            var tipo = _context.TipoChamado.FirstOrDefault(t => t.Nome == dto.TipoNome);
            if (tipo == null)
            {
                tipo = new TipoChamado { Nome = dto.TipoNome };
                _context.TipoChamado.Add(tipo);
                _context.SaveChanges();
            }

            categoria.TipoChamadoId = tipo.Id;
            categoria.Nome = dto.CategoriaNome;
            sub.Nome = dto.SubcategoriaNome;

            _context.SaveChanges();
            return Ok();
        }

        [HttpGet]
        public IActionResult GridCategorias(string filtro)
        {
            var tipos = _context.TipoChamado
                .Include(t => t.Categorias)
                    .ThenInclude(c => c.Subcategorias)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filtro))
            {
                tipos = tipos.Where(t =>
                    t.Nome.Contains(filtro) ||
                    t.Categorias.Any(c => c.Nome.Contains(filtro) ||
                        c.Subcategorias.Any(s => s.Nome.Contains(filtro))));
            }

            var lista = tipos.OrderBy(t => t.Nome).ToList();

            // ⚠️ Mantenho como você deixou (mas note que isso “renderiza página completa” dentro de partial)
            return PartialView("ListarCategorias", lista);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AdicionarCategoria([FromBody] CategoriaViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.TipoNome) ||
                string.IsNullOrWhiteSpace(model.CategoriaNome) ||
                string.IsNullOrWhiteSpace(model.SubcategoriaNome))
            {
                return BadRequest("Todos os campos são obrigatórios.");
            }

            var tipo = _context.TipoChamado
                .FirstOrDefault(t => t.Nome.ToLower() == model.TipoNome.ToLower());

            if (tipo == null)
            {
                tipo = new TipoChamado { Nome = model.TipoNome };
                _context.TipoChamado.Add(tipo);
                _context.SaveChanges();
            }

            var categoria = _context.CategoriaChamado
                .FirstOrDefault(c =>
                    c.Nome.ToLower() == model.CategoriaNome.ToLower() &&
                    c.TipoChamadoId == tipo.Id);

            if (categoria == null)
            {
                categoria = new CategoriaChamado
                {
                    Nome = model.CategoriaNome,
                    TipoChamadoId = tipo.Id
                };
                _context.CategoriaChamado.Add(categoria);
                _context.SaveChanges();
            }

            var subcategoriaExiste = _context.SubcategoriaChamado
                .Any(s =>
                    s.Nome.ToLower() == model.SubcategoriaNome.ToLower() &&
                    s.CategoriaId == categoria.Id);

            if (subcategoriaExiste)
            {
                return Conflict("Subcategoria já existe para esta categoria.");
            }

            var subcategoria = new SubcategoriaChamado
            {
                Nome = model.SubcategoriaNome,
                CategoriaId = categoria.Id
            };
            _context.SubcategoriaChamado.Add(subcategoria);
            _context.SaveChanges();

            return Ok();
        }

        [HttpPost("Configuracoes/ExcluirCategoria/{id}")]
        [ValidateAntiForgeryToken]
        public IActionResult ExcluirCategoria(int id)
        {
            var categoria = _context.CategoriaChamado
                .Include(c => c.Subcategorias)
                .FirstOrDefault(c => c.Id == id);

            if (categoria == null)
                return NotFound("Categoria não encontrada.");

            var subIds = categoria.Subcategorias.Select(s => s.Id).ToList();
            var algumaEmUso = _context.Chamados.Any(c => subIds.Contains(c.SubcategoriaId ?? 0));

            if (algumaEmUso)
                return Conflict("Não é possível excluir. Há subcategorias desta categoria vinculadas a chamados.");

            _context.SubcategoriaChamado.RemoveRange(categoria.Subcategorias);
            _context.CategoriaChamado.Remove(categoria);
            _context.SaveChanges();

            return Ok();
        }

        [HttpPost("Configuracoes/ExcluirSubcategoria/{id}")]
        [ValidateAntiForgeryToken]
        public IActionResult ExcluirSubcategoria(int id)
        {
            var sub = _context.SubcategoriaChamado.FirstOrDefault(s => s.Id == id);
            if (sub == null)
                return NotFound("Subcategoria não encontrada.");

            var emUso = _context.Chamados.Any(c => c.SubcategoriaId == id);
            if (emUso)
                return Conflict("Não é possível excluir. A subcategoria está vinculada a chamados.");

            _context.SubcategoriaChamado.Remove(sub);
            _context.SaveChanges();

            return Ok();
        }

        private async Task<(int UsuarioId, int DepartamentoId)> GetUsuarioLogadoAsync(CancellationToken ct)
        {
            var logadoId = HttpContext.Session.GetInt32("UsuarioId");
            if (!logadoId.HasValue || logadoId.Value <= 0)
                return (0, 0);

            var deptId = await _context.Usuarios
                .Where(u => u.Id == logadoId.Value)
                .Select(u => (int?)u.DepartamentoId)
                .FirstOrDefaultAsync(ct);

            return (logadoId.Value, deptId ?? 0);
        }


        private static bool IsDepartamentoTi(int departamentoId)
        {
            // Regra que você definiu (mantive seu padrão): 1 e 8 são TI
            return departamentoId == 1 || departamentoId == 8;
        }

        private async Task<bool> GarantirAcessoTiAsync(CancellationToken ct)
        {
            var (_, deptId) = await GetUsuarioLogadoAsync(ct);
            return IsDepartamentoTi(deptId);
        }


        [HttpPost("Configuracoes/ExcluirTipo/{id}")]
        [ValidateAntiForgeryToken]
        public IActionResult ExcluirTipo(int id)
        {
            var tipo = _context.TipoChamado
                .Include(t => t.Categorias)
                .ThenInclude(c => c.Subcategorias)
                .FirstOrDefault(t => t.Id == id);

            if (tipo == null)
                return NotFound("Tipo de chamado não encontrado.");

            var subIds = tipo.Categorias.SelectMany(c => c.Subcategorias).Select(s => s.Id).ToList();
            var algumaEmUso = _context.Chamados.Any(c => subIds.Contains(c.SubcategoriaId ?? 0));

            if (algumaEmUso)
                return Conflict("Não é possível excluir. Há chamados vinculados a subcategorias deste tipo.");

            _context.SubcategoriaChamado.RemoveRange(tipo.Categorias.SelectMany(c => c.Subcategorias));
            _context.CategoriaChamado.RemoveRange(tipo.Categorias);
            _context.TipoChamado.Remove(tipo);
            _context.SaveChanges();

            return Ok();
        }
    }
}
