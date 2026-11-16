using Microsoft.EntityFrameworkCore;

namespace CPTM.Manutencao.Api.Data
{
    public class DashboardRepository : IDashboardRepository
    {
        private readonly ManutencaoDbContext _contexto;

        public DashboardRepository(ManutencaoDbContext contexto)
        {
            _contexto = contexto;
        }

        public Dictionary<string, int> ContarPorStatus() =>
            _contexto.Solicitacoes.AsNoTracking()
                .GroupBy(s => s.Status)
                .Select(g => new { Status = g.Key, Quantidade = g.Count() })
                .ToDictionary(x => x.Status, x => x.Quantidade);

        public List<double> DuracoesEmHorasEntre(string de, string para) =>
            PrimeiraEntradaPorSolicitacao(de)
                .Join(PrimeiraEntradaPorSolicitacao(para), a => a.SolicitacaoId, b => b.SolicitacaoId, (a, b) => new { Inicio = a.Data, Fim = b.Data })
                .Where(x => x.Fim >= x.Inicio)
                .ToList()
                .Select(x => (x.Fim - x.Inicio).TotalHours)
                .ToList();

        private IQueryable<EntradaNoStatus> PrimeiraEntradaPorSolicitacao(string status) =>
            _contexto.HistoricoStatus.AsNoTracking()
                .Where(h => h.StatusNovo == status)
                .GroupBy(h => h.SolicitacaoId)
                .Select(g => new EntradaNoStatus { SolicitacaoId = g.Key, Data = g.Min(h => h.Data) });

        private class EntradaNoStatus
        {
            public int SolicitacaoId { get; set; }
            public DateTime Data { get; set; }
        }
    }
}
