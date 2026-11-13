namespace CPTM.Manutencao.Api.Models
{
    // US-10: observação opcional do Admin ao definir o destino (ex.: justificar "Sem Reparo")
    public class DestinoDto
    {
        public string? Observacao { get; set; }
    }

    // US-11: CPF ou matrícula de quem retirou o equipamento (obrigatório)
    public class ProtocoloRetiradaDto
    {
        public string IdentificacaoRecebedor { get; set; } = string.Empty;
    }
}

