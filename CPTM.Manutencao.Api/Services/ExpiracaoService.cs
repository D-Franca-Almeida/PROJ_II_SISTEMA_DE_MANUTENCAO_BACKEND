using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Services
{
    public class PrazosOptions
    {
        public int Dias { get; set; } = 10;
        public bool ExecucaoAutomatica { get; set; } = true;
        public int IntervaloMinutos { get; set; } = 1440;
    }

    public static class PrazoExpiracao
    {
        public static bool Expira(string status) =>
            status == StatusSolicitacao.Devolvida || status == StatusSolicitacao.Aprovada;

        public static bool Vencida(DateTime referencia, DateTime agora, int dias) =>
            agora - referencia > TimeSpan.FromDays(dias);

        public static int DiasParaExpirar(DateTime referencia, DateTime agora, int dias) =>
            Math.Max(0, (int)Math.Ceiling(dias - (agora - referencia).TotalDays));
    }

    public record SolicitacaoExpirada(int Id, string StatusAnterior, DateTime DataReferencia);

    public record ResultadoExpiracao(int Quantidade, List<SolicitacaoExpirada> Solicitacoes);

    public interface IExpiracaoService
    {
        ResultadoExpiracao Executar(string? acionadoPor = null);
    }

    public class ExpiracaoService : IExpiracaoService
    {
        private readonly ISolicitacaoRepository _repositorio;
        private readonly IWorkflowService _workflow;
        private readonly TimeProvider _relogio;
        private readonly PrazosOptions _prazos;

        public ExpiracaoService(ISolicitacaoRepository repositorio, IWorkflowService workflow, TimeProvider relogio, PrazosOptions prazos)
        {
            _repositorio = repositorio;
            _workflow = workflow;
            _relogio = relogio;
            _prazos = prazos;
        }

        // Toda expiração é gravada com a matrícula SISTEMA; no disparo manual a observação registra o Admin que acionou.
        public ResultadoExpiracao Executar(string? acionadoPor = null)
        {
            var agora = _relogio.GetUtcNow().UtcDateTime;
            var expiradas = new List<SolicitacaoExpirada>();

            foreach (var candidato in _repositorio.ObterCandidatosExpiracao())
            {
                if (!PrazoExpiracao.Vencida(candidato.DataReferencia, agora, _prazos.Dias)) continue;

                var s = candidato.Solicitacao;
                var statusAnterior = s.Status;
                var observacao = $"Expirada automaticamente: {statusAnterior} há mais de {_prazos.Dias} dias sem ação (desde {candidato.DataReferencia:dd/MM/yyyy}).";
                if (!string.IsNullOrWhiteSpace(acionadoPor))
                    observacao += $" Execução manual acionada por {acionadoPor}.";

                _workflow.Transicionar(s, StatusSolicitacao.Expirada, HistoricoStatus.MatriculaSistema, observacao);
                expiradas.Add(new SolicitacaoExpirada(s.Id, statusAnterior, candidato.DataReferencia));
            }

            return new ResultadoExpiracao(expiradas.Count, expiradas);
        }
    }

    public class ExpiracaoBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _escopos;
        private readonly PrazosOptions _prazos;
        private readonly TimeProvider _relogio;
        private readonly ILogger<ExpiracaoBackgroundService> _log;

        public ExpiracaoBackgroundService(IServiceScopeFactory escopos, PrazosOptions prazos, TimeProvider relogio, ILogger<ExpiracaoBackgroundService> log)
        {
            _escopos = escopos;
            _prazos = prazos;
            _relogio = relogio;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken parada)
        {
            if (!_prazos.ExecucaoAutomatica) return;

            using var temporizador = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, _prazos.IntervaloMinutos)), _relogio);
            do
            {
                try
                {
                    using var escopo = _escopos.CreateScope();
                    var resultado = escopo.ServiceProvider.GetRequiredService<IExpiracaoService>().Executar();
                    _log.LogInformation("Expiração automática: {Quantidade} solicitação(ões) expirada(s).", resultado.Quantidade);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Falha na rotina de expiração automática.");
                }
            } while (await temporizador.WaitForNextTickAsync(parada));
        }
    }
}
