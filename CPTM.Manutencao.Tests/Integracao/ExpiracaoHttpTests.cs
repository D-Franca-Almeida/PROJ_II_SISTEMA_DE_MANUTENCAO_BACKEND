using System.Net;
using System.Text.Json;
using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CPTM.Manutencao.Tests.Integracao;

public sealed class ExpiracaoHttpTests : IClassFixture<CptmApiFactory>
{
    private const string Rota = "/api/admin/expiracao/executar";
    private readonly CptmApiFactory _api;

    public ExpiracaoHttpTests(CptmApiFactory api)
    {
        _api = api;
    }

    private int Inserir(string status, int diasNoStatus)
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        var s = new Solicitacao
        {
            Descricao = "SSL de teste", Status = status, MatriculaSolicitante = "comum123",
            EquipamentoId = 1, DataAbertura = DateTime.UtcNow.AddDays(-diasNoStatus - 1)
        };
        db.Solicitacoes.Add(s);
        db.HistoricoStatus.Add(new HistoricoStatus
        {
            Solicitacao = s, StatusAnterior = "SOLICITADA", StatusNovo = status, Matricula = "admin123",
            Data = DateTime.UtcNow.AddDays(-diasNoStatus)
        });
        db.SaveChanges();
        return s.Id;
    }

    private (string Status, List<HistoricoStatus> Historico) Estado(int id)
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        return (db.Solicitacoes.AsNoTracking().Single(x => x.Id == id).Status,
            db.HistoricoStatus.AsNoTracking().Where(h => h.SolicitacaoId == id).OrderBy(h => h.Id).ToList());
    }

    [Fact]
    public async Task Admin_executa_e_recebe_quantidade_e_ids_expirados()
    {
        var devolvida = Inserir("DEVOLVIDA", 11);
        var aprovada = Inserir("APROVADA", 15);
        var recente = Inserir("DEVOLVIDA", 2);
        var solicitada = Inserir("SOLICITADA", 30);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PostAsync(Rota, null);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        var ids = json.RootElement.GetProperty("solicitacoes").EnumerateArray().Select(x => x.GetProperty("id").GetInt32()).ToList();
        Assert.Contains(devolvida, ids);
        Assert.Contains(aprovada, ids);
        Assert.DoesNotContain(recente, ids);
        Assert.DoesNotContain(solicitada, ids);
        Assert.Equal(ids.Count, json.RootElement.GetProperty("quantidade").GetInt32());

        var (status, historico) = Estado(devolvida);
        Assert.Equal("EXPIRADA", status);
        var h = historico.Last();
        Assert.Equal("SISTEMA", h.Matricula);
        Assert.Equal("DEVOLVIDA", h.StatusAnterior);
        Assert.Contains("admin123", h.Observacao);
        Assert.Equal("DEVOLVIDA", Estado(recente).Status);
        Assert.Equal("SOLICITADA", Estado(solicitada).Status);

        using var segunda = await admin.PostAsync(Rota, null);
        using var json2 = JsonDocument.Parse(await segunda.Content.ReadAsStringAsync());
        Assert.DoesNotContain(devolvida, json2.RootElement.GetProperty("solicitacoes").EnumerateArray().Select(x => x.GetProperty("id").GetInt32()));
    }

    [Theory]
    [InlineData("comum123")]
    [InlineData("tec123")]
    public async Task Outros_perfis_recebem_403(string matricula)
    {
        using var cliente = await _api.CriarClienteLogadoAsync(matricula);

        using var resposta = await cliente.PostAsync(Rota, null);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task Sem_token_recebe_401()
    {
        using var cliente = _api.CriarCliente();

        using var resposta = await cliente.PostAsync(Rota, null);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Listagem_expoe_DiasParaExpirar_e_filtro_EXPIRADA_e_valido()
    {
        var id = Inserir("DEVOLVIDA", 7);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.GetAsync("/api/solicitacoes?status=DEVOLVIDA");

        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        var item = json.RootElement.EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == id);
        Assert.Equal(3, item.GetProperty("diasParaExpirar").GetInt32());

        using var filtro = await admin.GetAsync("/api/solicitacoes?status=EXPIRADA");
        Assert.Equal(HttpStatusCode.OK, filtro.StatusCode);
    }
}
