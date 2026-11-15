using System.Net;
using System.Text.Json;
using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CPTM.Manutencao.Tests.Integracao;

// CPTM-77 (US-10): liberar-retirada (alias de aguardar-retirada), sem-reparo e inutilizar
public sealed class DestinoHttpTests : IClassFixture<CptmApiFactory>
{
    private readonly CptmApiFactory _api;

    public DestinoHttpTests(CptmApiFactory api)
    {
        _api = api;
    }

    private int Inserir(string status, string? observacaoDestino = null)
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
            ObservacaoDestino = observacaoDestino
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
    [InlineData("CONCLUIDA", "liberar-retirada")]
    [InlineData("NAO EXECUTADA", "liberar-retirada")]
    [InlineData("SEM REPARO", "liberar-retirada")]
    [InlineData("NAO EXECUTADA", "aguardar-retirada")]
    [InlineData("SEM REPARO", "aguardar-retirada")]
    public async Task Liberar_retirada_das_origens_da_us10_200(string origem, string rota)
    {
        var id = Inserir(origem);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PutAsync($"/api/solicitacoes/{id}/{rota}",
            CptmApiFactory.Json("{ \"observacao\": \"Solicitante quer o equipamento de volta.\" }"));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("AGUARDANDO RETIRADA", (await LerJson(resposta)).GetProperty("novoStatus").GetString());
        var (s, historico) = Estado(id);
        Assert.Equal("AGUARDANDO RETIRADA", s.Status);
        Assert.Equal("Solicitante quer o equipamento de volta.", s.ObservacaoDestino);
        var h = Assert.Single(historico);
        Assert.Equal(origem, h.StatusAnterior);
        Assert.Equal("admin123", h.Matricula);
    }

    [Theory]
    [InlineData("APROVADA")]
    [InlineData("EM MANUTENCAO")]
    [InlineData("INUTILIZADO")]
    [InlineData("RETIRADO")]
    public async Task Liberar_retirada_de_outro_status_400(string origem)
    {
        var id = Inserir(origem);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PutAsync($"/api/solicitacoes/{id}/liberar-retirada", null);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal(
            $"Transição inválida: a solicitação precisa estar CONCLUIDA ou NAO EXECUTADA ou SEM REPARO para ir para AGUARDANDO RETIRADA (status atual: {origem}).",
            (await LerJson(resposta)).GetProperty("erro").GetString());
        Assert.Equal(origem, Estado(id).Solicitacao.Status);
    }

    [Fact]
    public async Task Admin_inutiliza_sem_reparo_200_mantem_observacao_anterior_e_grava_historico()
    {
        var id = Inserir(StatusSolicitacao.SemReparo, "Placa-mãe queimada.");
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PutAsync($"/api/solicitacoes/{id}/inutilizar", null);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await LerJson(resposta);
        Assert.Equal("INUTILIZADO", corpo.GetProperty("novoStatus").GetString());
        Assert.Equal("Equipamento inutilizado (descarte).", corpo.GetProperty("message").GetString());
        var (s, historico) = Estado(id);
        Assert.Equal("INUTILIZADO", s.Status);
        Assert.Equal("Placa-mãe queimada.", s.ObservacaoDestino);
        var h = Assert.Single(historico);
        Assert.Equal("SEM REPARO", h.StatusAnterior);
        Assert.Equal("INUTILIZADO", h.StatusNovo);
        Assert.Equal("admin123", h.Matricula);
        Assert.Equal("Equipamento descartado.", h.Observacao);
    }

    [Fact]
    public async Task Admin_inutiliza_com_observacao_200_grava_observacao()
    {
        var id = Inserir(StatusSolicitacao.SemReparo, "Placa-mãe queimada.");
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PutAsync($"/api/solicitacoes/{id}/inutilizar",
            CptmApiFactory.Json("{ \"observacao\": \"  Enviado para sucata, lote 12.  \" }"));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var (s, historico) = Estado(id);
        Assert.Equal("Enviado para sucata, lote 12.", s.ObservacaoDestino);
        Assert.Equal("Enviado para sucata, lote 12.", Assert.Single(historico).Observacao);
    }

    [Theory]
    [InlineData("CONCLUIDA")]
    [InlineData("NAO EXECUTADA")]
    [InlineData("AGUARDANDO RETIRADA")]
    [InlineData("INUTILIZADO")]
    public async Task Inutilizar_so_a_partir_de_sem_reparo_400(string origem)
    {
        var id = Inserir(origem);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PutAsync($"/api/solicitacoes/{id}/inutilizar", null);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal($"Transição inválida: só é possível inutilizar um equipamento SEM REPARO (status atual: {origem}).",
            (await LerJson(resposta)).GetProperty("erro").GetString());
        var (s, historico) = Estado(id);
        Assert.Equal(origem, s.Status);
        Assert.Empty(historico);
    }

    [Fact]
    public async Task Inutilizar_observacao_longa_400_e_nao_altera()
    {
        var id = Inserir(StatusSolicitacao.SemReparo);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PutAsync($"/api/solicitacoes/{id}/inutilizar",
            CptmApiFactory.Json($"{{ \"observacao\": \"{new string('x', 501)}\" }}"));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("A observação deve ter no máximo 500 caracteres.", (await LerJson(resposta)).GetProperty("erro").GetString());
        Assert.Equal("SEM REPARO", Estado(id).Solicitacao.Status);
    }

    [Theory]
    [InlineData("tec123", "inutilizar")]
    [InlineData("comum123", "inutilizar")]
    [InlineData("tec123", "liberar-retirada")]
    [InlineData("comum123", "liberar-retirada")]
    public async Task So_admin_403(string matricula, string rota)
    {
        var id = Inserir(StatusSolicitacao.SemReparo);
        using var cliente = await _api.CriarClienteLogadoAsync(matricula);

        using var resposta = await cliente.PutAsync($"/api/solicitacoes/{id}/{rota}", null);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("SEM REPARO", Estado(id).Solicitacao.Status);
    }

    [Theory]
    [InlineData("inutilizar")]
    [InlineData("liberar-retirada")]
    public async Task Sem_token_401_e_inexistente_404(string rota)
    {
        using var anonimo = _api.CriarCliente();
        using var semToken = await anonimo.PutAsync($"/api/solicitacoes/1/{rota}", null);
        Assert.Equal(HttpStatusCode.Unauthorized, semToken.StatusCode);

        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        using var inexistente = await admin.PutAsync($"/api/solicitacoes/999999/{rota}", null);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal("Solicitação 999999 não encontrada.", (await LerJson(inexistente)).GetProperty("erro").GetString());
    }

    [Fact]
    public async Task Ciclo_sem_reparo_volta_para_retirada_e_inutilizado_e_final()
    {
        var volta = Inserir(StatusSolicitacao.Concluida);
        var descarte = Inserir(StatusSolicitacao.Concluida);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync($"/api/solicitacoes/{volta}/sem-reparo", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync($"/api/solicitacoes/{volta}/liberar-retirada", null)).StatusCode);
        Assert.Equal("AGUARDANDO RETIRADA", Estado(volta).Solicitacao.Status);

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync($"/api/solicitacoes/{descarte}/sem-reparo", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync($"/api/solicitacoes/{descarte}/inutilizar", null)).StatusCode);
        foreach (var rota in new[] { "liberar-retirada", "sem-reparo", "inutilizar" })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/solicitacoes/{descarte}/{rota}", null)).StatusCode);
        var (s, historico) = Estado(descarte);
        Assert.Equal("INUTILIZADO", s.Status);
        Assert.Equal(2, historico.Count);
    }

    [Fact]
    public async Task Liberacoes_simultaneas_so_uma_vence()
    {
        var id = Inserir(StatusSolicitacao.Concluida);
        var token = await _api.LoginAsync("admin123");
        var clientes = Enumerable.Range(0, 6).Select(_ => _api.CriarCliente(token)).ToList();

        var respostas = await Task.WhenAll(clientes.Select((c, i) =>
            c.PutAsync($"/api/solicitacoes/{id}/{(i % 2 == 0 ? "liberar-retirada" : "aguardar-retirada")}", null)));

        Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(respostas.Where(r => r.StatusCode != HttpStatusCode.OK),
            r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict }));
        var (s, historico) = Estado(id);
        Assert.Equal("AGUARDANDO RETIRADA", s.Status);
        Assert.Single(historico);
        foreach (var r in respostas) r.Dispose();
        foreach (var c in clientes) c.Dispose();
    }

    [Fact]
    public void Inutilizado_esta_na_lista_de_status_validos()
    {
        Assert.Contains(StatusSolicitacao.Inutilizado, StatusSolicitacao.Todos);
    }
}
