using System.Net;
using System.Text.Json;

namespace CPTM.Manutencao.Tests.Integracao;

public sealed class CatalogoHttpTests : IClassFixture<CptmApiFactory>
{
    private readonly CptmApiFactory _api;

    public CatalogoHttpTests(CptmApiFactory api)
    {
        _api = api;
    }

    private static async Task<JsonElement> LerJson(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.Clone();

    [Theory]
    [InlineData("admin123")]
    [InlineData("tec123")]
    [InlineData("comum123")]
    public async Task Estacoes_devolve_200_para_qualquer_perfil(string matricula)
    {
        using var cliente = await _api.CriarClienteLogadoAsync(matricula);
        using var resposta = await cliente.GetAsync("/api/estacoes");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var estacoes = (await LerJson(resposta)).EnumerateArray().ToList();
        Assert.Equal(5, estacoes.Count);
        Assert.Equal(new[] { "7-Rubi", "10-Turquesa", "11-Coral", "12-Safira", "13-Jade" },
            estacoes.Select(e => e.GetProperty("linha").GetString()).ToArray());
        Assert.Equal(1, estacoes[0].GetProperty("id").GetInt32());
        Assert.Equal("Luz", estacoes[0].GetProperty("nome").GetString());
    }

    [Theory]
    [InlineData("admin123")]
    [InlineData("tec123")]
    [InlineData("comum123")]
    public async Task Equipamentos_devolve_200_para_qualquer_perfil(string matricula)
    {
        using var cliente = await _api.CriarClienteLogadoAsync(matricula);
        using var resposta = await cliente.GetAsync("/api/equipamentos?estacaoId=2");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var equipamentos = (await LerJson(resposta)).EnumerateArray().ToList();
        Assert.Equal(3, equipamentos.Count);
        Assert.All(equipamentos, e => Assert.Equal(2, e.GetProperty("estacaoId").GetInt32()));
        Assert.Equal(new[] { "PAT-BRS-0001", "PAT-BRS-0002", "PAT-BRS-0003" },
            equipamentos.Select(e => e.GetProperty("patrimonio").GetString()).ToArray());
        Assert.Equal("Catraca de acesso norte", equipamentos[0].GetProperty("descricao").GetString());
    }

    [Theory]
    [InlineData("/api/estacoes")]
    [InlineData("/api/equipamentos?estacaoId=1")]
    public async Task Sem_token_devolve_401(string rota)
    {
        using var cliente = _api.CriarCliente();
        using var resposta = await cliente.GetAsync(rota);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Equipamentos_sem_estacaoId_devolve_400()
    {
        using var cliente = await _api.CriarClienteLogadoAsync("comum123");
        using var resposta = await cliente.GetAsync("/api/equipamentos");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("O parâmetro estacaoId é obrigatório.", (await LerJson(resposta)).GetProperty("erro").GetString());
    }

    [Fact]
    public async Task Equipamentos_estacaoId_nao_numerico_devolve_400()
    {
        using var cliente = await _api.CriarClienteLogadoAsync("comum123");
        using var resposta = await cliente.GetAsync("/api/equipamentos?estacaoId=abc");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Equipamentos_estacao_inexistente_devolve_404()
    {
        using var cliente = await _api.CriarClienteLogadoAsync("comum123");
        using var resposta = await cliente.GetAsync("/api/equipamentos?estacaoId=999");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.Equal("Estação 999 não encontrada.", (await LerJson(resposta)).GetProperty("erro").GetString());
    }
}
