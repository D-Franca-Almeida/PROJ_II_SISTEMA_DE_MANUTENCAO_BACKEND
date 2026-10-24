using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CPTM.Manutencao.Api.Models;
using CPTM.Manutencao.Api.Services;

namespace CPTM.Manutencao.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SolicitacoesController : ControllerBase
    {
        private readonly IWorkflowService _workflowService;

        public SolicitacoesController(IWorkflowService workflowService)
        {
            _workflowService = workflowService;
        }

        // 🟢 APROVAR (Apenas Administrativo)
        [HttpPut("{id}/aprovar")]
        [Authorize(Policy = "RequerPerfilAdmin")]
        public IActionResult Aprovar(int id) =>
            Executar(() => _workflowService.AprovarTriagem(id),
                "Solicitação aprovada com sucesso e pronta para a oficina.");

        // 🔴 DEVOLVER (Apenas Administrativo)
        [HttpPut("{id}/devolver")]
        [Authorize(Policy = "RequerPerfilAdmin")]
        public IActionResult Devolver(int id, [FromBody] DevolucaoDto dto) =>
            Executar(() => _workflowService.DevolverTriagem(id, dto?.Motivo ?? string.Empty),
                "Solicitação devolvida para correção do solicitante.");

        // ⚫ CANCELAR (Administrativo: qualquer uma | Comum: apenas a própria | Técnico: proibido)
        [HttpPut("{id}/cancelar")]
        [Authorize]
        public IActionResult Cancelar(int id)
        {
            var matricula = User.FindFirst("Matricula")?.Value;
            var perfil = User.FindFirst("Perfil")?.Value;

            if (string.IsNullOrEmpty(matricula) || (perfil != "Administrativo" && perfil != "Comum"))
                return Forbid();

            return Executar(() => _workflowService.CancelarTriagem(id, matricula, perfil),
                "Solicitação cancelada.");
        }

        // 🔧 INICIAR MANUTENÇÃO (Apenas Técnico) - Sprint 3 / US-07
        [HttpPut("{id}/iniciar-manutencao")]
        [Authorize(Policy = "RequerPerfilTecnico")]
        public IActionResult IniciarManutencao(int id)
        {
            var matricula = User.FindFirst("Matricula")?.Value;
            if (string.IsNullOrEmpty(matricula))
                return Forbid();

            try
            {
                _workflowService.IniciarManutencao(id, matricula);
                return Ok(new
                {
                    mensagem = "Manutenção iniciada com sucesso. Ordem de Serviço (OSM) aberta.",
                    solicitacaoId = id,
                    tecnicoResponsavel = matricula,
                    novoStatus = StatusSolicitacao.EmManutencao
                });
            }
            catch (SolicitacaoNaoEncontradaException ex)
            {
                return NotFound(new { erro = ex.Message });
            }
            catch (RegraDeNegocioException ex)
            {
                return BadRequest(new { erro = ex.Message });
            }
        }

        // Traduz as exceções do serviço em respostas HTTP (o Controller não tem regra de negócio)
        private IActionResult Executar(Action acao, string mensagemSucesso)
        {
            try
            {
                acao();
                return Ok(new { message = mensagemSucesso });
            }
            catch (SolicitacaoNaoEncontradaException ex)
            {
                return NotFound(new { erro = ex.Message });
            }
            catch (RegraDeNegocioException ex)
            {
                return BadRequest(new { erro = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }
    }
}
