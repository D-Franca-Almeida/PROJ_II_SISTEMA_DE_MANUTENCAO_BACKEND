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

        public List<SolicitacaoDto> Listar(string? status, string? matriculaSolicitante)
        {
            var consulta = _contexto.Solicitacoes.AsNoTracking()
                .Include(s => s.Equipamento).ThenInclude(e => e!.Estacao).AsQueryable();
            if (status is not null)
                consulta = consulta.Where(s => s.Status == status);
            if (matriculaSolicitante is not null)
                consulta = consulta.Where(s => s.MatriculaSolicitante == matriculaSolicitante);

            return consulta.OrderByDescending(s => s.DataAbertura).ThenByDescending(s => s.Id)
                .ToList()
                .Select(Mapear)
                .ToList();
        }

        public SolicitacaoDetalheDto? ObterDetalhe(int id)
        {
            var s = _contexto.Solicitacoes.AsNoTracking()
                .Include(x => x.Equipamento).ThenInclude(e => e!.Estacao)
                .FirstOrDefault(x => x.Id == id);
            if (s is null) return null;

            var historico = _contexto.HistoricoStatus.AsNoTracking()
                .Where(h => h.SolicitacaoId == id)
                .OrderBy(h => h.Data).ThenBy(h => h.Id)
                .Select(h => new HistoricoStatusDto(h.StatusAnterior, h.StatusNovo, h.Matricula, h.Data, h.Observacao))
                .ToList();

            var r = Mapear(s);
            return new SolicitacaoDetalheDto(r.Id, r.Status, r.Descricao, r.FotoUrl, r.DataAbertura,
                r.MatriculaSolicitante, r.Equipamento, r.Estacao, r.MotivoDevolucao,
                s.MatriculaTecnico, s.DataInicioManutencao, historico);
        }

        private static SolicitacaoDto Mapear(Solicitacao s) => new(
            s.Id, s.Status, s.Descricao, s.FotoUrl, s.DataAbertura, s.MatriculaSolicitante,
            s.Equipamento is null ? null : new EquipamentoDto(s.Equipamento.Id, s.Equipamento.EstacaoId, s.Equipamento.Descricao, s.Equipamento.Patrimonio),
            s.Equipamento?.Estacao is null ? null : new EstacaoDto(s.Equipamento.Estacao.Id, s.Equipamento.Estacao.Nome, s.Equipamento.Estacao.Linha),
            s.MotivoDevolucao);

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
