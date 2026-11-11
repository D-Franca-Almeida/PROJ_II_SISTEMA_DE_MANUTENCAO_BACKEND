using CPTM.Manutencao.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CPTM.Manutencao.Api.Data
{
    public class CatalogoRepository : ICatalogoRepository
    {
        private readonly ManutencaoDbContext _db;

        public CatalogoRepository(ManutencaoDbContext db)
        {
            _db = db;
        }

        public List<EstacaoDto> ListarEstacoes() =>
            _db.Estacoes.AsNoTracking()
                .Select(e => new EstacaoDto(e.Id, e.Nome, e.Linha))
                .AsEnumerable()
                .OrderBy(e => NumeroDaLinha(e.Linha)).ThenBy(e => e.Linha, StringComparer.Ordinal).ThenBy(e => e.Nome, StringComparer.Ordinal)
                .ToList();

        // "7-Rubi" antes de "10-Turquesa": ordena pelo número da linha, não pelo texto.
        private static int NumeroDaLinha(string linha) =>
            int.TryParse(linha.Split('-')[0], out var numero) ? numero : int.MaxValue;

        public bool EstacaoExiste(int estacaoId) => _db.Estacoes.Any(e => e.Id == estacaoId);

        public List<EquipamentoDto> ListarEquipamentos(int estacaoId) =>
            _db.Equipamentos.AsNoTracking()
                .Where(e => e.EstacaoId == estacaoId)
                .OrderBy(e => e.Descricao).ThenBy(e => e.Id)
                .Select(e => new EquipamentoDto(e.Id, e.EstacaoId, e.Descricao, e.Patrimonio))
                .ToList();

        public EquipamentoDetalheDto? ObterEquipamento(int equipamentoId) =>
            _db.Equipamentos.AsNoTracking()
                .Where(e => e.Id == equipamentoId)
                .Select(e => new EquipamentoDetalheDto(e.Id, e.Descricao, e.Patrimonio, e.EstacaoId, e.Estacao!.Nome, e.Estacao.Linha))
                .FirstOrDefault();
    }
}
