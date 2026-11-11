namespace CPTM.Manutencao.Api.Models
{
    // US-16: uma linha por mudança de status de uma SSL (quem, quando, de qual status para qual)
    public class HistoricoStatus
    {
        public const string MatriculaSistema = "SISTEMA";

        public int Id { get; set; }
        public int SolicitacaoId { get; set; }
        public Solicitacao? Solicitacao { get; set; }
        public string? StatusAnterior { get; set; }
        public string StatusNovo { get; set; } = string.Empty;
        public string Matricula { get; set; } = string.Empty;
        public DateTime Data { get; set; }
        public string? Observacao { get; set; }
    }
}
