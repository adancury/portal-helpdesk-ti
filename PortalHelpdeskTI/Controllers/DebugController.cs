using Microsoft.AspNetCore.Mvc;
using PortalHelpdeskTI.Infrastructure; // para usar o IHanaDb

public class DebugController : Controller
{
    private readonly IHanaDb _hana;

    public DebugController(IHanaDb hana)
    {
        _hana = hana;
    }

    [HttpGet("/debug/hana")]
    public async Task<IActionResult> HanaPing()
    {
        try
        {
            var dt = await _hana.QueryToDataTableAsync("SELECT CURRENT_TIMESTAMP AS Now FROM DUMMY");
            var result = dt.Rows.Count > 0 ? dt.Rows[0]["Now"].ToString() : "Sem retorno";
            return Content($"HANA OK: {result}");
        }
        catch (Exception ex)
        {
            return Content($"ERRO ao conectar no HANA:\n{ex.Message}");
        }
    }
}
