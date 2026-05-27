using System.Collections.Generic;

namespace PortalHelpdeskTI.Services.Relatorios
{
    public class InativarParceirosResultado
    {
        public int Total { get; set; }
        public int Inativados { get; set; }
        public List<string> Erros { get; set; } = new();
    }
}
