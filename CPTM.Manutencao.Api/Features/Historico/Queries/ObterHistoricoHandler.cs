using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using CPTM.Manutencao.Api.Services;

namespace CPTM.Manutencao.Api.Features.Historico.Queries
{
    public interface IObterHistoricoHandler
    {
        List<HistoricoStatusDto> Executar(int solicitacaoId, string matricula, string perfil);
    }

    // US-26: linha do tempo da SSL para o dono, o Administrativo e o técnico da OSM.
    // Reaproveita a consulta do detalhe (US-18), que já monta o histórico em ordem cronológica sem documentos.
    public class ObterHistoricoHandler : IObterHistoricoHandler
    {
        private readonly ISolicitacaoRepository _repositorio;

        public ObterHistoricoHandler(ISolicitacaoRepository repositorio)
        {
            _repositorio = repositorio;
        }

        public List<HistoricoStatusDto> Executar(int solicitacaoId, string matricula, string perfil)
        {
            var detalhe = _repositorio.ObterDetalhe(solicitacaoId) ?? throw new SolicitacaoNaoEncontradaException(solicitacaoId);
            ValidarPermissao(detalhe, matricula, perfil);
            return detalhe.Historico;
        }

        private static void ValidarPermissao(SolicitacaoDetalheDto detalhe, string matricula, string perfil)
        {
            if (!PodeVer(detalhe, matricula, perfil))
                throw new UnauthorizedAccessException();
        }

        private static bool PodeVer(SolicitacaoDetalheDto detalhe, string matricula, string perfil) => perfil switch
        {
            "Administrativo" => true,
            "Tecnico" => detalhe.MatriculaTecnico == matricula,
            _ => detalhe.MatriculaSolicitante == matricula
        };
    }
}
