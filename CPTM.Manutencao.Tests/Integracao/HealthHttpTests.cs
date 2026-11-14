using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CPTM.Manutencao.Tests.Integracao;

// Health check usado pelas probes do container e pelo monitoramento (anônimo, sem token)
public sealed class HealthHttpTests : IClassFixture<CptmApiFactory>
{
    private readonly CptmApiFactory _api;

    public HealthHttpTests(CptmApiFactory api)
    {
        _api = api;
    }

    [Fact]
    public async Task Health_responde_200_healthy_sem_autenticacao()
    {
        using var cliente = _api.CriarCliente();

        using var resposta = await cliente.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("Healthy", await resposta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Rota_protegida_continua_exigindo_token()
    {
        using var cliente = _api.CriarCliente();

        using var resposta = await cliente.GetAsync("/api/solicitacoes");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }
}

