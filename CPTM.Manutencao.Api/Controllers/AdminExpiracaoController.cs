using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CPTM.Manutencao.Api.Services;

namespace CPTM.Manutencao.Api.Controllers
{
    [ApiController]
    [Route("api/admin/expiracao")]
    public class AdminExpiracaoController : ControllerBase
    {
        private readonly IExpiracaoService _expiracao;

        public AdminExpiracaoController(IExpiracaoService expiracao)
        {
            _expiracao = expiracao;
        }

        // Roda a mesma rotina do job diário na hora (US-20; usado para demonstrar na banca)
        [HttpPost("executar")]
        [Authorize(Policy = "RequerPerfilAdmin")]
        public IActionResult Executar()
        {
            var matricula = User.FindFirst("Matricula")?.Value;
            return Ok(_expiracao.Executar(matricula));
        }
    }
}
