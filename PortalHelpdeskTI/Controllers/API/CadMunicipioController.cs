using Microsoft.AspNetCore.Mvc;
using PortalHelpdeskTI.Services.API;

namespace PortalHelpdeskTI.Controllers.Api
{
    [ApiController]
    [Route("api/cad/municipios")]
    public class CadMunicipioController : ControllerBase
    {
        private readonly APIServices _api;

        public CadMunicipioController(APIServices api)
        {
            _api = api;
        }

        [HttpGet]
        public async Task<IActionResult> Get(
            [FromQuery] string? uf,
            [FromQuery] string? q,
            [FromQuery] int? absId,
            [FromQuery] int top = 100,
            CancellationToken ct = default)
        {
            var rows = await _api.BuscarMunicipiosAsync(uf, q, absId, top, ct);
            return Ok(new { total = rows.Count, items = rows });
        }
    }
}
