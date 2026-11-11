namespace CPTM.Manutencao.Api.Models
{
    // Ciclo do pedido de peça (US-23): SOLICITADO -> ENCAMINHADO -> RECEBIDO; CANCELADO a partir de SOLICITADO ou ENCAMINHADO
    public static class StatusPedidoPeca
    {
        public const string Solicitado = "SOLICITADO";
        public const string Encaminhado = "ENCAMINHADO";
        public const string Recebido = "RECEBIDO";
        public const string Cancelado = "CANCELADO";

        // Filtro da fila de logística que junta SOLICITADO e ENCAMINHADO
        public const string Pendentes = "PENDENTES";

        public static readonly string[] Todos = { Solicitado, Encaminhado, Recebido, Cancelado };

        public static bool Pendente(string status) => status == Solicitado || status == Encaminhado;
    }

    // Pedido de peça de uma OSM (US-08). O Almoxarifado/Compras é ator externo: o Admin registra ENCAMINHADO e RECEBIDO por ele.
    public class PedidoPeca
    {
        public int Id { get; set; }
        public int OrdemServicoId { get; set; }
        public OrdemServico? OrdemServico { get; set; }
        public string Item { get; set; } = string.Empty;
        public int Quantidade { get; set; }
        public string Justificativa { get; set; } = string.Empty;
        public string Status { get; set; } = StatusPedidoPeca.Solicitado;
        public string MatriculaSolicitante { get; set; } = string.Empty;
        public DateTime DataPedido { get; set; }
        public string? ObservacaoAlmoxarifado { get; set; }
        public string? MatriculaEncaminhamento { get; set; }
        public DateTime? DataEncaminhamento { get; set; }
        public string? MatriculaRecebimento { get; set; }
        public DateTime? DataRecebimento { get; set; }
        public string? MatriculaCancelamento { get; set; }
        public DateTime? DataCancelamento { get; set; }
        public string? MotivoCancelamento { get; set; }
    }

    public class CriarPedidoPecaRequest
    {
        public string? Item { get; set; }
        public int? Quantidade { get; set; }
        public string? Justificativa { get; set; }
    }

    public class EncaminharPedidoPecaRequest
    {
        public string? ObservacaoAlmoxarifado { get; set; }
    }

    public class CancelarPedidoPecaRequest
    {
        public string? Motivo { get; set; }
    }

    public record PedidoPecaDto(
        int Id,
        int OrdemServicoId,
        int SolicitacaoId,
        string? NumeroSsm,
        string StatusSolicitacao,
        string MatriculaTecnico,
        string Item,
        int Quantidade,
        string Justificativa,
        string Status,
        DateTime DataPedido,
        string? ObservacaoAlmoxarifado,
        string? MatriculaEncaminhamento,
        DateTime? DataEncaminhamento,
        string? MatriculaRecebimento,
        DateTime? DataRecebimento,
        string? MatriculaCancelamento,
        DateTime? DataCancelamento,
        string? MotivoCancelamento);

    public record PedidoPendenteDto(int Id, string Item, int Quantidade, string Status);
}
