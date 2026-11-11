using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Data
{
    public interface IPedidoPecaRepository
    {
        // OSM rastreada com a SSL carregada
        OrdemServico? ObterOrdem(int ordemServicoId);

        // Pedido rastreado com a OSM e a SSL carregadas
        PedidoPeca? ObterPorId(int id);

        // Pedidos de uma OSM, na ordem em que foram feitos (DataPedido asc, Id asc)
        List<PedidoPecaDto> ListarDaOrdem(int ordemServicoId);

        // Fila de logística (Admin): mais antigos primeiro; status nulo traz todos
        List<PedidoPecaDto> Listar(IReadOnlyCollection<string>? status);

        PedidoPecaDto ObterDto(int id);

        // Status do pedido é token de concorrência: se outra operação mudou o status desde a leitura,
        // lança ConflitoDeConcorrenciaException e nada é gravado.
        void Salvar(PedidoPeca pedido);

        // Inclui a SSL no próximo Salvar com UPDATE condicionado ao status lido (o pedido só grava se a OSM continua EM MANUTENCAO)
        void MarcarStatusDaSolicitacaoComoLido(Solicitacao solicitacao);
    }
}
