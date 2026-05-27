using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using PortalHelpdeskTI.Models;
using PortalHelpdeskTI.Services;

public class UsuariosController : Controller
{
    private readonly AppDbContext _context;

    public UsuariosController(AppDbContext context)
    {
        _context = context;
    }

    public IActionResult ListarUsuario(string? filtro)
    {
        var usuarios = _context.Usuarios
            .Include(u => u.Departamento)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filtro))
        {
            var termo = filtro.Trim();
            usuarios = usuarios.Where(u => u.Nome.Contains(termo) || u.Email.Contains(termo));
        }

        return View(usuarios.OrderBy(u => u.Nome).ToList());
    }

    public IActionResult Editar(int id)
    {
        var usuario = _context.Usuarios.Find(id);
        if (usuario == null)
            return NotFound();

        return View(usuario);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Editar(Usuario usuario)
    {
        if (!ModelState.IsValid)
            return View(usuario);

        try
        {
            _context.Entry(usuario).State = EntityState.Modified;
            _context.SaveChanges();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!_context.Usuarios.Any(e => e.Id == usuario.Id))
                return NotFound();

            throw;
        }

        return RedirectToAction(nameof(ListarUsuario));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SalvarEdicaoInline([FromBody] InlineUsuarioDto dto)
    {
        if (dto == null)
            return BadRequest("Dados inválidos.");

        var usuario = _context.Usuarios.Find(dto.Id);
        if (usuario == null)
            return NotFound("Usuário não encontrado.");

        if (string.IsNullOrWhiteSpace(dto.Nome) ||
            string.IsNullOrWhiteSpace(dto.Email) ||
            string.IsNullOrWhiteSpace(dto.Perfil) ||
            dto.DepartamentoId <= 0)
            return BadRequest("Preencha nome, e-mail, perfil e departamento.");

        var email = dto.Email.Trim();
        var emailEmUso = _context.Usuarios.Any(u => u.Id != dto.Id && u.Email == email);
        if (emailEmUso)
            return Conflict("Já existe outro usuário com este e-mail.");

        usuario.Nome = dto.Nome.Trim();
        usuario.Email = email;
        usuario.Perfil = dto.Perfil.Trim();
        usuario.DepartamentoId = dto.DepartamentoId;
        usuario.Ramal = dto.Ramal?.Trim();

        if (!string.IsNullOrWhiteSpace(dto.Senha))
            usuario.SenhaHash = Senhas.GerarHash(dto.Senha);

        _context.SaveChanges();
        return Ok();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SalvarNovoInline([FromBody] InlineUsuarioDto dto)
    {
        if (dto == null)
            return BadRequest("Dados inválidos.");

        if (string.IsNullOrWhiteSpace(dto.Nome) ||
            string.IsNullOrWhiteSpace(dto.Email) ||
            string.IsNullOrWhiteSpace(dto.Perfil) ||
            string.IsNullOrWhiteSpace(dto.Senha) ||
            dto.DepartamentoId <= 0)
            return BadRequest("Preencha nome, e-mail, perfil, departamento e senha.");

        var email = dto.Email.Trim();
        var emailEmUso = _context.Usuarios.Any(u => u.Email == email);
        if (emailEmUso)
            return Conflict("Já existe um usuário com este e-mail.");

        var usuario = new Usuario
        {
            Nome = dto.Nome.Trim(),
            Email = email,
            Perfil = dto.Perfil.Trim(),
            DepartamentoId = dto.DepartamentoId,
            SenhaHash = Senhas.GerarHash(dto.Senha),
            Ativo = true,
            Ramal = dto.Ramal?.Trim()
        };

        _context.Usuarios.Add(usuario);
        _context.SaveChanges();

        TempData["MensagemSucesso"] = "Usuário cadastrado com sucesso!";
        return Ok();
    }

    public IActionResult ObterUsuariosDestino(int idExclusao)
    {
        var usuarios = _context.Usuarios
            .Where(u => u.Id != idExclusao)
            .OrderBy(u => u.Nome)
            .Select(u => new { id = u.Id, nome = u.Nome, email = u.Email })
            .ToList();

        return Json(usuarios);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ExcluirRemanejar([FromBody] ExcluirRemanejarDto dto)
    {
        if (dto == null || dto.IdExclusao == 0 || dto.IdDestino == 0)
            return BadRequest("Dados inválidos.");

        var usuario = _context.Usuarios.Find(dto.IdExclusao);
        var usuarioDestino = _context.Usuarios.Find(dto.IdDestino);

        if (usuario == null || usuarioDestino == null)
            return BadRequest("Usuário não encontrado.");

        var chamados = _context.Chamados.Where(c => c.UsuarioId == dto.IdExclusao).ToList();
        foreach (var chamado in chamados)
        {
            chamado.UsuarioId = usuarioDestino.Id;
        }

        _context.Usuarios.Remove(usuario);
        _context.SaveChanges();

        TempData["MensagemSucesso"] = "Usuário excluído e chamados remanejados com sucesso.";
        return Ok();
    }

    [HttpGet]
    public IActionResult ObterDepartamentos()
    {
        var departamentos = _context.Departamentos
            .OrderBy(d => d.Nome)
            .Select(d => new
            {
                id = d.Id,
                descricao = d.Nome
            })
            .ToList();

        return Json(departamentos);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportarUsuarios(IFormFile arquivoExcel)
    {
        if (arquivoExcel == null || arquivoExcel.Length == 0)
            return BadRequest("Arquivo inválido.");

        using var stream = new MemoryStream();
        await arquivoExcel.CopyToAsync(stream);

        using var package = new ExcelPackage(stream);
        var worksheet = package.Workbook.Worksheets[0];

        var usuarios = new List<Usuario>();

        for (int row = 2; row <= worksheet.Dimension.End.Row; row++)
        {
            var nome = worksheet.Cells[row, 1].Text.Trim();
            var email = worksheet.Cells[row, 2].Text.Trim();
            var perfil = worksheet.Cells[row, 3].Text.Trim();
            var departamentoNome = worksheet.Cells[row, 4].Text.Trim();
            var ramal = worksheet.Cells[row, 5].Text.Trim();
            var senha = worksheet.Cells[row, 6].Text.Trim();

            if (string.IsNullOrWhiteSpace(nome) ||
                string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(senha))
                continue;

            if (_context.Usuarios.Any(u => u.Email == email) || usuarios.Any(u => u.Email == email))
                continue;

            var departamento = _context.Departamentos.FirstOrDefault(d => d.Nome == departamentoNome);
            if (departamento == null)
            {
                departamento = new Departamento { Nome = departamentoNome };
                _context.Departamentos.Add(departamento);
                await _context.SaveChangesAsync();
            }

            usuarios.Add(new Usuario
            {
                Nome = nome,
                Email = email,
                Perfil = string.IsNullOrWhiteSpace(perfil) ? "Usuario" : perfil,
                DepartamentoId = departamento.Id,
                Ramal = ramal,
                Ativo = true,
                TokenExpiraEm = null,
                TokenRedefinicaoSenha = null,
                SenhaHash = Senhas.GerarHash(senha)
            });
        }

        _context.Usuarios.AddRange(usuarios);
        await _context.SaveChangesAsync();

        TempData["Mensagem"] = $"{usuarios.Count} usuário(s) importado(s) com sucesso.";
        TempData["ProximaTela"] = "/Usuarios/ListarUsuario";

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            return Ok(new { mensagem = TempData["Mensagem"] });

        return RedirectToAction("Index", "Configuracoes");
    }
}
