using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CPTM.Manutencao.Tests.Integracao;

public sealed class AuthHttpTests : IClassFixture<CptmApiFactory>
{
    private readonly CptmApiFactory _api;

    public AuthHttpTests(CptmApiFactory api)
    {
        _api = api;
    }

    [Theory]
    [InlineData("admin123")]
    [InlineData("tec123")]
    [InlineData("comum123")]
    public async Task Login_valido_devolve_200_com_token(string matricula)
    {
        using var cliente = _api.CriarCliente();
        using var resposta = await cliente.PostAsJsonAsync("/api/Auth/login", new { matricula, senha = "senha" });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("token").GetString()));
    }

    [Theory]
    [InlineData("admin123", "errada")]
    [InlineData("naoexiste", "senha")]
    public async Task Login_invalido_devolve_401(string matricula, string senha)
    {
        using var cliente = _api.CriarCliente();
        using var resposta = await cliente.PostAsJsonAsync("/api/Auth/login", new { matricula, senha });

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        Assert.Equal("Matrícula ou senha inválidos.", json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Token_invalido_devolve_401()
    {
        using var cliente = _api.CriarCliente("token-invalido");
        using var resposta = await cliente.GetAsync("/api/estacoes");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }
}
