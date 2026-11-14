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
        private readonly INotificacaoQueue _fila;

        public AdminExpiracaoController(IExpiracaoService expiracao, INotificacaoQueue fila)
        {
            _expiracao = expiracao;
            _fila = fila;
        }

        // Roda a mesma rotina do job diário na hora (US-20; usado para demonstrar na banca)
        [HttpPost("executar")]
        [Authorize(Policy = "RequerPerfilAdmin")]
        public IActionResult Executar()
        {
            var matricula = User.FindFirst("Matricula")?.Value;
            return Ok(_expiracao.Executar(matricula));
        }

        // Consulta os últimos eventos publicados na fila de mensageria (Login e Transições de Status)
        [HttpGet("/api/admin/eventos-fila")]
        [Authorize(Policy = "RequerPerfilAdmin")]
        public IActionResult ListarEventosFila([FromQuery] int limite = 50) =>
            Ok(_fila.ObterRecentes(limite));
    }
}
