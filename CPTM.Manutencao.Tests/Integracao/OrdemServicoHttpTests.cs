using System.Net;
using System.Text.Json;
using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CPTM.Manutencao.Tests.Integracao;

public sealed class OrdemServicoHttpTests : IClassFixture<CptmApiFactory>
{
    private readonly CptmApiFactory _api;
    private static int _sequencialSsm = 500000;

    public OrdemServicoHttpTests(CptmApiFactory api)
    {
        _api = api;
    }

    private int Inserir(string status, DateTime? dataRecebimento = null)
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        var recebida = status == StatusSolicitacao.Recebida;
        var s = new Solicitacao
        {
            Descricao = "SSL de teste",
            Status = status,
            MatriculaSolicitante = "comum123",
            EquipamentoId = 1,
            DataAbertura = DateTime.UtcNow.AddDays(-2),
            NumeroSsm = recebida ? $"SSM-2000-{Interlocked.Increment(ref _sequencialSsm):D6}" : null,
            DataRecebimento = recebida ? dataRecebimento ?? DateTime.UtcNow.AddHours(-1) : null
        };
        db.Solicitacoes.Add(s);
        db.SaveChanges();
        return s.Id;
    }

    private void GarantirSegundoTecnico()
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        if (db.Usuarios.Any(u => u.Matricula == "tec456")) return;
        var hash = db.Usuarios.AsNoTracking().Single(u => u.Matricula == "tec123").SenhaHash;
        db.Usuarios.Add(new Usuario { Matricula = "tec456", Nome = "Técnico 2", SenhaHash = hash, Perfil = "Tecnico" });
        db.SaveChanges();
    }

    private (Solicitacao Solicitacao, List<OrdemServico> Ordens, List<HistoricoStatus> Historico) Estado(int id)
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        return (db.Solicitacoes.AsNoTracking().Single(x => x.Id == id),
            db.OrdensServico.AsNoTracking().Where(o => o.SolicitacaoId == id).ToList(),
            db.HistoricoStatus.AsNoTracking().Where(h => h.SolicitacaoId == id).OrderBy(h => h.Id).ToList());
    }

    private static async Task<JsonElement> LerJson(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task Tecnico_assume_recebida_200_abre_osm_e_vai_para_em_manutencao()
    {
        var id = Inserir(StatusSolicitacao.Recebida);
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        using var resposta = await tecnico.PutAsync($"/api/solicitacoes/{id}/iniciar-manutencao", null);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await LerJson(resposta);
        var (s, ordens, historico) = Estado(id);
        var ordem = Assert.Single(ordens);
        Assert.Equal("Manutenção iniciada com sucesso. Ordem de Serviço (OSM) aberta.", corpo.GetProperty("mensagem").GetString());
        Assert.Equal(id, corpo.GetProperty("solicitacaoId").GetInt32());
        Assert.Equal(ordem.Id, corpo.GetProperty("ordemServicoId").GetInt32());
        Assert.Equal(s.NumeroSsm, corpo.GetProperty("numeroSsm").GetString());
        Assert.Equal("tec123", corpo.GetProperty("tecnicoResponsavel").GetString());
        Assert.EndsWith("Z", corpo.GetProperty("dataAbertura").GetString());
        Assert.Equal("EM MANUTENCAO", corpo.GetProperty("novoStatus").GetString());

        Assert.Equal("EM MANUTENCAO", s.Status);
        Assert.Equal("tec123", s.MatriculaTecnico);
        Assert.Equal("tec123", ordem.MatriculaTecnico);
        var h = Assert.Single(historico);
        Assert.Equal("RECEBIDA", h.StatusAnterior);
        Assert.Equal("EM MANUTENCAO", h.StatusNovo);
        Assert.Equal("tec123", h.Matricula);
    }

    [Fact]
    public async Task Aprovada_sem_recepcao_agora_e_400()
    {
        var id = Inserir(StatusSolicitacao.Aprovada);
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        using var resposta = await tecnico.PutAsync($"/api/solicitacoes/{id}/iniciar-manutencao", null);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("Transição inválida: só é possível iniciar a manutenção de uma solicitação RECEBIDA na oficina (status atual: APROVADA).",
            (await LerJson(resposta)).GetProperty("erro").GetString());
        var (s, ordens, historico) = Estado(id);
        Assert.Equal("APROVADA", s.Status);
        Assert.Empty(ordens);
        Assert.Empty(historico);
    }

    [Theory]
    [InlineData("SOLICITADA")]
    [InlineData("DEVOLVIDA")]
    [InlineData("REENVIADA")]
    [InlineData("EM MANUTENCAO")]
    [InlineData("CONCLUIDA")]
    [InlineData("CANCELADA")]
    [InlineData("EXPIRADA")]
    public async Task Outros_status_400_sem_osm(string status)
    {
        var id = Inserir(status);
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        using var resposta = await tecnico.PutAsync($"/api/solicitacoes/{id}/iniciar-manutencao", null);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Contains($"status atual: {status}", (await LerJson(resposta)).GetProperty("erro").GetString());
        Assert.Empty(Estado(id).Ordens);
    }

    [Fact]
    public async Task Assumir_de_novo_400_e_osm_continua_unica()
    {
        var id = Inserir(StatusSolicitacao.Recebida);
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");
        using var primeira = await tecnico.PutAsync($"/api/solicitacoes/{id}/iniciar-manutencao", null);
        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);

        using var segunda = await tecnico.PutAsync($"/api/solicitacoes/{id}/iniciar-manutencao", null);

        Assert.Equal(HttpStatusCode.BadRequest, segunda.StatusCode);
        var (_, ordens, historico) = Estado(id);
        Assert.Single(ordens);
        Assert.Single(historico);
    }

    [Fact]
    public async Task Dois_tecnicos_assumindo_ao_mesmo_tempo_so_um_vence()
    {
        GarantirSegundoTecnico();
        var id = Inserir(StatusSolicitacao.Recebida);
        var tokens = new[] { await _api.LoginAsync("tec123"), await _api.LoginAsync("tec456") };
        var clientes = Enumerable.Range(0, 6).Select(i => _api.CriarCliente(tokens[i % 2])).ToList();

        var respostas = await Task.WhenAll(clientes.Select(c => c.PutAsync($"/api/solicitacoes/{id}/iniciar-manutencao", null)));

        var vencedora = Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.OK);
        foreach (var perdedora in respostas.Where(r => r != vencedora))
        {
            Assert.Contains(perdedora.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict });
            Assert.False(string.IsNullOrEmpty((await LerJson(perdedora)).GetProperty("erro").GetString()));
        }
        var tecnicoVencedor = (await LerJson(vencedora)).GetProperty("tecnicoResponsavel").GetString();
        var (s, ordens, historico) = Estado(id);
        Assert.Equal("EM MANUTENCAO", s.Status);
        Assert.Equal(tecnicoVencedor, Assert.Single(ordens).MatriculaTecnico);
        Assert.Equal(tecnicoVencedor, s.MatriculaTecnico);
        Assert.Single(historico);
        foreach (var r in respostas) r.Dispose();
        foreach (var c in clientes) c.Dispose();
    }

    [Theory]
    [InlineData("admin123")]
    [InlineData("comum123")]
    public async Task Iniciar_perfil_nao_tecnico_403(string matricula)
    {
        var id = Inserir(StatusSolicitacao.Recebida);
        using var cliente = await _api.CriarClienteLogadoAsync(matricula);

        using var resposta = await cliente.PutAsync($"/api/solicitacoes/{id}/iniciar-manutencao", null);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("RECEBIDA", Estado(id).Solicitacao.Status);
    }

    [Fact]
    public async Task Iniciar_sem_token_401()
    {
        using var anonimo = _api.CriarCliente();
        using var resposta = await anonimo.PutAsync("/api/solicitacoes/1/iniciar-manutencao", null);
        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Iniciar_inexistente_404()
    {
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");
        using var resposta = await tecnico.PutAsync("/api/solicitacoes/9999/iniciar-manutencao", null);
        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.Equal("Solicitação 9999 não encontrada.", (await LerJson(resposta)).GetProperty("erro").GetString());
    }

    [Fact]
    public async Task Fluxo_receber_e_assumir_pela_api()
    {
        var id = Inserir(StatusSolicitacao.Aprovada);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        using var recebida = await admin.PutAsync($"/api/solicitacoes/{id}/receber", null);
        var numero = (await LerJson(recebida)).GetProperty("numeroSsm").GetString();
        using var iniciada = await tecnico.PutAsync($"/api/solicitacoes/{id}/iniciar-manutencao", null);

        Assert.Equal(HttpStatusCode.OK, iniciada.StatusCode);
        var corpo = await LerJson(iniciada);
        Assert.Equal(numero, corpo.GetProperty("numeroSsm").GetString());
        using var detalhe = await tecnico.GetAsync($"/api/solicitacoes/{id}");
        var d = await LerJson(detalhe);
        Assert.Equal(corpo.GetProperty("ordemServicoId").GetInt32(), d.GetProperty("ordemServicoId").GetInt32());
        Assert.Equal("tec123", d.GetProperty("matriculaTecnico").GetString());
        Assert.Equal(numero, d.GetProperty("numeroSsm").GetString());
    }

    [Fact]
    public async Task Fila_recebidas_tecnico_200_so_recebidas_mais_antigas_primeiro()
    {
        var recente = Inserir(StatusSolicitacao.Recebida, DateTime.UtcNow.AddMinutes(-5));
        var antiga = Inserir(StatusSolicitacao.Recebida, DateTime.UtcNow.AddDays(-5));
        var aprovada = Inserir(StatusSolicitacao.Aprovada);
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        using var resposta = await tecnico.GetAsync("/api/solicitacoes/recebidas");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var itens = (await LerJson(resposta)).EnumerateArray().ToList();
        Assert.All(itens, i => Assert.Equal("RECEBIDA", i.GetProperty("status").GetString()));
        var ids = itens.Select(i => i.GetProperty("id").GetInt32()).ToList();
        Assert.True(ids.IndexOf(antiga) < ids.IndexOf(recente));
        Assert.DoesNotContain(aprovada, ids);
        var item = itens.Single(i => i.GetProperty("id").GetInt32() == antiga);
        Assert.StartsWith("SSM-", item.GetProperty("numeroSsm").GetString());
        Assert.EndsWith("Z", item.GetProperty("dataRecebimento").GetString());
        Assert.Equal(1, item.GetProperty("equipamento").GetProperty("id").GetInt32());
        Assert.False(string.IsNullOrEmpty(item.GetProperty("estacao").GetProperty("nome").GetString()));

        using var assumida = await tecnico.PutAsync($"/api/solicitacoes/{antiga}/iniciar-manutencao", null);
        using var depois = await tecnico.GetAsync("/api/solicitacoes/recebidas");
        Assert.DoesNotContain((await LerJson(depois)).EnumerateArray(), i => i.GetProperty("id").GetInt32() == antiga);
    }

    [Theory]
    [InlineData("admin123")]
    [InlineData("comum123")]
    public async Task Fila_recebidas_perfil_nao_tecnico_403(string matricula)
    {
        using var cliente = await _api.CriarClienteLogadoAsync(matricula);
        using var resposta = await cliente.GetAsync("/api/solicitacoes/recebidas");
        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task Fila_recebidas_sem_token_401()
    {
        using var anonimo = _api.CriarCliente();
        using var resposta = await anonimo.GetAsync("/api/solicitacoes/recebidas");
        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Minhas_osms_traz_so_as_do_tecnico_logado_com_resumo()
    {
        GarantirSegundoTecnico();
        var minha = Inserir(StatusSolicitacao.Recebida);
        var deOutro = Inserir(StatusSolicitacao.Recebida);
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");
        using var outro = await _api.CriarClienteLogadoAsync("tec456");
        using var r1 = await tecnico.PutAsync($"/api/solicitacoes/{minha}/iniciar-manutencao", null);
        using var r2 = await outro.PutAsync($"/api/solicitacoes/{deOutro}/iniciar-manutencao", null);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

        using var resposta = await tecnico.GetAsync("/api/ordens-servico/minhas");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var itens = (await LerJson(resposta)).EnumerateArray().ToList();
        Assert.All(itens, i => Assert.Equal("tec123", i.GetProperty("matriculaTecnico").GetString()));
        var solicitacoes = itens.Select(i => i.GetProperty("solicitacaoId").GetInt32()).ToList();
        Assert.Contains(minha, solicitacoes);
        Assert.Contains(10, solicitacoes);
        Assert.Contains(11, solicitacoes);
        Assert.DoesNotContain(deOutro, solicitacoes);
        var datas = itens.Select(i => i.GetProperty("dataAbertura").GetDateTime()).ToList();
        Assert.Equal(datas.OrderByDescending(d => d), datas);

        var item = itens.Single(i => i.GetProperty("solicitacaoId").GetInt32() == minha);
        Assert.True(item.GetProperty("id").GetInt32() > 0);
        Assert.Equal("EM MANUTENCAO", item.GetProperty("statusSolicitacao").GetString());
        Assert.StartsWith("SSM-", item.GetProperty("numeroSsm").GetString());
        Assert.Equal("SSL de teste", item.GetProperty("descricao").GetString());
        Assert.Equal(1, item.GetProperty("equipamento").GetProperty("id").GetInt32());
        Assert.False(string.IsNullOrEmpty(item.GetProperty("estacao").GetProperty("nome").GetString()));
        Assert.EndsWith("Z", item.GetProperty("dataAbertura").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("dataConclusao").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("laudo").ValueKind);

        using var doOutro = await outro.GetAsync("/api/ordens-servico/minhas");
        var doOutroIds = (await LerJson(doOutro)).EnumerateArray().Select(i => i.GetProperty("solicitacaoId").GetInt32()).ToList();
        Assert.Contains(deOutro, doOutroIds);
        Assert.DoesNotContain(minha, doOutroIds);
    }

    [Theory]
    [InlineData("admin123")]
    [InlineData("comum123")]
    public async Task Minhas_osms_perfil_nao_tecnico_403(string matricula)
    {
        using var cliente = await _api.CriarClienteLogadoAsync(matricula);
        using var resposta = await cliente.GetAsync("/api/ordens-servico/minhas");
        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task Minhas_osms_sem_token_401()
    {
        using var anonimo = _api.CriarCliente();
        using var resposta = await anonimo.GetAsync("/api/ordens-servico/minhas");
        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }
}
