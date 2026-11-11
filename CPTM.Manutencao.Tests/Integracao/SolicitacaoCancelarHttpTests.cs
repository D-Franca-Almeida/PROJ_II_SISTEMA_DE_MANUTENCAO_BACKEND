using System.Net;
using System.Text.Json;
using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CPTM.Manutencao.Tests.Integracao;

public sealed class SolicitacaoCancelarHttpTests : IClassFixture<CptmApiFactory>
{
    private readonly CptmApiFactory _api;

    public SolicitacaoCancelarHttpTests(CptmApiFactory api)
    {
        _api = api;
    }

    private int Inserir(string status, string dono = "comum123")
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        var s = new Solicitacao
        {
            Descricao = "SSL de teste",
            Status = status,
            MatriculaSolicitante = dono,
            EquipamentoId = 1,
            DataAbertura = DateTime.UtcNow
        };
        db.Solicitacoes.Add(s);
        db.SaveChanges();
        return s.Id;
    }

    private (string Status, List<HistoricoStatus> Historico) Estado(int id)
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        return (db.Solicitacoes.AsNoTracking().Single(x => x.Id == id).Status,
            db.HistoricoStatus.AsNoTracking().Where(h => h.SolicitacaoId == id).ToList());
    }

    [Theory]
    [InlineData("SOLICITADA")]
    [InlineData("DEVOLVIDA")]
    [InlineData("REENVIADA")]
    [InlineData("APROVADA")]
    public async Task Comum_dono_cancela_nos_status_permitidos_incluindo_aprovada(string status)
    {
        var id = Inserir(status);
        using var comum = await _api.CriarClienteLogadoAsync("comum123");

        using var resposta = await comum.PutAsync($"/api/solicitacoes/{id}/cancelar", null);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("Solicitação cancelada.",
            JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString());
        var (novo, historico) = Estado(id);
        Assert.Equal("CANCELADA", novo);
        var linha = Assert.Single(historico);
        Assert.Equal(status, linha.StatusAnterior);
        Assert.Equal("CANCELADA", linha.StatusNovo);
        Assert.Equal("comum123", linha.Matricula);
    }

    [Fact]
    public async Task Admin_cancela_aprovada_de_qualquer_dono()
    {
        var id = Inserir("APROVADA", "outro456");
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PutAsync($"/api/solicitacoes/{id}/cancelar", null);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("CANCELADA", Estado(id).Status);
    }

    [Fact]
    public async Task Aprovada_cancelada_nao_pode_ser_iniciada_pelo_tecnico()
    {
        var id = Inserir("APROVADA");
        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");
        using var cancelada = await comum.PutAsync($"/api/solicitacoes/{id}/cancelar", null);
        Assert.Equal(HttpStatusCode.OK, cancelada.StatusCode);

        using var iniciar = await tecnico.PutAsync($"/api/solicitacoes/{id}/iniciar-manutencao", null);

        Assert.Equal(HttpStatusCode.BadRequest, iniciar.StatusCode);
    }

    [Theory]
    [InlineData("EM MANUTENCAO")]
    [InlineData("CONCLUIDA")]
    [InlineData("CANCELADA")]
    public async Task Status_apos_aprovada_400_para_comum_e_admin_sem_historico(string status)
    {
        var id = Inserir(status);
        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        foreach (var cliente in new[] { comum, admin })
        {
            using var resposta = await cliente.PutAsync($"/api/solicitacoes/{id}/cancelar", null);
            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
            Assert.Equal($"Não é possível cancelar uma solicitação com status {status}.",
                JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.GetProperty("erro").GetString());
        }
        var (atual, historico) = Estado(id);
        Assert.Equal(status, atual);
        Assert.Empty(historico);
    }

    [Fact]
    public async Task Comum_nao_dono_403_mesmo_em_aprovada()
    {
        var id = Inserir("APROVADA", "outro456");
        using var comum = await _api.CriarClienteLogadoAsync("comum123");

        using var resposta = await comum.PutAsync($"/api/solicitacoes/{id}/cancelar", null);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("APROVADA", Estado(id).Status);
    }

    [Fact]
    public async Task Tecnico_403_em_aprovada()
    {
        var id = Inserir("APROVADA");
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        using var resposta = await tecnico.PutAsync($"/api/solicitacoes/{id}/cancelar", null);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("APROVADA", Estado(id).Status);
    }

    [Fact]
    public async Task Sem_token_401()
    {
        using var anonimo = _api.CriarCliente();
        using var resposta = await anonimo.PutAsync("/api/solicitacoes/8/cancelar", null);
        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Inexistente_404()
    {
        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        using var resposta = await comum.PutAsync("/api/solicitacoes/9999/cancelar", null);
        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.Equal("Solicitação 9999 não encontrada.",
            JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.GetProperty("erro").GetString());
    }
}
