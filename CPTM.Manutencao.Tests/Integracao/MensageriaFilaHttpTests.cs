using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CPTM.Manutencao.Tests.Integracao;

// Valida que eventos de login e transição de status são publicados na fila de mensageria (INotificacaoQueue)
public sealed class MensageriaFilaHttpTests : IClassFixture<CptmApiFactory>
{
    private readonly CptmApiFactory _api;

    public MensageriaFilaHttpTests(CptmApiFactory api)
    {
        _api = api;
    }

    [Fact]
    public async Task Login_e_mudanca_de_status_publicam_eventos_na_fila_e_admin_consegue_consultar()
    {
        using var anonimo = _api.CriarCliente();
        await anonimo.PostAsJsonAsync("/api/Auth/login", new { matricula = "usuario_invalido", senha = "errada" });

        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        await admin.PutAsync("/api/solicitacoes/1/aprovar", null);

        using var resposta = await admin.GetAsync("/api/admin/eventos-fila?limite=20");
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        var eventos = json.RootElement.EnumerateArray().ToList();

        Assert.Contains(eventos, e =>
            e.GetProperty("tipoEvento").GetString() == "LOGIN_FALHA" &&
            e.GetProperty("matricula").GetString() == "usuario_invalido");

        Assert.Contains(eventos, e =>
            e.GetProperty("tipoEvento").GetString() == "LOGIN_SUCESSO" &&
            e.GetProperty("matricula").GetString() == "admin123");

        Assert.Contains(eventos, e =>
            e.GetProperty("tipoEvento").GetString() == "MUDANCA_STATUS" &&
            e.GetProperty("solicitacaoId").GetInt32() == 1 &&
            e.GetProperty("statusNovo").GetString() == "APROVADA");
    }
}
