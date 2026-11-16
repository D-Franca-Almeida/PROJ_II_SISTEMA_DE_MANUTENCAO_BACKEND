using Microsoft.EntityFrameworkCore;
using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Data
{
    public class NotificacaoRepository : INotificacaoRepository
    {
        private readonly ManutencaoDbContext _contexto;

        public NotificacaoRepository(ManutencaoDbContext contexto)
        {
            _contexto = contexto;
        }

        public void Adicionar(Notificacao notificacao) => _contexto.Notificacoes.Add(notificacao);

        public List<Notificacao> ListarDoUsuario(string matricula, string perfil, bool apenasNaoLidas)
        {
            var consulta = DoUsuario(matricula, perfil);
            if (apenasNaoLidas)
                consulta = consulta.Where(n => !n.Lida);
            return consulta.OrderByDescending(n => n.Data).ThenByDescending(n => n.Id).ToList();
        }

        public int ContarNaoLidas(string matricula, string perfil) =>
            DoUsuario(matricula, perfil).Count(n => !n.Lida);

        public Notificacao? ObterPorId(int id) => _contexto.Notificacoes.Find(id);

        public void Salvar() => _contexto.SaveChanges();

        private IQueryable<Notificacao> DoUsuario(string matricula, string perfil) =>
            _contexto.Notificacoes.AsNoTracking()
                .Where(n => n.MatriculaDestino == matricula || (n.MatriculaDestino == null && n.PerfilDestino == perfil));
    }
}
