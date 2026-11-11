namespace CPTM.Manutencao.Api.Models
{
    // OSM (US-07): aberta quando o técnico assume uma SSL RECEBIDA. Encerrada na US-09 (laudo ou motivo da não execução).
    public class OrdemServico
    {
        public int Id { get; set; }
        public int SolicitacaoId { get; set; }
        public Solicitacao? Solicitacao { get; set; }
        public string MatriculaTecnico { get; set; } = string.Empty;
        public DateTime DataAbertura { get; set; }
        public DateTime? DataConclusao { get; set; }
        public string? Laudo { get; set; }
        public string? MotivoNaoExecucao { get; set; }
    }

    public record OrdemServicoDto(
        int Id,
        int SolicitacaoId,
        string? NumeroSsm,
        string StatusSolicitacao,
        string Descricao,
        EquipamentoDto? Equipamento,
        EstacaoDto? Estacao,
        string MatriculaTecnico,
        DateTime DataAbertura,
        DateTime? DataConclusao,
        string? Laudo,
        string? MotivoNaoExecucao);

    public class ConclusaoOsmDto
    {
        public string? Laudo { get; set; }
    }

    public class NaoExecucaoOsmDto
    {
        public string? Motivo { get; set; }
    }
}
