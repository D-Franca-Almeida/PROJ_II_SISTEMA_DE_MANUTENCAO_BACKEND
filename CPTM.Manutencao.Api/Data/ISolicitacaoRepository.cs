using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Data
{
    // Contrato de acesso a dados (EF Core).
    public record CandidatoExpiracao(Solicitacao Solicitacao, DateTime DataReferencia);

    public interface ISolicitacaoRepository
    {
        Solicitacao? ObterPorId(int id);

        // Solicitação rastreada com a OSM e os pedidos de peça carregados, para gravar OSM + status no mesmo SaveChanges (US-09)
        Solicitacao? ObterComOrdemServico(int id);

        // Mais recentes primeiro (DataAbertura desc, Id desc); filtros opcionais por status e solicitante
        List<SolicitacaoDto> Listar(string? status, string? matriculaSolicitante);

        // Fila do técnico (US-07): RECEBIDAS, mais antigas na oficina primeiro (DataRecebimento asc, Id asc)
        List<SolicitacaoDto> ListarRecebidas();

        SolicitacaoDetalheDto? ObterDetalhe(int id);

        // Grava a solicitação e a linha de histórico da transição na mesma transação (US-16)
        // DEVOLVIDA e APROVADA com a data em que entraram no status atual (último histórico; DataAbertura se não houver)
        List<CandidatoExpiracao> ObterCandidatosExpiracao();

        // Maior NumeroSsm já gravado com o prefixo (ex.: "SSM-2026-"), ou null
        string? UltimoNumeroSsm(string prefixo);

        // Status é token de concorrência: se outra operação mudou o status entre a leitura e a gravação,
        // lança ConflitoDeConcorrenciaException e nada é gravado.
        void Salvar(Solicitacao solicitacao, HistoricoStatus historico);
    }
}
