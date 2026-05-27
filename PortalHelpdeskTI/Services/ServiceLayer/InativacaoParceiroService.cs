using PortalHelpdeskTI.Services.SAP;
namespace PortalHelpdeskTI.Services.ServiceLayer
{
    public class InativacaoParceiroService
    {
        private readonly ServiceLayerClient _sl;

        public InativacaoParceiroService(ServiceLayerClient sl)
        {
            _sl = sl;
        }

        public async Task<(int inativados, List<string> erros)>
            InativarEmMassaAsync(List<string> cardCodes)
        {
            var erros = new List<string>();
            var inativados = 0;

            foreach (var cardCode in cardCodes)
            {
                try
                {
                    var body = new
                    {
                        Valid = "tNO",
                        Frozen = "tYES",
                        FrozenRemarks = "Inativado via Portal Helpdesk"
                    };

                    var response = await _sl.PatchAsync(
                        $"BusinessPartners('{cardCode}')",
                        body);

                    if (response.IsSuccessStatusCode)
                    {
                        inativados++;
                    }
                    else
                    {
                        var erro = await response.Content.ReadAsStringAsync();
                        erros.Add($"{cardCode}: {erro}");
                    }
                }
                catch (Exception ex)
                {
                    erros.Add($"{cardCode}: {ex.Message}");
                }
            }

            return (inativados, erros);
        }
        public async Task<(bool ok, string? erro)> InativarAsync(string cardCode)
        {
            if (string.IsNullOrWhiteSpace(cardCode))
                return (false, "CardCode vazio.");

            cardCode = cardCode.Trim();

            var body = new
            {
                Valid = "tNO",
                Frozen = "tYES",
                FrozenRemarks = "Inativado via Portal Helpdesk"
            };

            var response = await _sl.PatchAsync(
                $"BusinessPartners('{cardCode}')",
                body
            );

            if (response.IsSuccessStatusCode)
                return (true, null);

            var erro = await response.Content.ReadAsStringAsync();
            return (false, erro);
        }
    }
}
