using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Data
{
    // Contrato de acesso a dados (EF Core).
    public record CandidatoExpiracao(Solicitacao Solicitacao, DateTime DataReferencia);

    public interface ISolicitacaoRepository
    {
        Solicitacao? ObterPorId(int id);

        // Mais recentes primeiro (DataAbertura desc, Id desc); filtros opcionais por status e solicitante
        List<SolicitacaoDto> Listar(string? status, string? matriculaSolicitante);

        SolicitacaoDetalheDto? ObterDetalhe(int id);

        // Grava a solicitação e a linha de histórico da transição na mesma transação (US-16)
        // DEVOLVIDA e APROVADA com a data em que entraram no status atual (último histórico; DataAbertura se não houver)
        List<CandidatoExpiracao> ObterCandidatosExpiracao();

        void Salvar(Solicitacao solicitacao, HistoricoStatus historico);
    }
}
