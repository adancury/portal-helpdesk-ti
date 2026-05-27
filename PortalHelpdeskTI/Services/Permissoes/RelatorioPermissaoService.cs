using Microsoft.EntityFrameworkCore;

namespace PortalHelpdeskTI.Services.Permissoes
{
    public interface IRelatorioPermissaoService
    {
        Task<bool> PodeVerAsync(int usuarioId, string relatorioKey, CancellationToken ct);
    }

    public class RelatorioPermissaoService : IRelatorioPermissaoService
    {
        private readonly AppDbContext _db;

        public RelatorioPermissaoService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<bool> PodeVerAsync(int usuarioId, string relatorioKey, CancellationToken ct)
        {
            relatorioKey = (relatorioKey ?? "").Trim().ToUpperInvariant();

            // 1) Pega Departamento do usuário (primeiro, para garantir TI sempre liberado)
            var deptId = await _db.Usuarios
                .Where(u => u.Id == usuarioId)
                .Select(u => (int?)u.DepartamentoId)
                .FirstOrDefaultAsync(ct);

            if (!deptId.HasValue)
                return false;

            // ✅ TI vê tudo SEMPRE (mesmo que exista override negando)
            if (deptId.Value == 1 || deptId.Value == 8)
                return true;

            // 2) Override por usuário
            var userPerm = await _db.RelatorioPermissaoUsuario
                .Where(x => x.UsuarioId == usuarioId && x.RelatorioKey == relatorioKey)
                .Select(x => (bool?)x.PodeVer)
                .FirstOrDefaultAsync(ct);

            if (userPerm.HasValue)
                return userPerm.Value;

            // 3) Permissão por departamento
            var deptPerm = await _db.RelatorioPermissaoDepartamento
                .Where(x => x.DepartamentoId == deptId.Value && x.RelatorioKey == relatorioKey)
                .Select(x => (bool?)x.PodeVer)
                .FirstOrDefaultAsync(ct);

            if (deptPerm.HasValue)
                return deptPerm.Value;

            // 4) Default: nega
            return false;
        }

    }
}
