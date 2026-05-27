using PortalHelpdeskTI.Models;
using System.Collections.Generic;

namespace PortalHelpdeskTI.ViewModels.Permissoes
{
    public class RelatoriosPermissoesVm
    {
        public List<Departamento> Departamentos { get; set; } = new();
        public List<Usuario> Usuarios { get; set; } = new();

        public int? UsuarioId { get; set; }

        // Matriz (somente ATIVOS)
        public List<RelatorioDefVm> Relatorios { get; set; } = new();

        public Dictionary<string, bool> DeptPerms { get; set; } = new();
        public Dictionary<string, bool?> UserOverrides { get; set; } = new();

        // ✅ Catálogo (ativos + inativos)
        public List<CatalogoItemVm> Catalogo { get; set; } = new();

        public class RelatorioDefVm
        {
            public string Key { get; set; } = "";
            public string Titulo { get; set; } = "";
            public string Departamento { get; set; } = "";
        }

        public class CatalogoItemVm
        {
            public int Id { get; set; }
            public string Key { get; set; } = "";
            public string Titulo { get; set; } = "";
            public string? Descricao { get; set; }
            public string? Departamento { get; set; }
            public string? UrlVisualizar { get; set; }
            public int Ordem { get; set; }
            public bool Ativo { get; set; }
        }
    }
}
