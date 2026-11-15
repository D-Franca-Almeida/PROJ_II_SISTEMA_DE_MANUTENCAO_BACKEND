using System.Net;
using System.Text.Json;
using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CPTM.Manutencao.Tests.Integracao;

// CPTM-79 (US-24): PUT /api/solicitacoes/{id}/autorizar-retirada
public sealed class AutorizarRetiradaHttpTests : IClassFixture<CptmApiFactory>
{
    private const string CorpoValido = "{ \"nome\": \"  Maria Souza  \", \"documento\": \" 123.456.789-00 \" }";

    private readonly CptmApiFactory _api;

    public AutorizarRetiradaHttpTests(CptmApiFactory api)
    {
        _api = api;
    }

    private int Inserir(string status, string solicitante = "comum123")
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        var s = new Solicitacao
        {
            Descricao = "SSL de teste",
            Status = status,
            MatriculaSolicitante = solicitante,
            EquipamentoId = 1,
            DataAbertura = DateTime.UtcNow.AddDays(-2)
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

    private static async Task<JsonElement> LerJson(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.Clone();

    [Theory]
    [InlineData("comum123")]
    [InlineData("admin123")]
    public async Task Comum_dono_ou_admin_autoriza_200_grava_nome_documento_e_historico(string matricula)
    {
        var id = Inserir(StatusSolicitacao.AguardandoRetirada);
        using var cliente = await _api.CriarClienteLogadoAsync(matricula);

        using var resposta = await cliente.PutAsync($"/api/solicitacoes/{id}/autorizar-retirada", CptmApiFactory.Json(CorpoValido));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await LerJson(resposta);
        Assert.Equal("RETIRADA AUTORIZADA", corpo.GetProperty("novoStatus").GetString());
        Assert.Equal("Retirada autorizada para o terceiro informado.", corpo.GetProperty("message").GetString());
        var (s, historico) = Estado(id);
        Assert.Equal("RETIRADA AUTORIZADA", s.Status);
        Assert.Equal("Maria Souza", s.AutorizadoNome);
        Assert.Equal("123.456.789-00", s.AutorizadoDocumento);
        var h = Assert.Single(historico);
        Assert.Equal("AGUARDANDO RETIRADA", h.StatusAnterior);
        Assert.Equal("RETIRADA AUTORIZADA", h.StatusNovo);
        Assert.Equal(matricula, h.Matricula);
        Assert.Equal("Retirada autorizada para Maria Souza.", h.Observacao);
        Assert.DoesNotContain("123.456", h.Observacao);
    }

    [Theory]
    [InlineData("{ \"documento\": \"123\" }", "O nome de quem vai retirar é obrigatório.")]
    [InlineData("{ \"nome\": \"   \", \"documento\": \"123\" }", "O nome de quem vai retirar é obrigatório.")]
    [InlineData("{ \"nome\": \"Maria\" }", "O documento (CPF ou matrícula) de quem vai retirar é obrigatório.")]
    [InlineData("{ \"nome\": \"Maria\", \"documento\": \"  \" }", "O documento (CPF ou matrícula) de quem vai retirar é obrigatório.")]
    [InlineData("{ \"nome\": \"Maria\", \"documento\": \"12 34\" }", "O documento deve ser um CPF ou uma matrícula (letras, números, ponto, hífen ou barra).")]
    [InlineData("{ \"nome\": \"Maria\", \"documento\": \"<script>\" }", "O documento deve ser um CPF ou uma matrícula (letras, números, ponto, hífen ou barra).")]
    [InlineData("{}", "O nome de quem vai retirar é obrigatório.")]
    public async Task Nome_e_documento_obrigatorios_400_e_nao_altera(string corpo, string erro)
    {
        var id = Inserir(StatusSolicitacao.AguardandoRetirada);
        using var comum = await _api.CriarClienteLogadoAsync("comum123");

        using var resposta = await comum.PutAsync($"/api/solicitacoes/{id}/autorizar-retirada", CptmApiFactory.Json(corpo));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal(erro, (await LerJson(resposta)).GetProperty("erro").GetString());
        var (s, historico) = Estado(id);
        Assert.Equal("AGUARDANDO RETIRADA", s.Status);
        Assert.Null(s.AutorizadoNome);
        Assert.Null(s.AutorizadoDocumento);
        Assert.Empty(historico);
    }

    [Fact]
    public async Task Sem_corpo_400()
    {
        var id = Inserir(StatusSolicitacao.AguardandoRetirada);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PutAsync($"/api/solicitacoes/{id}/autorizar-retirada", null);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Theory]
    [InlineData(151, 0)]
    [InlineData(0, 51)]
    public async Task Limites_de_tamanho_400(int tamanhoNome, int tamanhoDocumento)
    {
        var id = Inserir(StatusSolicitacao.AguardandoRetirada);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        var nome = tamanhoNome > 0 ? new string('n', tamanhoNome) : "Maria";
        var documento = tamanhoDocumento > 0 ? new string('1', tamanhoDocumento) : "123";

        using var resposta = await admin.PutAsync($"/api/solicitacoes/{id}/autorizar-retirada",
            CptmApiFactory.Json($"{{ \"nome\": \"{nome}\", \"documento\": \"{documento}\" }}"));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("AGUARDANDO RETIRADA", Estado(id).Solicitacao.Status);
    }

    [Theory]
    [InlineData("CONCLUIDA")]
    [InlineData("RETIRADA AUTORIZADA")]
    [InlineData("RETIRADO")]
    [InlineData("SEM REPARO")]
    public async Task So_de_aguardando_retirada_400(string origem)
    {
        var id = Inserir(origem);
        using var comum = await _api.CriarClienteLogadoAsync("comum123");

        using var resposta = await comum.PutAsync($"/api/solicitacoes/{id}/autorizar-retirada", CptmApiFactory.Json(CorpoValido));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal(
            $"Transição inválida: só é possível autorizar a retirada de uma solicitação AGUARDANDO RETIRADA (status atual: {origem}).",
            (await LerJson(resposta)).GetProperty("erro").GetString());
        Assert.Equal(origem, Estado(id).Solicitacao.Status);
    }

    [Fact]
    public async Task Comum_que_nao_e_dono_403()
    {
        var id = Inserir(StatusSolicitacao.AguardandoRetirada, solicitante: "outro456");
        using var comum = await _api.CriarClienteLogadoAsync("comum123");

        using var resposta = await comum.PutAsync($"/api/solicitacoes/{id}/autorizar-retirada", CptmApiFactory.Json(CorpoValido));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        var (s, historico) = Estado(id);
        Assert.Equal("AGUARDANDO RETIRADA", s.Status);
        Assert.Empty(historico);
    }

    [Fact]
    public async Task Tecnico_403()
    {
        var id = Inserir(StatusSolicitacao.AguardandoRetirada);
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        using var resposta = await tecnico.PutAsync($"/api/solicitacoes/{id}/autorizar-retirada", CptmApiFactory.Json(CorpoValido));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("AGUARDANDO RETIRADA", Estado(id).Solicitacao.Status);
    }

    [Fact]
    public async Task Sem_token_401_e_inexistente_404()
    {
        using var anonimo = _api.CriarCliente();
        using var semToken = await anonimo.PutAsync("/api/solicitacoes/1/autorizar-retirada", CptmApiFactory.Json(CorpoValido));
        Assert.Equal(HttpStatusCode.Unauthorized, semToken.StatusCode);

        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        using var inexistente = await comum.PutAsync("/api/solicitacoes/999999/autorizar-retirada", CptmApiFactory.Json(CorpoValido));
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal("Solicitação 999999 não encontrada.", (await LerJson(inexistente)).GetProperty("erro").GetString());
    }

    [Fact]
    public async Task Comum_e_admin_simultaneos_so_um_vence()
    {
        var id = Inserir(StatusSolicitacao.AguardandoRetirada);
        var tokenComum = await _api.LoginAsync("comum123");
        var tokenAdmin = await _api.LoginAsync("admin123");
        var clientes = Enumerable.Range(0, 6).Select(i => _api.CriarCliente(i % 2 == 0 ? tokenComum : tokenAdmin)).ToList();

        var respostas = await Task.WhenAll(clientes.Select((c, i) => c.PutAsync($"/api/solicitacoes/{id}/autorizar-retirada",
            CptmApiFactory.Json($"{{ \"nome\": \"Pessoa {i}\", \"documento\": \"DOC{i}\" }}"))));

        var vencedora = Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(respostas.Where(r => r != vencedora),
            r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict }));
        var (s, historico) = Estado(id);
        Assert.Equal("RETIRADA AUTORIZADA", s.Status);
        var i = Array.IndexOf(respostas, vencedora);
        Assert.Equal($"Pessoa {i}", s.AutorizadoNome);
        Assert.Equal($"DOC{i}", s.AutorizadoDocumento);
        Assert.Single(historico);
        foreach (var r in respostas) r.Dispose();
        foreach (var c in clientes) c.Dispose();
    }

    [Fact]
    public void Retirada_autorizada_esta_na_lista_de_status_validos()
    {
        Assert.Contains(StatusSolicitacao.RetiradaAutorizada, StatusSolicitacao.Todos);
    }
}
