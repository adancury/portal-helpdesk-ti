using System.Collections.Generic;

namespace PortalHelpdeskTI.Services.Relatorios
{
    public class MudancaCarteiraResultado
    {
        public int Total { get; set; }
        public int Atualizados { get; set; }
        public List<string> Erros { get; set; } = new();
    }
}
