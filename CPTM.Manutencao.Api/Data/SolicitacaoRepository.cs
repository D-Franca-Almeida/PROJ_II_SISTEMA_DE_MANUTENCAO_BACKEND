using Microsoft.EntityFrameworkCore;
using CPTM.Manutencao.Api.Models;
using CPTM.Manutencao.Api.Services;

namespace CPTM.Manutencao.Api.Data
{
    // Implementação EF Core do repositório (substitui o SolicitacaoRepositoryMock). Registrar como Scoped.
    public class SolicitacaoRepository : ISolicitacaoRepository
    {
        private readonly ManutencaoDbContext _contexto;
        private readonly TimeProvider _relogio;
        private readonly PrazosOptions _prazos;

        public SolicitacaoRepository(ManutencaoDbContext contexto, TimeProvider? relogio = null, PrazosOptions? prazos = null)
        {
            _contexto = contexto;
            _relogio = relogio ?? TimeProvider.System;
            _prazos = prazos ?? new PrazosOptions();
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

            var solicitacoes = consulta.OrderByDescending(s => s.DataAbertura).ThenByDescending(s => s.Id).ToList();
            var referencias = DatasDeEntradaNoStatus(solicitacoes);
            return solicitacoes.Select(s => Mapear(s, DiasParaExpirar(s, referencias))).ToList();
        }

        public List<SolicitacaoDto> ListarRecebidas() =>
            _contexto.Solicitacoes.AsNoTracking()
                .Include(s => s.Equipamento).ThenInclude(e => e!.Estacao)
                .Where(s => s.Status == StatusSolicitacao.Recebida)
                .OrderBy(s => s.DataRecebimento).ThenBy(s => s.Id)
                .AsEnumerable()
                .Select(s => Mapear(s, null))
                .ToList();

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

            var ordemServicoId = _contexto.OrdensServico.AsNoTracking()
                .Where(o => o.SolicitacaoId == id).Select(o => (int?)o.Id).FirstOrDefault();

            var r = Mapear(s, DiasParaExpirar(s, DatasDeEntradaNoStatus(new[] { s })));
            return new SolicitacaoDetalheDto(r.Id, r.Status, r.Descricao, r.FotoUrl, r.DataAbertura,
                r.MatriculaSolicitante, r.Equipamento, r.Estacao, r.MotivoDevolucao,
                s.MatriculaTecnico, s.DataInicioManutencao, historico, r.DiasParaExpirar, s.NumeroSsm, s.DataRecebimento,
                ordemServicoId);
        }

        public List<CandidatoExpiracao> ObterCandidatosExpiracao()
        {
            var candidatas = _contexto.Solicitacoes
                .Where(s => s.Status == StatusSolicitacao.Devolvida || s.Status == StatusSolicitacao.Aprovada)
                .OrderBy(s => s.Id)
                .ToList();
            var referencias = DatasDeEntradaNoStatus(candidatas);
            return candidatas.Select(s => new CandidatoExpiracao(s, referencias[s.Id])).ToList();
        }

        private Dictionary<int, DateTime> DatasDeEntradaNoStatus(IEnumerable<Solicitacao> solicitacoes)
        {
            var alvo = solicitacoes.Where(s => PrazoExpiracao.Expira(s.Status)).ToList();
            var resultado = alvo.ToDictionary(s => s.Id, s => s.DataAbertura);
            if (alvo.Count == 0) return resultado;

            var ids = alvo.Select(s => s.Id).ToList();
            var historico = _contexto.HistoricoStatus.AsNoTracking()
                .Where(h => ids.Contains(h.SolicitacaoId))
                .Select(h => new { h.SolicitacaoId, h.StatusNovo, h.Data })
                .ToList();
            foreach (var s in alvo)
            {
                var datas = historico.Where(h => h.SolicitacaoId == s.Id && h.StatusNovo == s.Status).Select(h => h.Data).ToList();
                if (datas.Count > 0) resultado[s.Id] = datas.Max();
            }
            return resultado;
        }

        private int? DiasParaExpirar(Solicitacao s, Dictionary<int, DateTime> referencias) =>
            referencias.TryGetValue(s.Id, out var referencia)
                ? PrazoExpiracao.DiasParaExpirar(referencia, _relogio.GetUtcNow().UtcDateTime, _prazos.Dias)
                : null;

        private static SolicitacaoDto Mapear(Solicitacao s, int? diasParaExpirar) => new(
            s.Id, s.Status, s.Descricao, s.FotoUrl, s.DataAbertura, s.MatriculaSolicitante,
            s.Equipamento is null ? null : new EquipamentoDto(s.Equipamento.Id, s.Equipamento.EstacaoId, s.Equipamento.Descricao, s.Equipamento.Patrimonio),
            s.Equipamento?.Estacao is null ? null : new EstacaoDto(s.Equipamento.Estacao.Id, s.Equipamento.Estacao.Nome, s.Equipamento.Estacao.Linha),
            s.MotivoDevolucao,
            diasParaExpirar,
            s.NumeroSsm,
            s.DataRecebimento);

        public string? UltimoNumeroSsm(string prefixo) =>
            _contexto.Solicitacoes.AsNoTracking()
                .Where(s => s.NumeroSsm != null && s.NumeroSsm.StartsWith(prefixo))
                .OrderByDescending(s => s.NumeroSsm)
                .Select(s => s.NumeroSsm)
                .FirstOrDefault();

        public void Salvar(Solicitacao solicitacao, HistoricoStatus historico)
        {
            if (_contexto.Entry(solicitacao).State == EntityState.Detached)
            {
                if (solicitacao.Id == 0)
                    _contexto.Solicitacoes.Add(solicitacao);
                else
                    _contexto.Solicitacoes.Update(solicitacao);
            }

            if (solicitacao.Id != 0 && historico.StatusAnterior is not null)
                _contexto.Entry(solicitacao).Property(s => s.Status).OriginalValue = historico.StatusAnterior;

            historico.Solicitacao = solicitacao;
            _contexto.HistoricoStatus.Add(historico);

            try
            {
                _contexto.SaveChanges();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _contexto.ChangeTracker.Clear();
                throw new ConflitoDeConcorrenciaException(solicitacao.Id, ex);
            }
            catch
            {
                _contexto.ChangeTracker.Clear();
                throw;
            }
        }
    }
}
