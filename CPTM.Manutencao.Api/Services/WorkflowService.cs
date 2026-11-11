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

    // Outra operação mudou o status da SSL entre a leitura e a gravação (ex.: dois técnicos assumindo ao mesmo tempo) -> 409
    public class ConflitoDeConcorrenciaException : Exception
    {
        public ConflitoDeConcorrenciaException(int id, Exception? interna = null)
            : base($"A solicitação {id} foi alterada por outra operação ao mesmo tempo. Atualize a lista e tente novamente.", interna) { }
    }

    public record RecebimentoResultado(int SolicitacaoId, string NumeroSsm, DateTime DataRecebimento);

    // 1. A Interface (O Contrato)
    public interface IWorkflowService
    {
        void AprovarTriagem(int id, string matriculaLogada);
        void DevolverTriagem(int id, string motivo, string matriculaLogada);
        void CancelarTriagem(int id, string matriculaLogada, string perfilLogado);
        // US-07: RECEBIDA -> EM MANUTENCAO (Técnico); abre a OSM na mesma gravação
        OrdemServico IniciarManutencao(int id, string matriculaTecnico);

        // US-22: APROVADA -> RECEBIDA (Admin), gera NumeroSsm SSM-AAAA-NNNNNN e DataRecebimento
        RecebimentoResultado ReceberNaOficina(int id, string matriculaAdmin);

        // US-16: ponto único de mudança de status. Grava exatamente 1 linha em HistoricoStatus.
        // Solicitação nova (Id = 0) registra a abertura com StatusAnterior nulo.
        // Transições automáticas usam HistoricoStatus.MatriculaSistema.
        void Transicionar(Solicitacao solicitacao, string novoStatus, string matricula, string? observacao = null);
    }

    // 2. A Implementação: motor de triagem (máquina de estados)
    public class WorkflowService : IWorkflowService
    {
        private readonly ISolicitacaoRepository _repositorio;
        private readonly TimeProvider _relogio;

        public WorkflowService(ISolicitacaoRepository repositorio, TimeProvider? relogio = null)
        {
            _repositorio = repositorio;
            _relogio = relogio ?? TimeProvider.System;
        }

        public void AprovarTriagem(int id, string matriculaLogada)
        {
            var solicitacao = ObterParaTriagem(id, "aprovar");

            Transicionar(solicitacao, StatusSolicitacao.Aprovada, matriculaLogada);
        }

        public void DevolverTriagem(int id, string motivo, string matriculaLogada)
        {
            var solicitacao = ObterParaTriagem(id, "devolver");

            if (string.IsNullOrWhiteSpace(motivo))
                throw new RegraDeNegocioException("O motivo da devolução é obrigatório.");

            solicitacao.MotivoDevolucao = motivo.Trim();
            Transicionar(solicitacao, StatusSolicitacao.Devolvida, matriculaLogada, solicitacao.MotivoDevolucao);
        }

        public void CancelarTriagem(int id, string matriculaLogada, string perfilLogado)
        {
            var solicitacao = _repositorio.ObterPorId(id)
                ?? throw new SolicitacaoNaoEncontradaException(id);

            // REGRA 1: O Usuário Comum SÓ pode cancelar a PRÓPRIA solicitação.
            // (O Administrativo pode cancelar a de qualquer um, por isso não cai neste IF)
            if (perfilLogado == "Comum" && solicitacao.MatriculaSolicitante != matriculaLogada)
                throw new UnauthorizedAccessException("Você só tem permissão para cancelar as suas próprias solicitações.");

            // REGRA 2: Bloquear cancelamento tardio (US-19: permitido até APROVADA, antes de o equipamento chegar à oficina)
            if (solicitacao.Status != StatusSolicitacao.Solicitada &&
                solicitacao.Status != StatusSolicitacao.Reenviada &&
                solicitacao.Status != StatusSolicitacao.Devolvida &&
                solicitacao.Status != StatusSolicitacao.Aprovada)
                throw new RegraDeNegocioException(
                    $"Não é possível cancelar uma solicitação com status {solicitacao.Status}.");

            Transicionar(solicitacao, StatusSolicitacao.Cancelada, matriculaLogada);
        }

        // US-07: o técnico assume uma SSL RECEBIDA na oficina. RECEBIDA -> EM MANUTENCAO + OSM (uma única gravação)
        public OrdemServico IniciarManutencao(int id, string matriculaTecnico)
        {
            var solicitacao = _repositorio.ObterPorId(id)
                ?? throw new SolicitacaoNaoEncontradaException(id);

            if (solicitacao.Status != StatusSolicitacao.Recebida)
                throw new RegraDeNegocioException(
                    $"Transição inválida: só é possível iniciar a manutenção de uma solicitação RECEBIDA na oficina (status atual: {solicitacao.Status}).");

            if (string.IsNullOrWhiteSpace(matriculaTecnico))
                throw new RegraDeNegocioException("A identificação do técnico responsável é obrigatória.");

            var tecnico = matriculaTecnico.Trim();
            var agora = _relogio.GetUtcNow().UtcDateTime;
            var ordem = new OrdemServico { MatriculaTecnico = tecnico, DataAbertura = agora };
            solicitacao.MatriculaTecnico = tecnico;
            solicitacao.DataInicioManutencao = agora;
            solicitacao.OrdemServico = ordem;
            try
            {
                Transicionar(solicitacao, StatusSolicitacao.EmManutencao, tecnico);
            }
            catch (DbUpdateException ex)
            {
                throw new ConflitoDeConcorrenciaException(id, ex);
            }
            return ordem;
        }

        public RecebimentoResultado ReceberNaOficina(int id, string matriculaAdmin)
        {
            lock (TravaSsm)
            {
                for (var tentativa = 1; ; tentativa++)
                {
                    var solicitacao = _repositorio.ObterPorId(id)
                        ?? throw new SolicitacaoNaoEncontradaException(id);

                    if (solicitacao.Status != StatusSolicitacao.Aprovada)
                        throw new RegraDeNegocioException(
                            $"Transição inválida: só é possível receber na oficina uma solicitação APROVADA (status atual: {solicitacao.Status}).");

                    var agora = _relogio.GetUtcNow().UtcDateTime;
                    solicitacao.NumeroSsm = ProximoNumeroSsm(agora.Year);
                    solicitacao.DataRecebimento = agora;
                    try
                    {
                        Transicionar(solicitacao, StatusSolicitacao.Recebida, matriculaAdmin, $"SSM {solicitacao.NumeroSsm} gerada.");
                        return new RecebimentoResultado(solicitacao.Id, solicitacao.NumeroSsm, agora);
                    }
                    catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
                    {
                        if (tentativa >= TentativasSsm)
                            throw new ConflitoDeConcorrenciaException(id, ex);
                    }
                }
            }
        }

        public const int TentativasSsm = 3;
        private static readonly object TravaSsm = new();

        private string ProximoNumeroSsm(int ano)
        {
            var prefixo = $"SSM-{ano}-";
            var ultimo = _repositorio.UltimoNumeroSsm(prefixo);
            var sequencial = ultimo is null ? 1 : int.Parse(ultimo[prefixo.Length..]) + 1;
            if (sequencial > 999999)
                throw new RegraDeNegocioException($"Limite anual de números SSM atingido para {ano}.");
            return $"{prefixo}{sequencial:D6}";
        }

        public void Transicionar(Solicitacao solicitacao, string novoStatus, string matricula, string? observacao = null)
        {
            if (string.IsNullOrWhiteSpace(novoStatus))
                throw new RegraDeNegocioException("O novo status é obrigatório.");
            if (string.IsNullOrWhiteSpace(matricula))
                throw new RegraDeNegocioException("A matrícula responsável pela transição é obrigatória.");
            if (solicitacao.Id != 0 && solicitacao.Status == novoStatus)
                throw new RegraDeNegocioException($"A solicitação já está com status {novoStatus}.");

            var historico = new HistoricoStatus
            {
                StatusAnterior = solicitacao.Id == 0 ? null : solicitacao.Status,
                StatusNovo = novoStatus,
                Matricula = matricula.Trim(),
                Data = _relogio.GetUtcNow().UtcDateTime,
                Observacao = observacao
            };

            solicitacao.Status = novoStatus;
            _repositorio.Salvar(solicitacao, historico);
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
