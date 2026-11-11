using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Data
{
    public interface IOrdemServicoRepository
    {
        // OSMs do técnico, mais recentes primeiro (DataAbertura desc, Id desc)
        List<OrdemServicoDto> ListarDoTecnico(string matriculaTecnico);
    }
}
