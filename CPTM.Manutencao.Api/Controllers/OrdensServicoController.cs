using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using CPTM.Manutencao.Api.Services;

namespace CPTM.Manutencao.Api.Controllers
{
    [ApiController]
    [Route("api/ordens-servico")]
    public class OrdensServicoController : ControllerBase
    {
        private readonly IOrdemServicoRepository _ordens;
        private readonly IPedidoPecaService _pedidos;

        public OrdensServicoController(IOrdemServicoRepository ordens, IPedidoPecaService pedidos)
        {
            _ordens = ordens;
            _pedidos = pedidos;
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

        // PEDIR PEÇA (Apenas Técnico dono da OSM, OSM EM MANUTENCAO) - US-08
        [HttpPost("{id:int}/pecas")]
        [Authorize(Policy = "RequerPerfilTecnico")]
        public IActionResult PedirPeca(int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CriarPedidoPecaRequest? requisicao)
        {
            var matricula = User.FindFirst("Matricula")?.Value;
            if (string.IsNullOrEmpty(matricula))
                return Forbid();

            return PedidosPecaController.Traduzir(() =>
            {
                var criado = _pedidos.Criar(id, requisicao ?? new CriarPedidoPecaRequest(), matricula);
                return Created($"/api/pedidos-peca/{criado.Id}", criado);
            }, this);
        }

        // PEDIDOS DA OSM (Técnico dono ou Administrativo) - US-23
        [HttpGet("{id:int}/pecas")]
        [Authorize]
        public IActionResult PedidosDaOrdem(int id)
        {
            var matricula = User.FindFirst("Matricula")?.Value;
            var perfil = User.FindFirst("Perfil")?.Value;
            if (string.IsNullOrEmpty(matricula) || (perfil != "Tecnico" && perfil != "Administrativo"))
                return Forbid();

            return PedidosPecaController.Traduzir(() => Ok(_pedidos.ListarDaOrdem(id, matricula, perfil)), this);
        }
    }
}
