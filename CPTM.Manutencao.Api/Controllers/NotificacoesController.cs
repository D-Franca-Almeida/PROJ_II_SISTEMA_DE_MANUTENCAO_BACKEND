using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CPTM.Manutencao.Api.Features.Notificacoes.Commands;
using CPTM.Manutencao.Api.Features.Notificacoes.Queries;

namespace CPTM.Manutencao.Api.Controllers
{
    // US-25: central de notificações in-app (qualquer perfil autenticado)
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class NotificacoesController : ControllerBase
    {
        [HttpGet]
        public IActionResult Listar([FromServices] IListarNotificacoesHandler handler, [FromQuery] bool naoLidas = false)
        {
            if (!UsuarioLogado(out var matricula, out var perfil))
                return Forbid();

            return Ok(handler.Executar(matricula, perfil, naoLidas));
        }

        [HttpPut("{id:int}/lida")]
        public IActionResult MarcarComoLida(int id, [FromServices] IMarcarNotificacaoComoLidaHandler handler)
        {
            if (!UsuarioLogado(out var matricula, out var perfil))
                return Forbid();

            try
            {
                return Ok(handler.Executar(id, matricula, perfil));
            }
            catch (NotificacaoNaoEncontradaException ex)
            {
                return NotFound(new { erro = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }

        private bool UsuarioLogado(out string matricula, out string perfil)
        {
            matricula = User.FindFirst("Matricula")?.Value ?? string.Empty;
            perfil = User.FindFirst("Perfil")?.Value ?? string.Empty;
            return matricula.Length > 0 && perfil.Length > 0;
        }
    }
}
