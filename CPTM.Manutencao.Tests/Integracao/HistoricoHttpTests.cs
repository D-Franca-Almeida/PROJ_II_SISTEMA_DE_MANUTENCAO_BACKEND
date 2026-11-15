using System.Net;
using System.Text.Json;
using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CPTM.Manutencao.Tests.Integracao;

// CPTM-87 (US-26): GET /api/solicitacoes/{id}/historico
public sealed class HistoricoHttpTests : IClassFixture<CptmApiFactory>
{
    private readonly CptmApiFactory _api;

    public HistoricoHttpTests(CptmApiFactory api)
    {
        _api = api;
    }

    private static async Task<JsonElement> LerJson(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.Clone();

    private int SslComHistoricoDeRetirada()
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        var s = new Solicitacao
        {
            Descricao = "SSL com retirada", Status = StatusSolicitacao.Retirado, MatriculaSolicitante = "comum123", EquipamentoId = 1,
            DataAbertura = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc),
            AutorizadoNome = "Maria Souza", AutorizadoDocumento = "123.456.789-00", IdentificacaoRetirada = "12345678900"
        };
        db.Solicitacoes.Add(s);
        db.SaveChanges();
        db.HistoricoStatus.AddRange(
            new HistoricoStatus { SolicitacaoId = s.Id, StatusNovo = "SOLICITADA", Matricula = "comum123", Data = s.DataAbertura },
            new HistoricoStatus { SolicitacaoId = s.Id, StatusAnterior = "SOLICITADA", StatusNovo = "APROVADA", Matricula = "admin123", Data = s.DataAbertura.AddHours(2), Observacao = "Aprovada" },
            new HistoricoStatus { SolicitacaoId = s.Id, StatusAnterior = "AGUARDANDO RETIRADA", StatusNovo = "RETIRADO", Matricula = "admin123", Data = s.DataAbertura.AddHours(1), Observacao = "Retirado por Maria Souza." });
        db.SaveChanges();
        return s.Id;
    }

    [Fact]
    public async Task Sem_token_401()
    {
        using var anonimo = _api.CriarCliente();

        using var resposta = await anonimo.GetAsync("/api/solicitacoes/1/historico");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Dono_ve_o_historico_200_com_quem_quando_de_para_e_observacao_em_utc()
    {
        using var comum = await _api.CriarClienteLogadoAsync("comum123");

        using var resposta = await comum.GetAsync("/api/solicitacoes/8/historico");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var linhas = (await LerJson(resposta)).EnumerateArray().ToList();
        Assert.Equal(2, linhas.Count);
        Assert.Equal(JsonValueKind.Null, linhas[0].GetProperty("statusAnterior").ValueKind);
        Assert.Equal("SOLICITADA", linhas[0].GetProperty("statusNovo").GetString());
        Assert.Equal("comum123", linhas[0].GetProperty("matricula").GetString());
        Assert.Equal("SOLICITADA", linhas[1].GetProperty("statusAnterior").GetString());
        Assert.Equal("APROVADA", linhas[1].GetProperty("statusNovo").GetString());
        Assert.Equal("SISTEMA", linhas[1].GetProperty("matricula").GetString());
        Assert.Equal("Carga inicial", linhas[1].GetProperty("observacao").GetString());
        Assert.Equal("2026-11-01T12:01:00Z", linhas[1].GetProperty("data").GetString());
    }

    [Theory]
    [InlineData("admin123", 8)]
    [InlineData("tec123", 10)]
    public async Task Admin_e_tecnico_da_osm_veem_o_historico_200(string matricula, int id)
    {
        using var cliente = await _api.CriarClienteLogadoAsync(matricula);

        using var resposta = await cliente.GetAsync($"/api/solicitacoes/{id}/historico");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("SOLICITADA", (await LerJson(resposta)).EnumerateArray().First().GetProperty("statusNovo").GetString());
    }

    [Fact]
    public async Task Comum_que_nao_e_dono_403()
    {
        using var comum = await _api.CriarClienteLogadoAsync("comum123");

        using var resposta = await comum.GetAsync("/api/solicitacoes/4/historico");

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task Tecnico_sem_osm_na_ssl_403()
    {
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        using var resposta = await tecnico.GetAsync("/api/solicitacoes/1/historico");

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Theory]
    [InlineData("comum123")]
    [InlineData("admin123")]
    [InlineData("tec123")]
    public async Task Ssl_inexistente_404(string matricula)
    {
        using var cliente = await _api.CriarClienteLogadoAsync(matricula);

        using var resposta = await cliente.GetAsync("/api/solicitacoes/99999/historico");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.True((await LerJson(resposta)).TryGetProperty("erro", out _));
    }

    [Fact]
    public async Task Id_que_nao_e_numero_404()
    {
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.GetAsync("/api/solicitacoes/abc/historico");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Historico_vem_em_ordem_cronologica_e_nao_vaza_documentos()
    {
        var id = SslComHistoricoDeRetirada();
        using var comum = await _api.CriarClienteLogadoAsync("comum123");

        using var resposta = await comum.GetAsync($"/api/solicitacoes/{id}/historico");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await resposta.Content.ReadAsStringAsync();
        var status = JsonDocument.Parse(corpo).RootElement.EnumerateArray().Select(l => l.GetProperty("statusNovo").GetString()).ToList();
        Assert.Equal(new[] { "SOLICITADA", "RETIRADO", "APROVADA" }, status);
        Assert.DoesNotContain("123.456.789-00", corpo);
        Assert.DoesNotContain("12345678900", corpo);
        Assert.DoesNotContain("documento", corpo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Detalhe_continua_devolvendo_o_mesmo_historico()
    {
        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        using var detalhe = await admin.GetAsync("/api/solicitacoes/8");
        using var historico = await admin.GetAsync("/api/solicitacoes/8/historico");

        Assert.Equal((await LerJson(detalhe)).GetProperty("historico").GetRawText(), (await LerJson(historico)).GetRawText());
    }
}
