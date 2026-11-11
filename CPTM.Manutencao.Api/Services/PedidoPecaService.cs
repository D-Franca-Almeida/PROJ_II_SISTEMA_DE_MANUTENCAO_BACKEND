using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Services
{
    public interface IPedidoPecaService
    {
        // US-08: técnico dono pede peça para a OSM EM MANUTENCAO -> SOLICITADO
        PedidoPecaDto Criar(int ordemServicoId, CriarPedidoPecaRequest requisicao, string matriculaTecnico);

        // Técnico dono ou Admin
        List<PedidoPecaDto> ListarDaOrdem(int ordemServicoId, string matricula, string perfil);

        // Aba de logística (Admin). status: SOLICITADO | ENCAMINHADO | RECEBIDO | CANCELADO | PENDENTES | nulo (todos)
        List<PedidoPecaDto> Listar(string? status);

        // US-23 (Admin, pelo Almoxarifado): SOLICITADO -> ENCAMINHADO -> RECEBIDO
        PedidoPecaDto Encaminhar(int id, string? observacaoAlmoxarifado, string matriculaAdmin);
        PedidoPecaDto ConfirmarRecebimento(int id, string matriculaAdmin);

        // US-23: técnico dono cancela um pedido SOLICITADO ou ENCAMINHADO
        PedidoPecaDto Cancelar(int id, string? motivo, string matriculaTecnico);
    }

    public class PedidoPecaService : IPedidoPecaService
    {
        public const int TamanhoMaximoItem = 200;
        public const int TamanhoMaximoTexto = 500;

        private readonly IPedidoPecaRepository _repositorio;
        private readonly TimeProvider _relogio;

        public PedidoPecaService(IPedidoPecaRepository repositorio, TimeProvider? relogio = null)
        {
            _repositorio = repositorio;
            _relogio = relogio ?? TimeProvider.System;
        }

        public PedidoPecaDto Criar(int ordemServicoId, CriarPedidoPecaRequest requisicao, string matriculaTecnico)
        {
            var ordem = _repositorio.ObterOrdem(ordemServicoId)
                ?? throw new OrdemServicoNaoEncontradaException(ordemServicoId);
            GarantirDono(ordem, matriculaTecnico, "Somente o técnico responsável pela OSM pode pedir peças para ela.");

            var statusSolicitacao = ordem.Solicitacao!.Status;
            if (statusSolicitacao != StatusSolicitacao.EmManutencao)
                throw new RegraDeNegocioException(
                    $"Só é possível pedir peças para uma OSM em manutenção (status atual da solicitação: {statusSolicitacao}).");

            var item = Obrigatorio(requisicao?.Item, "O item (nome da peça) é obrigatório.", TamanhoMaximoItem, "O item");
            if (requisicao!.Quantidade is not > 0)
                throw new RegraDeNegocioException("A quantidade deve ser maior que zero.");
            var justificativa = Obrigatorio(requisicao.Justificativa, "A justificativa é obrigatória.", TamanhoMaximoTexto, "A justificativa");

            var pedido = new PedidoPeca
            {
                OrdemServicoId = ordem.Id,
                Item = item,
                Quantidade = requisicao.Quantidade.Value,
                Justificativa = justificativa,
                Status = StatusPedidoPeca.Solicitado,
                MatriculaSolicitante = ordem.MatriculaTecnico,
                DataPedido = Agora()
            };
            ordem.Pedidos.Add(pedido);
            // UPDATE ... WHERE Status = 'EM MANUTENCAO': se a OSM foi encerrada desde a leitura, nada é gravado (409)
            _repositorio.MarcarStatusDaSolicitacaoComoLido(ordem.Solicitacao);
            _repositorio.Salvar(pedido);
            return _repositorio.ObterDto(pedido.Id);
        }

        public List<PedidoPecaDto> ListarDaOrdem(int ordemServicoId, string matricula, string perfil)
        {
            var ordem = _repositorio.ObterOrdem(ordemServicoId)
                ?? throw new OrdemServicoNaoEncontradaException(ordemServicoId);
            if (perfil != "Administrativo")
                GarantirDono(ordem, matricula, "Somente o técnico responsável pela OSM pode ver os pedidos dela.");
            return _repositorio.ListarDaOrdem(ordemServicoId);
        }

        public List<PedidoPecaDto> Listar(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return _repositorio.Listar(null);

            var filtro = status.Trim().ToUpperInvariant();
            if (filtro == StatusPedidoPeca.Pendentes)
                return _repositorio.Listar(new[] { StatusPedidoPeca.Solicitado, StatusPedidoPeca.Encaminhado });
            if (!StatusPedidoPeca.Todos.Contains(filtro))
                throw new RegraDeNegocioException(
                    $"Status de pedido inválido: {status}. Use {string.Join(", ", StatusPedidoPeca.Todos)} ou {StatusPedidoPeca.Pendentes}.");
            return _repositorio.Listar(new[] { filtro });
        }

        public PedidoPecaDto Encaminhar(int id, string? observacaoAlmoxarifado, string matriculaAdmin)
        {
            var pedido = _repositorio.ObterPorId(id) ?? throw new PedidoPecaNaoEncontradoException(id);
            if (pedido.Status != StatusPedidoPeca.Solicitado)
                throw new RegraDeNegocioException(
                    $"Transição inválida: só é possível encaminhar ao almoxarifado um pedido SOLICITADO (status atual: {pedido.Status}).");
            var observacao = Obrigatorio(observacaoAlmoxarifado,
                "A observação do almoxarifado (nº de requisição/fornecedor) é obrigatória.", TamanhoMaximoTexto, "A observação do almoxarifado");

            pedido.Status = StatusPedidoPeca.Encaminhado;
            pedido.ObservacaoAlmoxarifado = observacao;
            pedido.MatriculaEncaminhamento = matriculaAdmin.Trim();
            pedido.DataEncaminhamento = Agora();
            _repositorio.Salvar(pedido);
            return _repositorio.ObterDto(id);
        }

        public PedidoPecaDto ConfirmarRecebimento(int id, string matriculaAdmin)
        {
            var pedido = _repositorio.ObterPorId(id) ?? throw new PedidoPecaNaoEncontradoException(id);
            if (pedido.Status != StatusPedidoPeca.Encaminhado)
                throw new RegraDeNegocioException(
                    $"Transição inválida: só é possível confirmar a chegada de um pedido ENCAMINHADO (status atual: {pedido.Status}).");

            pedido.Status = StatusPedidoPeca.Recebido;
            pedido.MatriculaRecebimento = matriculaAdmin.Trim();
            pedido.DataRecebimento = Agora();
            _repositorio.Salvar(pedido);
            return _repositorio.ObterDto(id);
        }

        public PedidoPecaDto Cancelar(int id, string? motivo, string matriculaTecnico)
        {
            var pedido = _repositorio.ObterPorId(id) ?? throw new PedidoPecaNaoEncontradoException(id);
            GarantirDono(pedido.OrdemServico!, matriculaTecnico, "Somente o técnico responsável pela OSM pode cancelar os pedidos dela.");
            if (!StatusPedidoPeca.Pendente(pedido.Status))
                throw new RegraDeNegocioException(
                    $"Transição inválida: só é possível cancelar um pedido SOLICITADO ou ENCAMINHADO (status atual: {pedido.Status}).");
            var texto = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
            if (texto is not null && texto.Length > TamanhoMaximoTexto)
                throw new RegraDeNegocioException($"O motivo deve ter no máximo {TamanhoMaximoTexto} caracteres.");

            pedido.Status = StatusPedidoPeca.Cancelado;
            pedido.MotivoCancelamento = texto;
            pedido.MatriculaCancelamento = pedido.OrdemServico!.MatriculaTecnico;
            pedido.DataCancelamento = Agora();
            _repositorio.Salvar(pedido);
            return _repositorio.ObterDto(id);
        }

        private DateTime Agora() => _relogio.GetUtcNow().UtcDateTime;

        private static void GarantirDono(OrdemServico ordem, string matricula, string mensagem)
        {
            if (!string.Equals(ordem.MatriculaTecnico, matricula?.Trim(), StringComparison.Ordinal))
                throw new UnauthorizedAccessException(mensagem);
        }

        private static string Obrigatorio(string? valor, string mensagemVazio, int tamanhoMaximo, string campo)
        {
            if (string.IsNullOrWhiteSpace(valor))
                throw new RegraDeNegocioException(mensagemVazio);
            var texto = valor.Trim();
            if (texto.Length > tamanhoMaximo)
                throw new RegraDeNegocioException($"{campo} deve ter no máximo {tamanhoMaximo} caracteres.");
            return texto;
        }
    }
}
