namespace CPTM.Manutencao.Api.Models
{
    // Entidade principal: Solicitação de Serviço Local (SSL)
    public class Solicitacao
    {
        public int Id { get; set; }
        public string Descricao { get; set; } = string.Empty;
        public string Status { get; set; } = StatusSolicitacao.Solicitada;
        public string? MotivoDevolucao { get; set; }
        public string MatriculaSolicitante { get; set; } = string.Empty;
        public string? MatriculaTecnico { get; set; }
        public DateTime? DataInicioManutencao { get; set; }
        public DateTime DataAbertura { get; set; }
        public string? FotoUrl { get; set; }
        public int? EquipamentoId { get; set; }
        public Equipamento? Equipamento { get; set; }
    }
}
