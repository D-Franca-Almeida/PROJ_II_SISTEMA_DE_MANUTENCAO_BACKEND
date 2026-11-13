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
        public string? NumeroSsm { get; set; }
        public DateTime? DataRecebimento { get; set; }

        // US-10 (destino) e US-11 (protocolo de retirada)
        public string? ObservacaoDestino { get; set; }
        public string? IdentificacaoRetirada { get; set; } // CPF ou matrícula de quem buscou
        public DateTime? DataRetirada { get; set; }
        public int? EquipamentoId { get; set; }
        public Equipamento? Equipamento { get; set; }
        public OrdemServico? OrdemServico { get; set; }
    }
}
