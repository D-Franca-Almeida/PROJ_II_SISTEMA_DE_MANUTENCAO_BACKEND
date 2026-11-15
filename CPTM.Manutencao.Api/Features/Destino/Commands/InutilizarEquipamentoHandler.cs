using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using CPTM.Manutencao.Api.Services;

namespace CPTM.Manutencao.Api.Features.Destino.Commands
{
    public interface IInutilizarEquipamentoHandler
    {
        void Executar(int id, string matriculaAdmin, string? observacao);
    }

    // US-10: SEM REPARO -> INUTILIZADO (descarte, estado final), só pelo Admin
    public class InutilizarEquipamentoHandler : IInutilizarEquipamentoHandler
    {
        public const string ObservacaoPadrao = "Equipamento descartado.";

        private readonly ISolicitacaoRepository _repositorio;
        private readonly IWorkflowService _workflow;

        public InutilizarEquipamentoHandler(ISolicitacaoRepository repositorio, IWorkflowService workflow)
        {
            _repositorio = repositorio;
            _workflow = workflow;
        }

        public void Executar(int id, string matriculaAdmin, string? observacao)
        {
            var solicitacao = Carregar(id);
            ValidarSemReparo(solicitacao);
            var texto = ObservacaoDestino.Normalizar(observacao);
            if (texto is not null)
                solicitacao.ObservacaoDestino = texto;
            _workflow.Transicionar(solicitacao, StatusSolicitacao.Inutilizado, matriculaAdmin, texto ?? ObservacaoPadrao);
        }

        private Solicitacao Carregar(int id) =>
            _repositorio.ObterPorId(id) ?? throw new SolicitacaoNaoEncontradaException(id);

        private static void ValidarSemReparo(Solicitacao solicitacao)
        {
            if (solicitacao.Status != StatusSolicitacao.SemReparo)
                throw new RegraDeNegocioException(
                    $"Transição inválida: só é possível inutilizar um equipamento SEM REPARO (status atual: {solicitacao.Status}).");
        }
    }
}
