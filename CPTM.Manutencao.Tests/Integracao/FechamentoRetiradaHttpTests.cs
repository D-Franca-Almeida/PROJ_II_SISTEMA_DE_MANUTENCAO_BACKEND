using System.Net;
using System.Text.Json;
using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CPTM.Manutencao.Tests.Integracao;

// US-10 (destino: AGUARDANDO RETIRADA | SEM REPARO) e US-11 (protocolo de retirada)
public sealed class FechamentoRetiradaHttpTests : IClassFixture<CptmApiFactory>
{
    private readonly CptmApiFactory _api;

    public FechamentoRetiradaHttpTests(CptmApiFactory api)
    {
        _api = api;
    }

    private int Inserir(string status)
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        var s = new Solicitacao
        {
            Descricao = "SSL de teste",
            Status = status,
            MatriculaSolicitante = "comum123",
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

    // ---------- US-10 ----------

    [Fact]
    public async Task Admin_libera_concluida_para_retirada_200_e_grava_historico()
    {
        var id = Inserir(StatusSolicitacao.Concluida);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PutAsync($"/api/solicitacoes/{id}/aguardar-retirada",
            CptmApiFactory.Json("{ \"observacao\": \"  Pronto na prateleira B.  \" }"));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("AGUARDANDO RETIRADA", (await LerJson(resposta)).GetProperty("novoStatus").GetString());
        var (s, historico) = Estado(id);
        Assert.Equal("AGUARDANDO RETIRADA", s.Status);
        Assert.Equal("Pronto na prateleira B.", s.ObservacaoDestino);
        var h = Assert.Single(historico);
        Assert.Equal("CONCLUIDA", h.StatusAnterior);
        Assert.Equal("AGUARDANDO RETIRADA", h.StatusNovo);
        Assert.Equal("admin123", h.Matricula);
    }

    [Fact]
    public async Task Admin_libera_para_retirada_sem_corpo_200()
    {
        var id = Inserir(StatusSolicitacao.Concluida);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PutAsync($"/api/solicitacoes/{id}/aguardar-retirada", null);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Null(Estado(id).Solicitacao.ObservacaoDestino);
    }

    [Fact]
    public async Task Admin_condena_concluida_como_sem_reparo_200()
    {
        var id = Inserir(StatusSolicitacao.Concluida);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PutAsync($"/api/solicitacoes/{id}/sem-reparo",
            CptmApiFactory.Json("{ \"observacao\": \"Placa-mãe queimada, sem reposição.\" }"));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("SEM REPARO", (await LerJson(resposta)).GetProperty("novoStatus").GetString());
        var (s, historico) = Estado(id);
        Assert.Equal("SEM REPARO", s.Status);
        Assert.Equal("Placa-mãe queimada, sem reposição.", s.ObservacaoDestino);
        Assert.Single(historico);
    }

    [Theory]
    [InlineData("EM MANUTENCAO", "aguardar-retirada")]
    [InlineData("APROVADA", "aguardar-retirada")]
    [InlineData("EM MANUTENCAO", "sem-reparo")]
    [InlineData("AGUARDANDO RETIRADA", "sem-reparo")]
    [InlineData("SEM REPARO", "aguardar-retirada")]
    public async Task Destino_so_a_partir_de_concluida_400(string statusAtual, string rota)
    {
        var id = Inserir(statusAtual);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PutAsync($"/api/solicitacoes/{id}/{rota}", null);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal(statusAtual, Estado(id).Solicitacao.Status);
    }

    [Theory]
    [InlineData("tec123", "aguardar-retirada")]
    [InlineData("tec123", "sem-reparo")]
    [InlineData("comum123", "aguardar-retirada")]
    [InlineData("comum123", "sem-reparo")]
    public async Task Destino_so_para_admin_403(string matricula, string rota)
    {
        var id = Inserir(StatusSolicitacao.Concluida);
        using var cliente = await _api.CriarClienteLogadoAsync(matricula);

        using var resposta = await cliente.PutAsync($"/api/solicitacoes/{id}/{rota}", null);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("CONCLUIDA", Estado(id).Solicitacao.Status);
    }

    [Fact]
    public async Task Destino_sem_token_401_e_inexistente_404()
    {
        using var anonimo = _api.CriarCliente();
        using var semToken = await anonimo.PutAsync("/api/solicitacoes/1/sem-reparo", null);
        Assert.Equal(HttpStatusCode.Unauthorized, semToken.StatusCode);

        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        using var inexistente = await admin.PutAsync("/api/solicitacoes/999999/sem-reparo", null);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
    }

    // ---------- US-11 ----------

    [Fact]
    public async Task Admin_registra_retirada_200_grava_recebedor_data_e_historico()
    {
        var id = Inserir(StatusSolicitacao.AguardandoRetirada);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PutAsync($"/api/solicitacoes/{id}/retirar",
            CptmApiFactory.Json("{ \"identificacaoRecebedor\": \"  CPF 123.456.789-00 - Matrícula CPTM 9988  \" }"));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("RETIRADO", (await LerJson(resposta)).GetProperty("novoStatus").GetString());
        var (s, historico) = Estado(id);
        Assert.Equal("RETIRADO", s.Status);
        Assert.Equal("CPF 123.456.789-00 - Matrícula CPTM 9988", s.IdentificacaoRetirada);
        Assert.NotNull(s.DataRetirada);
        Assert.Equal(DateTimeKind.Utc, s.DataRetirada!.Value.Kind);
        var h = Assert.Single(historico);
        Assert.Equal("AGUARDANDO RETIRADA", h.StatusAnterior);
        Assert.Equal("RETIRADO", h.StatusNovo);
        Assert.Equal("admin123", h.Matricula);
        Assert.DoesNotContain("123.456.789", h.Observacao ?? string.Empty);
    }

    [Theory]
    [InlineData("{ \"identificacaoRecebedor\": \"\" }")]
    [InlineData("{ \"identificacaoRecebedor\": \"    \" }")]
    [InlineData("{}")]
    public async Task Retirada_sem_identificacao_400_e_nao_altera(string corpo)
    {
        var id = Inserir(StatusSolicitacao.AguardandoRetirada);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PutAsync($"/api/solicitacoes/{id}/retirar", CptmApiFactory.Json(corpo));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var (s, historico) = Estado(id);
        Assert.Equal("AGUARDANDO RETIRADA", s.Status);
        Assert.Null(s.IdentificacaoRetirada);
        Assert.Empty(historico);
    }

    [Theory]
    [InlineData("SEM REPARO")]
    [InlineData("CONCLUIDA")]
    [InlineData("RETIRADO")]
    [InlineData("EM MANUTENCAO")]
    public async Task Retirada_so_de_aguardando_retirada_400(string statusAtual)
    {
        var id = Inserir(statusAtual);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PutAsync($"/api/solicitacoes/{id}/retirar",
            CptmApiFactory.Json("{ \"identificacaoRecebedor\": \"9988\" }"));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal(statusAtual, Estado(id).Solicitacao.Status);
    }

    [Theory]
    [InlineData("tec123")]
    [InlineData("comum123")]
    public async Task Retirada_so_para_admin_403(string matricula)
    {
        var id = Inserir(StatusSolicitacao.AguardandoRetirada);
        using var cliente = await _api.CriarClienteLogadoAsync(matricula);

        using var resposta = await cliente.PutAsync($"/api/solicitacoes/{id}/retirar",
            CptmApiFactory.Json("{ \"identificacaoRecebedor\": \"9988\" }"));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("AGUARDANDO RETIRADA", Estado(id).Solicitacao.Status);
    }

    // ---------- Fluxo completo ----------

    [Fact]
    public async Task Fluxo_concluida_aguardando_retirada_retirado_e_sem_reparo_nao_pode_ser_retirado()
    {
        var feliz = Inserir(StatusSolicitacao.Concluida);
        var condenada = Inserir(StatusSolicitacao.Concluida);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync($"/api/solicitacoes/{feliz}/aguardar-retirada", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync($"/api/solicitacoes/{feliz}/retirar",
            CptmApiFactory.Json("{ \"identificacaoRecebedor\": \"9988\" }"))).StatusCode);
        // retirar de novo: já RETIRADO
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/solicitacoes/{feliz}/retirar",
            CptmApiFactory.Json("{ \"identificacaoRecebedor\": \"9988\" }"))).StatusCode);
        Assert.Equal(2, Estado(feliz).Historico.Count);

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync($"/api/solicitacoes/{condenada}/sem-reparo", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/solicitacoes/{condenada}/retirar",
            CptmApiFactory.Json("{ \"identificacaoRecebedor\": \"9988\" }"))).StatusCode);
        Assert.Equal("SEM REPARO", Estado(condenada).Solicitacao.Status);
    }

    [Fact]
    public void Novos_status_estao_na_lista_de_status_validos()
    {
        Assert.Contains(StatusSolicitacao.AguardandoRetirada, StatusSolicitacao.Todos);
        Assert.Contains(StatusSolicitacao.SemReparo, StatusSolicitacao.Todos);
        Assert.Contains(StatusSolicitacao.Retirado, StatusSolicitacao.Todos);
    }
}

