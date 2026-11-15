using CPTM.Manutencao.Api.Data;

namespace CPTM.Manutencao.Api.Features.Notificacoes.Queries
{
    public interface IListarNotificacoesHandler
    {
        NotificacoesDoUsuarioDto Executar(string matricula, string perfil, bool apenasNaoLidas);
    }

    // US-25: as notificações da matrícula e as do perfil dela, mais novas primeiro, com o total de não lidas
    public class ListarNotificacoesHandler : IListarNotificacoesHandler
    {
        private readonly INotificacaoRepository _repositorio;

        public ListarNotificacoesHandler(INotificacaoRepository repositorio)
        {
            _repositorio = repositorio;
        }

        public NotificacoesDoUsuarioDto Executar(string matricula, string perfil, bool apenasNaoLidas)
        {
            var notificacoes = _repositorio.ListarDoUsuario(matricula, perfil, apenasNaoLidas).Select(NotificacaoDto.De).ToList();
            return new NotificacoesDoUsuarioDto(_repositorio.ContarNaoLidas(matricula, perfil), notificacoes);
        }
    }
}
