using Microsoft.EntityFrameworkCore;
using CPTM.Manutencao.Api.Models;
using CPTM.Manutencao.Api.Services;

namespace CPTM.Manutencao.Api.Data
{
    public class PedidoPecaRepository : IPedidoPecaRepository
    {
        private readonly ManutencaoDbContext _contexto;

        public PedidoPecaRepository(ManutencaoDbContext contexto)
        {
            _contexto = contexto;
        }

        public OrdemServico? ObterOrdem(int ordemServicoId) =>
            _contexto.OrdensServico.Include(o => o.Solicitacao).FirstOrDefault(o => o.Id == ordemServicoId);

        public PedidoPeca? ObterPorId(int id) =>
            _contexto.PedidosPeca.Include(p => p.OrdemServico).ThenInclude(o => o!.Solicitacao).FirstOrDefault(p => p.Id == id);

        public List<PedidoPecaDto> ListarDaOrdem(int ordemServicoId) =>
            Consulta().Where(p => p.OrdemServicoId == ordemServicoId)
                .OrderBy(p => p.DataPedido).ThenBy(p => p.Id)
                .AsEnumerable().Select(Mapear).ToList();

        public List<PedidoPecaDto> Listar(IReadOnlyCollection<string>? status)
        {
            var consulta = Consulta();
            if (status is not null)
                consulta = consulta.Where(p => status.Contains(p.Status));
            return consulta.OrderBy(p => p.DataPedido).ThenBy(p => p.Id).AsEnumerable().Select(Mapear).ToList();
        }

        public PedidoPecaDto ObterDto(int id) => Mapear(Consulta().Single(p => p.Id == id));

        public void Salvar(PedidoPeca pedido)
        {
            if (pedido.Id == 0)
                _contexto.PedidosPeca.Add(pedido);
            try
            {
                _contexto.SaveChanges();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _contexto.ChangeTracker.Clear();
                throw new ConflitoDeConcorrenciaException(
                    pedido.Id != 0
                        ? $"O pedido de peça {pedido.Id} foi alterado por outra operação ao mesmo tempo. Atualize a lista e tente novamente."
                        : $"A OSM {pedido.OrdemServicoId} foi alterada por outra operação ao mesmo tempo. Atualize e tente novamente.",
                    ex);
            }
            catch
            {
                _contexto.ChangeTracker.Clear();
                throw;
            }
        }

        public void MarcarStatusDaSolicitacaoComoLido(Solicitacao solicitacao) =>
            _contexto.Entry(solicitacao).Property(s => s.Status).IsModified = true;

        private IQueryable<PedidoPeca> Consulta() =>
            _contexto.PedidosPeca.AsNoTracking().Include(p => p.OrdemServico).ThenInclude(o => o!.Solicitacao);

        private static PedidoPecaDto Mapear(PedidoPeca p)
        {
            var o = p.OrdemServico!;
            var s = o.Solicitacao!;
            return new PedidoPecaDto(p.Id, p.OrdemServicoId, o.SolicitacaoId, s.NumeroSsm, s.Status, o.MatriculaTecnico,
                p.Item, p.Quantidade, p.Justificativa, p.Status, p.DataPedido, p.ObservacaoAlmoxarifado,
                p.MatriculaEncaminhamento, p.DataEncaminhamento, p.MatriculaRecebimento, p.DataRecebimento,
                p.MatriculaCancelamento, p.DataCancelamento, p.MotivoCancelamento);
        }
    }
}
