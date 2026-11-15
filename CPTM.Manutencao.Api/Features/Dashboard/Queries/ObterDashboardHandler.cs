using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Features.Dashboard.Queries
{
    public interface IObterDashboardHandler
    {
        DashboardDto Executar();
    }

    // US-27: indicadores do painel do Administrativo
    public class ObterDashboardHandler : IObterDashboardHandler
    {
        public const int DiasParaAlertaDeExpiracao = 2;

        private static readonly (string De, string Para)[] Etapas =
        {
            (StatusSolicitacao.Solicitada, StatusSolicitacao.Aprovada),
            (StatusSolicitacao.Aprovada, StatusSolicitacao.Recebida),
            (StatusSolicitacao.Recebida, StatusSolicitacao.Concluida)
        };

        private readonly IDashboardRepository _dashboard;
        private readonly ISolicitacaoRepository _solicitacoes;

        public ObterDashboardHandler(IDashboardRepository dashboard, ISolicitacaoRepository solicitacoes)
        {
            _dashboard = dashboard;
            _solicitacoes = solicitacoes;
        }

        public DashboardDto Executar()
        {
            var porStatus = ContarPorStatus();
            return new DashboardDto(porStatus.Sum(c => c.Quantidade), porStatus, CalcularTemposMedios(), ListarPertoDeExpirar());
        }

        private List<ContagemPorStatusDto> ContarPorStatus()
        {
            var contagem = _dashboard.ContarPorStatus();
            return StatusSolicitacao.Todos.Select(s => new ContagemPorStatusDto(s, contagem.GetValueOrDefault(s))).ToList();
        }

        private List<TempoMedioEtapaDto> CalcularTemposMedios() =>
            Etapas.Select(e => CalcularTempoMedio(e.De, e.Para)).ToList();

        private TempoMedioEtapaDto CalcularTempoMedio(string de, string para)
        {
            var horas = _dashboard.DuracoesEmHorasEntre(de, para);
            return new TempoMedioEtapaDto(de, para, horas.Count == 0 ? null : Math.Round(horas.Average(), 2), horas.Count);
        }

        private List<SolicitacaoDto> ListarPertoDeExpirar() =>
            _solicitacoes.Listar(StatusSolicitacao.Aprovada, null)
                .Concat(_solicitacoes.Listar(StatusSolicitacao.Devolvida, null))
                .Where(s => s.DiasParaExpirar <= DiasParaAlertaDeExpiracao)
                .OrderBy(s => s.DiasParaExpirar).ThenBy(s => s.Id)
                .ToList();
    }
}
