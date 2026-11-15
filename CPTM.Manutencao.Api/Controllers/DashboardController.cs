using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CPTM.Manutencao.Api.Features.Dashboard.Queries;

namespace CPTM.Manutencao.Api.Controllers
{
    // US-27: dashboard analítico (Apenas Administrativo)
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "RequerPerfilAdmin")]
    public class DashboardController : ControllerBase
    {
        [HttpGet]
        public IActionResult Obter([FromServices] IObterDashboardHandler handler) => Ok(handler.Executar());
    }
}
