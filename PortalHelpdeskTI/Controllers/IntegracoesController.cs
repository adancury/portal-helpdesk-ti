using Microsoft.AspNetCore.Mvc;
using PortalHelpdeskTI.Services.Integracoes;

public class IntegracoesController : Controller
{
    private readonly WmsProcessadorFilaService _processador;

    public IntegracoesController(WmsProcessadorFilaService processador)
    {
        _processador = processador;
    }

    public IActionResult Index()
    {
        return View();
    }

    public async Task<IActionResult> ProcessarWms()
    {
        try
        {
            await _processador.ProcessarAsync();

            return Json(new
            {
                sucesso = true,
                mensagem = "Fila WMS processada com sucesso."
            });
        }
        catch (Exception ex)
        {
            return Json(new
            {
                sucesso = false,
                mensagem = ex.Message
            });
        }
    }
}