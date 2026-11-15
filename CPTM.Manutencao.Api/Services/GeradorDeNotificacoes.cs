using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Services
{
    // US-25: cria as notificações de uma mudança sem gravá-las; o SaveChanges da própria mudança grava tudo junto
    public interface IGeradorDeNotificacoes
    {
        void GerarDaTransicao(Solicitacao solicitacao, HistoricoStatus historico);
        void GerarDoPedidoCriado(PedidoPeca pedido, OrdemServico ordem);
        void GerarDoPedidoRecebido(PedidoPeca pedido, OrdemServico ordem);
    }

    public class GeradorDeNotificacoes : IGeradorDeNotificacoes
    {
        private readonly INotificacaoRepository _repositorio;

        public GeradorDeNotificacoes(INotificacaoRepository repositorio)
        {
            _repositorio = repositorio;
        }

        public void GerarDaTransicao(Solicitacao solicitacao, HistoricoStatus historico)
        {
            if (solicitacao.Id == 0)
                return;

            if (solicitacao.MatriculaSolicitante != historico.Matricula)
                ParaMatricula(solicitacao.MatriculaSolicitante, solicitacao.Id,
                    TextosDeNotificacao.DoSolicitante(solicitacao, historico.StatusNovo), historico.Data);

            ParaPerfil(Notificacao.PerfilAdministrativo, solicitacao.Id,
                TextosDeNotificacao.DoAdministrativo(solicitacao, historico.StatusNovo), historico.Data);
        }

        public void GerarDoPedidoCriado(PedidoPeca pedido, OrdemServico ordem) =>
            ParaPerfil(Notificacao.PerfilAdministrativo, ordem.SolicitacaoId,
                TextosDeNotificacao.PedidoCriado(pedido, ordem), pedido.DataPedido);

        public void GerarDoPedidoRecebido(PedidoPeca pedido, OrdemServico ordem) =>
            ParaMatricula(ordem.MatriculaTecnico, ordem.SolicitacaoId,
                TextosDeNotificacao.PedidoRecebido(pedido, ordem), pedido.DataRecebimento ?? pedido.DataPedido);

        private void ParaMatricula(string matricula, int solicitacaoId, string? mensagem, DateTime data)
        {
            if (mensagem is not null)
                _repositorio.Adicionar(new Notificacao { MatriculaDestino = matricula, SolicitacaoId = solicitacaoId, Mensagem = mensagem, Data = data });
        }

        private void ParaPerfil(string perfil, int solicitacaoId, string? mensagem, DateTime data)
        {
            if (mensagem is not null)
                _repositorio.Adicionar(new Notificacao { PerfilDestino = perfil, SolicitacaoId = solicitacaoId, Mensagem = mensagem, Data = data });
        }
    }
}
