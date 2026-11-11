using Microsoft.EntityFrameworkCore;
using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Data
{
    // Implementação EF Core do repositório (substitui o SolicitacaoRepositoryMock). Registrar como Scoped.
    public class SolicitacaoRepository : ISolicitacaoRepository
    {
        private readonly ManutencaoDbContext _contexto;

        public SolicitacaoRepository(ManutencaoDbContext contexto)
        {
            _contexto = contexto;
        }

        public Solicitacao? ObterPorId(int id) => _contexto.Solicitacoes.Find(id);

        public void Salvar(Solicitacao solicitacao, HistoricoStatus historico)
        {
            if (_contexto.Entry(solicitacao).State == EntityState.Detached)
            {
                if (solicitacao.Id == 0)
                    _contexto.Solicitacoes.Add(solicitacao);
                else
                    _contexto.Solicitacoes.Update(solicitacao);
            }

            historico.Solicitacao = solicitacao;
            _contexto.HistoricoStatus.Add(historico);

            _contexto.SaveChanges();
        }
    }
}
