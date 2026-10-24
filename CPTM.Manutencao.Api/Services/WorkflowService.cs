using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CPTM.Manutencao.Api.Services
{
    // Solicitação inexistente -> o Controller responde 404
    public class SolicitacaoNaoEncontradaException : Exception
    {
        public SolicitacaoNaoEncontradaException(int id)
            : base($"Solicitação {id} não encontrada.") { }
    }

    // Violação de regra de negócio (transição inválida, motivo ausente) -> 400
    public class RegraDeNegocioException : Exception
    {
        public RegraDeNegocioException(string message) : base(message) { }
    }

    // 1. A Interface (O Contrato)
    public interface IWorkflowService
    {
        void AprovarTriagem(int id);
        void DevolverTriagem(int id, string motivo);
        void CancelarTriagem(int id, string matriculaLogada, string perfilLogado);
        void IniciarManutencao(int id, string matriculaTecnico);
    }

    // 2. A Implementação: motor de triagem (máquina de estados)
    public class WorkflowService : IWorkflowService
    {
        private readonly ISolicitacaoRepository _repositorio;

        public WorkflowService(ISolicitacaoRepository repositorio)
        {
            _repositorio = repositorio;
        }

        public void AprovarTriagem(int id)
        {
            var solicitacao = ObterParaTriagem(id, "aprovar");

            solicitacao.Status = StatusSolicitacao.Aprovada;
            _repositorio.Salvar(solicitacao);
        }

        public void DevolverTriagem(int id, string motivo)
        {
            var solicitacao = ObterParaTriagem(id, "devolver");

            if (string.IsNullOrWhiteSpace(motivo))
                throw new RegraDeNegocioException("O motivo da devolução é obrigatório.");

            solicitacao.Status = StatusSolicitacao.Devolvida;
            solicitacao.MotivoDevolucao = motivo.Trim();
            _repositorio.Salvar(solicitacao);
        }

        public void CancelarTriagem(int id, string matriculaLogada, string perfilLogado)
        {
            var solicitacao = _repositorio.ObterPorId(id)
                ?? throw new SolicitacaoNaoEncontradaException(id);

            // REGRA 1: O Usuário Comum SÓ pode cancelar a PRÓPRIA solicitação.
            // (O Administrativo pode cancelar a de qualquer um, por isso não cai neste IF)
            if (perfilLogado == "Comum" && solicitacao.MatriculaSolicitante != matriculaLogada)
                throw new UnauthorizedAccessException("Você só tem permissão para cancelar as suas próprias solicitações.");

            // REGRA 2: Bloquear cancelamento tardio (só na fase de triagem/correção)
            if (solicitacao.Status != StatusSolicitacao.Solicitada &&
                solicitacao.Status != StatusSolicitacao.Reenviada &&
                solicitacao.Status != StatusSolicitacao.Devolvida)
                throw new RegraDeNegocioException(
                    $"Não é possível cancelar uma solicitação com status {solicitacao.Status}.");

            solicitacao.Status = StatusSolicitacao.Cancelada;
            _repositorio.Salvar(solicitacao);
        }

        // Sprint 3 (US-07): Recepção na oficina. APROVADA -> EM MANUTENCAO
        public void IniciarManutencao(int id, string matriculaTecnico)
        {
            var solicitacao = _repositorio.ObterPorId(id)
                ?? throw new SolicitacaoNaoEncontradaException(id);

            if (solicitacao.Status != StatusSolicitacao.Aprovada)
                throw new RegraDeNegocioException(
                    $"Transição inválida: só é possível iniciar a manutenção de uma solicitação APROVADA (status atual: {solicitacao.Status}).");

            if (string.IsNullOrWhiteSpace(matriculaTecnico))
                throw new RegraDeNegocioException("A identificação do técnico responsável é obrigatória.");

            solicitacao.Status = StatusSolicitacao.EmManutencao;
            solicitacao.MatriculaTecnico = matriculaTecnico.Trim();
            solicitacao.DataInicioManutencao = DateTime.UtcNow;
            _repositorio.Salvar(solicitacao);
        }

        // Busca a solicitação e garante que ela está apta para triagem:
        // só SOLICITADA ou REENVIADA podem sofrer aprovação/devolução/cancelamento.
        private Solicitacao ObterParaTriagem(int id, string acao)
        {
            var solicitacao = _repositorio.ObterPorId(id)
                ?? throw new SolicitacaoNaoEncontradaException(id);

            if (solicitacao.Status != StatusSolicitacao.Solicitada &&
                solicitacao.Status != StatusSolicitacao.Reenviada)
                throw new RegraDeNegocioException(
                    $"Transição inválida: não é possível {acao} uma solicitação com status {solicitacao.Status}.");

            return solicitacao;
        }
    }
}
