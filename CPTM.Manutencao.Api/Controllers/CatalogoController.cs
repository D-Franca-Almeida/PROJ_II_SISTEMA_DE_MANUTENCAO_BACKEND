using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CPTM.Manutencao.Api.Data;

namespace CPTM.Manutencao.Api.Controllers
{
    [ApiController]
    [Authorize]
    public class CatalogoController : ControllerBase
    {
        private readonly ICatalogoRepository _catalogo;

        public CatalogoController(ICatalogoRepository catalogo)
        {
            _catalogo = catalogo;
        }

        [HttpGet("api/estacoes")]
        public IActionResult ListarEstacoes() => Ok(_catalogo.ListarEstacoes());

        [HttpGet("api/equipamentos")]
        public IActionResult ListarEquipamentos([FromQuery] int? estacaoId)
        {
            if (estacaoId is null)
                return BadRequest(new { erro = "O parâmetro estacaoId é obrigatório." });

            if (!_catalogo.EstacaoExiste(estacaoId.Value))
                return NotFound(new { erro = $"Estação {estacaoId} não encontrada." });

            return Ok(_catalogo.ListarEquipamentos(estacaoId.Value));
        }
    }
}
