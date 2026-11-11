using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CPTM.Manutencao.Api.Data;

namespace CPTM.Manutencao.Api.Controllers
{
    [ApiController]
    [Route("api/ordens-servico")]
    public class OrdensServicoController : ControllerBase
    {
        private readonly IOrdemServicoRepository _ordens;

        public OrdensServicoController(IOrdemServicoRepository ordens)
        {
            _ordens = ordens;
        }

        // MINHAS OSMs (Apenas Técnico) - US-07
        [HttpGet("minhas")]
        [Authorize(Policy = "RequerPerfilTecnico")]
        public IActionResult Minhas()
        {
            var matricula = User.FindFirst("Matricula")?.Value;
            if (string.IsNullOrEmpty(matricula))
                return Forbid();

            return Ok(_ordens.ListarDoTecnico(matricula));
        }
    }
}
