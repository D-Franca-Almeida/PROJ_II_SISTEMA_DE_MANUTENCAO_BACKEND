using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Features.Dashboard.Queries
{
    public record ContagemPorStatusDto(string Status, int Quantidade);

    // MediaHoras é nula quando nenhuma SSL passou pelos dois status da etapa
    public record TempoMedioEtapaDto(string De, string Para, double? MediaHoras, int Quantidade);

    public record DashboardDto(
        int TotalSolicitacoes,
        List<ContagemPorStatusDto> PorStatus,
        List<TempoMedioEtapaDto> TemposMedios,
        List<SolicitacaoDto> ExpiramEmAteDoisDias);
}
