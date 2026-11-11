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
        private readonly ISolicitacaoService _solicitacaoService;

        public SolicitacoesController(IWorkflowService workflowService, ISolicitacaoService solicitacaoService)
        {
            _workflowService = workflowService;
            _solicitacaoService = solicitacaoService;
        }

        // FILA DE PEDIDOS (Apenas Administrativo) - US-04
        [HttpGet]
        [Authorize(Policy = "RequerPerfilAdmin")]
        public IActionResult Listar([FromQuery] string? status)
        {
            try
            {
                return Ok(_solicitacaoService.Listar(status));
            }
            catch (RegraDeNegocioException ex)
            {
                return BadRequest(new { erro = ex.Message });
            }
        }

        // FILA DO TÉCNICO (Apenas Técnico) - US-07: SSLs RECEBIDAS na oficina, disponíveis para assumir
        [HttpGet("recebidas")]
        [Authorize(Policy = "RequerPerfilTecnico")]
        public IActionResult Recebidas() => Ok(_solicitacaoService.ListarRecebidas());

        // MINHAS SSLs (qualquer perfil autenticado; apenas as do próprio solicitante) - US-18
        [HttpGet("minhas")]
        [Authorize]
        public IActionResult Minhas()
        {
            var matricula = User.FindFirst("Matricula")?.Value;
            if (string.IsNullOrEmpty(matricula))
                return Forbid();

            return Ok(_solicitacaoService.ListarDoSolicitante(matricula));
        }

        // DETALHE (Comum: apenas a própria | Administrativo e Técnico: qualquer uma) - US-18
        [HttpGet("{id:int}")]
        [Authorize]
        public IActionResult Obter(int id)
        {
            var matricula = User.FindFirst("Matricula")?.Value;
            var perfil = User.FindFirst("Perfil")?.Value;
            if (string.IsNullOrEmpty(matricula) || string.IsNullOrEmpty(perfil))
                return Forbid();

            try
            {
                return Ok(_solicitacaoService.ObterDetalhe(id, matricula, perfil));
            }
            catch (SolicitacaoNaoEncontradaException ex)
            {
                return NotFound(new { erro = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }

        // ABRIR SSL com foto (Apenas Comum) - US-03
        [HttpPost]
        [Authorize(Policy = "RequerPerfilComum")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(SolicitacaoService.TamanhoMaximoRequisicao)]
        [RequestFormLimits(MultipartBodyLengthLimit = SolicitacaoService.TamanhoMaximoRequisicao)]
        [LimiteCorpo(SolicitacaoService.TamanhoMaximoRequisicao, "A foto deve ter no máximo 5 MB.")]
        public async Task<IActionResult> Criar([FromForm] CriarSolicitacaoRequest requisicao)
        {
            var matricula = User.FindFirst("Matricula")?.Value;
            if (string.IsNullOrEmpty(matricula))
                return Forbid();

            try
            {
                var criada = await _solicitacaoService.CriarAsync(requisicao, matricula);
                return Created($"/api/Solicitacoes/{criada.Id}", criada);
            }
            catch (RegraDeNegocioException ex)
            {
                return BadRequest(new { erro = ex.Message });
            }
        }

        // REENVIAR SSL devolvida (Apenas Comum dono) - US-06
        [HttpPut("{id:int}/reenviar")]
        [Authorize(Policy = "RequerPerfilComum")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(SolicitacaoService.TamanhoMaximoRequisicao)]
        [RequestFormLimits(MultipartBodyLengthLimit = SolicitacaoService.TamanhoMaximoRequisicao)]
        [LimiteCorpo(SolicitacaoService.TamanhoMaximoRequisicao, "A foto deve ter no máximo 5 MB.")]
        public async Task<IActionResult> Reenviar(int id, [FromForm] ReenviarSolicitacaoRequest requisicao)
        {
            var matricula = User.FindFirst("Matricula")?.Value;
            if (string.IsNullOrEmpty(matricula))
                return Forbid();

            try
            {
                return Ok(await _solicitacaoService.ReenviarAsync(id, requisicao, matricula));
            }
            catch (SolicitacaoNaoEncontradaException ex)
            {
                return NotFound(new { erro = ex.Message });
            }
            catch (ConflitoDeConcorrenciaException ex)
            {
                return Conflict(new { erro = ex.Message });
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

        // 🟢 APROVAR (Apenas Administrativo)
        [HttpPut("{id}/aprovar")]
        [Authorize(Policy = "RequerPerfilAdmin")]
        public IActionResult Aprovar(int id)
        {
            var matricula = User.FindFirst("Matricula")?.Value;
            if (string.IsNullOrEmpty(matricula))
                return Forbid();

            return Executar(() => _workflowService.AprovarTriagem(id, matricula),
                "Solicitação aprovada com sucesso e pronta para a oficina.");
        }

        // 🔴 DEVOLVER (Apenas Administrativo)
        [HttpPut("{id}/devolver")]
        [Authorize(Policy = "RequerPerfilAdmin")]
        public IActionResult Devolver(int id, [FromBody] DevolucaoDto dto)
        {
            var matricula = User.FindFirst("Matricula")?.Value;
            if (string.IsNullOrEmpty(matricula))
                return Forbid();

            return Executar(() => _workflowService.DevolverTriagem(id, dto?.Motivo ?? string.Empty, matricula),
                "Solicitação devolvida para correção do solicitante.");
        }

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

        // 📦 RECEBER NA OFICINA (Apenas Administrativo) - US-22: APROVADA -> RECEBIDA e gera o número da SSM
        [HttpPut("{id:int}/receber")]
        [Authorize(Policy = "RequerPerfilAdmin")]
        public IActionResult Receber(int id)
        {
            var matricula = User.FindFirst("Matricula")?.Value;
            if (string.IsNullOrEmpty(matricula))
                return Forbid();

            try
            {
                var r = _workflowService.ReceberNaOficina(id, matricula);
                return Ok(new
                {
                    message = $"Equipamento recebido na oficina. SSM {r.NumeroSsm} gerada.",
                    solicitacaoId = r.SolicitacaoId,
                    numeroSsm = r.NumeroSsm,
                    dataRecebimento = r.DataRecebimento,
                    novoStatus = StatusSolicitacao.Recebida
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
            catch (ConflitoDeConcorrenciaException ex)
            {
                return Conflict(new { erro = ex.Message });
            }
        }

        // 🔧 INICIAR MANUTENÇÃO (Apenas Técnico) - US-07: assume uma SSL RECEBIDA e abre a OSM
        [HttpPut("{id}/iniciar-manutencao")]
        [Authorize(Policy = "RequerPerfilTecnico")]
        public IActionResult IniciarManutencao(int id)
        {
            var matricula = User.FindFirst("Matricula")?.Value;
            if (string.IsNullOrEmpty(matricula))
                return Forbid();

            try
            {
                var ordem = _workflowService.IniciarManutencao(id, matricula);
                return Ok(new
                {
                    mensagem = "Manutenção iniciada com sucesso. Ordem de Serviço (OSM) aberta.",
                    solicitacaoId = id,
                    ordemServicoId = ordem.Id,
                    numeroSsm = ordem.Solicitacao?.NumeroSsm,
                    tecnicoResponsavel = ordem.MatriculaTecnico,
                    dataAbertura = ordem.DataAbertura,
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
            catch (ConflitoDeConcorrenciaException ex)
            {
                return Conflict(new { erro = ex.Message });
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
            catch (ConflitoDeConcorrenciaException ex)
            {
                return Conflict(new { erro = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }
    }
}
