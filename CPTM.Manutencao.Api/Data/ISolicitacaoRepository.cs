using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Data
{
    // Contrato de acesso a dados. Na Sprint 2, a implementação passa a usar EF Core + Oracle.
    public interface ISolicitacaoRepository
    {
        Solicitacao? ObterPorId(int id);
        void Salvar(Solicitacao solicitacao);
    }
}
