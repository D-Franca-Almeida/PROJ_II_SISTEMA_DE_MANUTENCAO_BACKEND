using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Data
{
    public interface ICatalogoRepository
    {
        List<EstacaoDto> ListarEstacoes();

        bool EstacaoExiste(int estacaoId);

        List<EquipamentoDto> ListarEquipamentos(int estacaoId);
    }
}
