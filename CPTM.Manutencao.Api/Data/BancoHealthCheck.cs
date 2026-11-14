using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CPTM.Manutencao.Api.Data
{
    // Verificação de saúde usada por /health (probes do container e monitoramento): confirma que o banco responde.
    public class BancoHealthCheck : IHealthCheck
    {
        private readonly ManutencaoDbContext _contexto;

        public BancoHealthCheck(ManutencaoDbContext contexto)
        {
            _contexto = contexto;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _contexto.Database.CanConnectAsync(cancellationToken)
                    ? HealthCheckResult.Healthy("Banco de dados acessível.")
                    : HealthCheckResult.Unhealthy("Banco de dados inacessível.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Falha ao consultar o banco de dados.", ex);
            }
        }
    }
}

