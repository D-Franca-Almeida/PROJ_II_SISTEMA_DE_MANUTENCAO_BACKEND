using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Data
{
    // Contrato de acesso a dados (EF Core).
    public interface ISolicitacaoRepository
    {
        Solicitacao? ObterPorId(int id);

        // Grava a solicitação e a linha de histórico da transição na mesma transação (US-16)
        void Salvar(Solicitacao solicitacao, HistoricoStatus historico);
    }
}
