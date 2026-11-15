using System.Net;
using System.Text.Json;
using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CPTM.Manutencao.Tests.Integracao;

// CPTM-82 (US-11): PUT /retirar valida o documento contra o solicitante ou o autorizado (US-24)
public sealed class RetiradaDocumentoHttpTests : IClassFixture<CptmApiFactory>
{
    private readonly CptmApiFactory _api;

    public RetiradaDocumentoHttpTests(CptmApiFactory api)
    {
        _api = api;
    }

    private int Inserir(string status, string? autorizadoDocumento = null)
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        var s = new Solicitacao
        {
            Descricao = "SSL de teste",
            Status = status,
            MatriculaSolicitante = "comum123",
            EquipamentoId = 1,
            DataAbertura = DateTime.UtcNow.AddDays(-2),
            AutorizadoNome = autorizadoDocumento is null ? null : "Maria Souza",
            AutorizadoDocumento = autorizadoDocumento
        };
        db.Solicitacoes.Add(s);
        db.SaveChanges();
        return s.Id;
    }

    private (Solicitacao Solicitacao, List<HistoricoStatus> Historico) Estado(int id)
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        return (db.Solicitacoes.AsNoTracking().Single(x => x.Id == id),
            db.HistoricoStatus.AsNoTracking().Where(h => h.SolicitacaoId == id).OrderBy(h => h.Id).ToList());
    }

    private static StringContent Documento(string documento) =>
        CptmApiFactory.Json(JsonSerializer.Serialize(new { identificacaoRecebedor = documento }));

    private static async Task<JsonElement> LerJson(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.Clone();

    [Theory]
    [InlineData("comum123")]
    [InlineData(" COMUM123 ")]
    public async Task Aguardando_retirada_com_matricula_do_solicitante_200(string documento)
    {
        var id = Inserir(StatusSolicitacao.AguardandoRetirada);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PutAsync($"/api/solicitacoes/{id}/retirar", Documento(documento));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var (s, historico) = Estado(id);
        Assert.Equal("RETIRADO", s.Status);
        Assert.Equal(documento.Trim(), s.IdentificacaoRetirada);
        Assert.Equal(DateTimeKind.Utc, s.DataRetirada!.Value.Kind);
        Assert.Equal("AGUARDANDO RETIRADA", Assert.Single(historico).StatusAnterior);
    }

    [Theory]
    [InlineData("12345678900")]
    [InlineData("tec123")]
    [InlineData("comum1234")]
    public async Task Aguardando_retirada_com_outro_documento_400_e_nao_altera(string documento)
    {
        var id = Inserir(StatusSolicitacao.AguardandoRetirada);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PutAsync($"/api/solicitacoes/{id}/retirar", Documento(documento));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal(
            "O documento informado não confere com a matrícula do solicitante. Para outra pessoa retirar, registre antes a autorização de retirada.",
            (await LerJson(resposta)).GetProperty("erro").GetString());
        var (s, historico) = Estado(id);
        Assert.Equal("AGUARDANDO RETIRADA", s.Status);
        Assert.Null(s.IdentificacaoRetirada);
        Assert.Null(s.DataRetirada);
        Assert.Empty(historico);
    }

    [Theory]
    [InlineData("123.456.789-00")]
    [InlineData("12345678900")]
    [InlineData(" 123 456 789 00 ")]
    [InlineData("comum123")]
    public async Task Retirada_autorizada_aceita_documento_autorizado_ou_solicitante_200(string documento)
    {
        var id = Inserir(StatusSolicitacao.RetiradaAutorizada, autorizadoDocumento: "123.456.789-00");
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PutAsync($"/api/solicitacoes/{id}/retirar", Documento(documento));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("RETIRADO", (await LerJson(resposta)).GetProperty("novoStatus").GetString());
        var (s, historico) = Estado(id);
        Assert.Equal("RETIRADO", s.Status);
        Assert.Equal(documento.Trim(), s.IdentificacaoRetirada);
        var h = Assert.Single(historico);
        Assert.Equal("RETIRADA AUTORIZADA", h.StatusAnterior);
        Assert.Equal("RETIRADO", h.StatusNovo);
        Assert.Equal("admin123", h.Matricula);
    }

    [Theory]
    [InlineData("98765432100")]
    [InlineData("---")]
    public async Task Retirada_autorizada_com_documento_diferente_400(string documento)
    {
        var id = Inserir(StatusSolicitacao.RetiradaAutorizada, autorizadoDocumento: "123.456.789-00");
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PutAsync($"/api/solicitacoes/{id}/retirar", Documento(documento));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("O documento informado não confere com a matrícula do solicitante nem com o documento autorizado.",
            (await LerJson(resposta)).GetProperty("erro").GetString());
        Assert.Equal("RETIRADA AUTORIZADA", Estado(id).Solicitacao.Status);
    }

    [Fact]
    public async Task Retirado_e_final_nenhuma_acao_de_fechamento_aceita()
    {
        var id = Inserir(StatusSolicitacao.RetiradaAutorizada, autorizadoDocumento: "123.456.789-00");
        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync($"/api/solicitacoes/{id}/retirar", Documento("12345678900"))).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/solicitacoes/{id}/retirar", Documento("comum123"))).StatusCode);
        foreach (var rota in new[] { "liberar-retirada", "sem-reparo", "inutilizar" })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/solicitacoes/{id}/{rota}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/solicitacoes/{id}/autorizar-retirada",
            CptmApiFactory.Json("{ \"nome\": \"X\", \"documento\": \"1\" }"))).StatusCode);
        var (s, historico) = Estado(id);
        Assert.Equal("RETIRADO", s.Status);
        Assert.Single(historico);
    }

    [Theory]
    [InlineData("tec123")]
    [InlineData("comum123")]
    public async Task Retirada_autorizada_so_admin_registra_403(string matricula)
    {
        var id = Inserir(StatusSolicitacao.RetiradaAutorizada, autorizadoDocumento: "123.456.789-00");
        using var cliente = await _api.CriarClienteLogadoAsync(matricula);

        using var resposta = await cliente.PutAsync($"/api/solicitacoes/{id}/retirar", Documento("12345678900"));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("RETIRADA AUTORIZADA", Estado(id).Solicitacao.Status);
    }

    [Fact]
    public async Task Retirar_sem_token_401_e_inexistente_404()
    {
        using var anonimo = _api.CriarCliente();
        using var semToken = await anonimo.PutAsync("/api/solicitacoes/1/retirar", Documento("comum123"));
        Assert.Equal(HttpStatusCode.Unauthorized, semToken.StatusCode);

        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        using var inexistente = await admin.PutAsync("/api/solicitacoes/999999/retirar", Documento("comum123"));
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal("Solicitação 999999 não encontrada.", (await LerJson(inexistente)).GetProperty("erro").GetString());
    }

    [Fact]
    public async Task Retiradas_simultaneas_so_uma_vence()
    {
        var id = Inserir(StatusSolicitacao.RetiradaAutorizada, autorizadoDocumento: "123.456.789-00");
        var token = await _api.LoginAsync("admin123");
        var clientes = Enumerable.Range(0, 6).Select(_ => _api.CriarCliente(token)).ToList();

        var respostas = await Task.WhenAll(clientes.Select((c, i) =>
            c.PutAsync($"/api/solicitacoes/{id}/retirar", Documento(i % 2 == 0 ? "comum123" : "12345678900"))));

        Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(respostas.Where(r => r.StatusCode != HttpStatusCode.OK),
            r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict }));
        Assert.Single(Estado(id).Historico);
        foreach (var r in respostas) r.Dispose();
        foreach (var c in clientes) c.Dispose();
    }

    [Fact]
    public async Task Detalhe_mostra_destino_autorizacao_e_retirada_em_utc()
    {
        var id = Inserir(StatusSolicitacao.RetiradaAutorizada, autorizadoDocumento: "123.456.789-00");
        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync($"/api/solicitacoes/{id}/retirar", Documento("123.456.789-00"))).StatusCode);

        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        using var resposta = await comum.GetAsync($"/api/solicitacoes/{id}");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await LerJson(resposta);
        Assert.Equal("RETIRADO", corpo.GetProperty("status").GetString());
        Assert.Equal("Maria Souza", corpo.GetProperty("autorizadoNome").GetString());
        Assert.Equal("123.456.789-00", corpo.GetProperty("autorizadoDocumento").GetString());
        Assert.Equal("123.456.789-00", corpo.GetProperty("retiradoPorDocumento").GetString());
        Assert.EndsWith("Z", corpo.GetProperty("dataRetirada").GetString());
        Assert.Equal(JsonValueKind.Null, corpo.GetProperty("observacaoDestino").ValueKind);
    }
}
