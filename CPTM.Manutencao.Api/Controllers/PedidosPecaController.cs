using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using CPTM.Manutencao.Api.Models;
using CPTM.Manutencao.Api.Services;

namespace CPTM.Manutencao.Api.Controllers
{
    // Logística de peças (US-23). O Almoxarifado/Compras não tem login: o Administrativo registra o andamento por ele.
    [ApiController]
    [Route("api/pedidos-peca")]
    public class PedidosPecaController : ControllerBase
    {
        private readonly IPedidoPecaService _pedidos;

        public PedidosPecaController(IPedidoPecaService pedidos)
        {
            _pedidos = pedidos;
        }

        // ABA DE LOGÍSTICA (Apenas Administrativo): ?status=SOLICITADO|ENCAMINHADO|RECEBIDO|CANCELADO|PENDENTES
        [HttpGet]
        [Authorize(Policy = "RequerPerfilAdmin")]
        public IActionResult Listar([FromQuery] string? status) => Traduzir(() => Ok(_pedidos.Listar(status)), this);

        // ENCAMINHAR AO ALMOXARIFADO (Apenas Administrativo): SOLICITADO -> ENCAMINHADO
        [HttpPut("{id:int}/encaminhar")]
        [Authorize(Policy = "RequerPerfilAdmin")]
        public IActionResult Encaminhar(int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] EncaminharPedidoPecaRequest? requisicao)
        {
            var matricula = User.FindFirst("Matricula")?.Value;
            if (string.IsNullOrEmpty(matricula))
                return Forbid();

            return Traduzir(() => Ok(_pedidos.Encaminhar(id, requisicao?.ObservacaoAlmoxarifado, matricula)), this);
        }

        // CONFIRMAR CHEGADA DA PEÇA (Apenas Administrativo): ENCAMINHADO -> RECEBIDO
        [HttpPut("{id:int}/receber")]
        [Authorize(Policy = "RequerPerfilAdmin")]
        public IActionResult Receber(int id)
        {
            var matricula = User.FindFirst("Matricula")?.Value;
            if (string.IsNullOrEmpty(matricula))
                return Forbid();

            return Traduzir(() => Ok(_pedidos.ConfirmarRecebimento(id, matricula)), this);
        }

        // CANCELAR (Apenas Técnico dono da OSM): SOLICITADO/ENCAMINHADO -> CANCELADO
        [HttpPut("{id:int}/cancelar")]
        [Authorize(Policy = "RequerPerfilTecnico")]
        public IActionResult Cancelar(int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancelarPedidoPecaRequest? requisicao)
        {
            var matricula = User.FindFirst("Matricula")?.Value;
            if (string.IsNullOrEmpty(matricula))
                return Forbid();

            return Traduzir(() => Ok(_pedidos.Cancelar(id, requisicao?.Motivo, matricula)), this);
        }

        internal static IActionResult Traduzir(Func<IActionResult> acao, ControllerBase controller)
        {
            try
            {
                return acao();
            }
            catch (OrdemServicoNaoEncontradaException ex)
            {
                return controller.NotFound(new { erro = ex.Message });
            }
            catch (PedidoPecaNaoEncontradoException ex)
            {
                return controller.NotFound(new { erro = ex.Message });
            }
            catch (ConflitoDeConcorrenciaException ex)
            {
                return controller.Conflict(new { erro = ex.Message });
            }
            catch (RegraDeNegocioException ex)
            {
                return controller.BadRequest(new { erro = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return controller.Forbid();
            }
        }
    }
}
