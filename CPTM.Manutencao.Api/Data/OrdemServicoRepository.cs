using Microsoft.EntityFrameworkCore;
using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Data
{
    public class OrdemServicoRepository : IOrdemServicoRepository
    {
        private readonly ManutencaoDbContext _contexto;

        public OrdemServicoRepository(ManutencaoDbContext contexto)
        {
            _contexto = contexto;
        }

        public List<OrdemServicoDto> ListarDoTecnico(string matriculaTecnico) =>
            _contexto.OrdensServico.AsNoTracking()
                .Include(o => o.Solicitacao).ThenInclude(s => s!.Equipamento).ThenInclude(e => e!.Estacao)
                .Where(o => o.MatriculaTecnico == matriculaTecnico)
                .OrderByDescending(o => o.DataAbertura).ThenByDescending(o => o.Id)
                .AsEnumerable()
                .Select(Mapear)
                .ToList();

        private static OrdemServicoDto Mapear(OrdemServico o)
        {
            var s = o.Solicitacao!;
            var e = s.Equipamento;
            return new OrdemServicoDto(
                o.Id, o.SolicitacaoId, s.NumeroSsm, s.Status, s.Descricao,
                e is null ? null : new EquipamentoDto(e.Id, e.EstacaoId, e.Descricao, e.Patrimonio),
                e?.Estacao is null ? null : new EstacaoDto(e.Estacao.Id, e.Estacao.Nome, e.Estacao.Linha),
                o.MatriculaTecnico, o.DataAbertura, o.DataConclusao, o.Laudo, o.MotivoNaoExecucao);
        }
    }
}
