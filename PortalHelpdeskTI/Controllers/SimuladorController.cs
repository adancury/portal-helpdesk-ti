using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[AllowAnonymous]
[Route("Simulador")]
public class SimuladorController : Controller
{
    [HttpGet("Index")]
    [HttpGet("")]
    public IActionResult Index()
    {
        return View();
    }
}
