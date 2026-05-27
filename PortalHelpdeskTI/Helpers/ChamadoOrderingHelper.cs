using System.Globalization;
using System.Text;
using PortalHelpdeskTI.Models;

namespace PortalHelpdeskTI.Helpers
{
    public static class ChamadoOrderingHelper
    {
        public static int PrioridadePeso(string? prioridade)
        {
            var valor = RemoverAcentos(prioridade ?? "").Trim().ToUpperInvariant();

            return valor switch
            {
                "URGENTE" => 5,
                "CRITICA" => 4,
                "ALTA" => 3,
                "MEDIA" => 2,
                "BAIXA" => 1,
                _ => 0
            };
        }

        public static IEnumerable<Chamado> OrdenarPorPrioridadeESla(IEnumerable<Chamado> chamados)
        {
            return chamados
                .OrderByDescending(c => PrioridadePeso(c.Prioridade))
                .ThenByDescending(c => c.PercentualProgressoSLA)
                .ThenByDescending(c => c.DataAbertura);
        }

        public static IEnumerable<GridTecnicoItemVM> OrdenarPorPrioridadeESla(IEnumerable<GridTecnicoItemVM> itens)
        {
            return itens
                .OrderByDescending(i => PrioridadePeso(i.Chamado.Prioridade))
                .ThenByDescending(i => i.SlaPercent)
                .ThenByDescending(i => i.Chamado.DataAbertura);
        }

        private static string RemoverAcentos(string texto)
        {
            var normalizado = texto.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalizado.Length);

            foreach (var c in normalizado)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
