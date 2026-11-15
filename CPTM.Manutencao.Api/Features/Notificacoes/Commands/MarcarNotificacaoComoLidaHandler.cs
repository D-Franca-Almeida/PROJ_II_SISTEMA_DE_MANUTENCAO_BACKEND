using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Features.Notificacoes.Commands
{
    public class NotificacaoNaoEncontradaException : Exception
    {
        public NotificacaoNaoEncontradaException(int id) : base($"Notificação {id} não encontrada.") { }
    }

    public interface IMarcarNotificacaoComoLidaHandler
    {
        NotificacaoDto Executar(int id, string matricula, string perfil);
    }

    // US-25: só o destinatário (a matrícula, ou quem tem o perfil de destino) marca; marcar de novo mantém a primeira leitura
    public class MarcarNotificacaoComoLidaHandler : IMarcarNotificacaoComoLidaHandler
    {
        private readonly INotificacaoRepository _repositorio;
        private readonly TimeProvider _relogio;

        public MarcarNotificacaoComoLidaHandler(INotificacaoRepository repositorio, TimeProvider? relogio = null)
        {
            _repositorio = repositorio;
            _relogio = relogio ?? TimeProvider.System;
        }

        public NotificacaoDto Executar(int id, string matricula, string perfil)
        {
            var notificacao = Carregar(id);
            ValidarDestinatario(notificacao, matricula, perfil);
            MarcarComoLida(notificacao);
            return NotificacaoDto.De(notificacao);
        }

        private Notificacao Carregar(int id) =>
            _repositorio.ObterPorId(id) ?? throw new NotificacaoNaoEncontradaException(id);

        private static void ValidarDestinatario(Notificacao notificacao, string matricula, string perfil)
        {
            if (!notificacao.EhDestinatario(matricula, perfil))
                throw new UnauthorizedAccessException("Somente o destinatário pode marcar a notificação como lida.");
        }

        private void MarcarComoLida(Notificacao notificacao)
        {
            if (notificacao.Lida)
                return;
            notificacao.Lida = true;
            notificacao.DataLeitura = _relogio.GetUtcNow().UtcDateTime;
            _repositorio.Salvar();
        }
    }
}
