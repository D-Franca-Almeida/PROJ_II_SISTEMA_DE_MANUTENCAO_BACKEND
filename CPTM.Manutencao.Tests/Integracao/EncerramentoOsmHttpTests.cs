using System.Net;
using System.Text.Json;
using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CPTM.Manutencao.Tests.Integracao;

public sealed class EncerramentoOsmHttpTests : IClassFixture<CptmApiFactory>
{
    private readonly CptmApiFactory _api;

    public EncerramentoOsmHttpTests(CptmApiFactory api)
    {
        _api = api;
    }

    private int Inserir(string status, string? tecnicoDaOsm = "tec123")
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
            MatriculaTecnico = tecnicoDaOsm
        };
        if (tecnicoDaOsm is not null)
            s.OrdemServico = new OrdemServico { MatriculaTecnico = tecnicoDaOsm, DataAbertura = DateTime.UtcNow.AddHours(-3) };
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

    private (Solicitacao Solicitacao, OrdemServico? Ordem, List<HistoricoStatus> Historico) Estado(int id)
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        return (db.Solicitacoes.AsNoTracking().Single(x => x.Id == id),
            db.OrdensServico.AsNoTracking().SingleOrDefault(o => o.SolicitacaoId == id),
            db.HistoricoStatus.AsNoTracking().Where(h => h.SolicitacaoId == id).OrderBy(h => h.Id).ToList());
    }

    private static async Task<JsonElement> LerJson(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task Dono_conclui_200_com_laudo_e_vai_para_concluida()
    {
        var id = Inserir(StatusSolicitacao.EmManutencao);
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        using var resposta = await tecnico.PutAsync($"/api/solicitacoes/{id}/concluir",
            CptmApiFactory.Json("{ \"laudo\": \"Fonte substituída; equipamento testado por 30 min.\" }"));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await LerJson(resposta);
        var (s, ordem, historico) = Estado(id);
        Assert.Equal("Manutenção concluída. OSM encerrada com laudo.", corpo.GetProperty("mensagem").GetString());
        Assert.Equal(id, corpo.GetProperty("solicitacaoId").GetInt32());
        Assert.Equal(ordem!.Id, corpo.GetProperty("ordemServicoId").GetInt32());
        Assert.Equal("tec123", corpo.GetProperty("tecnicoResponsavel").GetString());
        Assert.EndsWith("Z", corpo.GetProperty("dataConclusao").GetString());
        Assert.Equal("Fonte substituída; equipamento testado por 30 min.", corpo.GetProperty("laudo").GetString());
        Assert.Equal(JsonValueKind.Null, corpo.GetProperty("motivoNaoExecucao").ValueKind);
        Assert.Equal("CONCLUIDA", corpo.GetProperty("novoStatus").GetString());

        Assert.Equal("CONCLUIDA", s.Status);
        Assert.Equal("Fonte substituída; equipamento testado por 30 min.", ordem.Laudo);
        Assert.NotNull(ordem.DataConclusao);
        var h = Assert.Single(historico);
        Assert.Equal("EM MANUTENCAO", h.StatusAnterior);
        Assert.Equal("CONCLUIDA", h.StatusNovo);
        Assert.Equal("tec123", h.Matricula);
    }

    [Fact]
    public async Task Dono_nao_executa_200_com_motivo_e_vai_para_nao_executada()
    {
        var id = Inserir(StatusSolicitacao.EmManutencao);
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        using var resposta = await tecnico.PutAsync($"/api/solicitacoes/{id}/nao-executar",
            CptmApiFactory.Json("{ \"motivo\": \"Equipamento obsoleto, sem peça no mercado.\" }"));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await LerJson(resposta);
        Assert.Equal("Manutenção registrada como não executada. OSM encerrada.", corpo.GetProperty("mensagem").GetString());
        Assert.Equal("NAO EXECUTADA", corpo.GetProperty("novoStatus").GetString());
        Assert.Equal("Equipamento obsoleto, sem peça no mercado.", corpo.GetProperty("motivoNaoExecucao").GetString());
        Assert.Equal(JsonValueKind.Null, corpo.GetProperty("laudo").ValueKind);
        Assert.EndsWith("Z", corpo.GetProperty("dataConclusao").GetString());
        var (s, ordem, historico) = Estado(id);
        Assert.Equal("NAO EXECUTADA", s.Status);
        Assert.Equal("Equipamento obsoleto, sem peça no mercado.", ordem!.MotivoNaoExecucao);
        Assert.NotNull(ordem.DataConclusao);
        Assert.Equal("Equipamento obsoleto, sem peça no mercado.", Assert.Single(historico).Observacao);
    }

    [Theory]
    [InlineData("concluir", "{ \"laudo\": \"   \" }", "O laudo técnico é obrigatório para concluir a OSM.")]
    [InlineData("concluir", "{ }", "O laudo técnico é obrigatório para concluir a OSM.")]
    [InlineData("concluir", null, "O laudo técnico é obrigatório para concluir a OSM.")]
    [InlineData("nao-executar", "{ \"motivo\": \"\" }", "O motivo da não execução é obrigatório.")]
    [InlineData("nao-executar", null, "O motivo da não execução é obrigatório.")]
    public async Task Sem_laudo_ou_motivo_400_sem_gravar(string acao, string? corpo, string erro)
    {
        var id = Inserir(StatusSolicitacao.EmManutencao);
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        using var resposta = await tecnico.PutAsync($"/api/solicitacoes/{id}/{acao}", corpo is null ? null : CptmApiFactory.Json(corpo));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal(erro, (await LerJson(resposta)).GetProperty("erro").GetString());
        var (s, ordem, historico) = Estado(id);
        Assert.Equal("EM MANUTENCAO", s.Status);
        Assert.Null(ordem!.DataConclusao);
        Assert.Empty(historico);
    }

    [Fact]
    public async Task Laudo_acima_de_2000_caracteres_400()
    {
        var id = Inserir(StatusSolicitacao.EmManutencao);
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        using var resposta = await tecnico.PutAsync($"/api/solicitacoes/{id}/concluir",
            CptmApiFactory.Json(JsonSerializer.Serialize(new { laudo = new string('x', 2001) })));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("O laudo deve ter no máximo 2000 caracteres.", (await LerJson(resposta)).GetProperty("erro").GetString());
    }

    [Theory]
    [InlineData("SOLICITADA")]
    [InlineData("APROVADA")]
    [InlineData("RECEBIDA")]
    [InlineData("CANCELADA")]
    public async Task Status_sem_osm_400(string status)
    {
        var id = Inserir(status, tecnicoDaOsm: null);
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        using var concluir = await tecnico.PutAsync($"/api/solicitacoes/{id}/concluir", CptmApiFactory.Json("{ \"laudo\": \"ok\" }"));
        using var naoExecutar = await tecnico.PutAsync($"/api/solicitacoes/{id}/nao-executar", CptmApiFactory.Json("{ \"motivo\": \"x\" }"));

        Assert.Equal(HttpStatusCode.BadRequest, concluir.StatusCode);
        Assert.Equal($"Transição inválida: só é possível concluir uma solicitação EM MANUTENCAO (status atual: {status}).",
            (await LerJson(concluir)).GetProperty("erro").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, naoExecutar.StatusCode);
        Assert.Equal(status, Estado(id).Solicitacao.Status);
    }

    [Theory]
    [InlineData("concluir", "{ \"laudo\": \"de novo\" }")]
    [InlineData("nao-executar", "{ \"motivo\": \"de novo\" }")]
    public async Task Encerrar_osm_ja_encerrada_400(string acao, string corpo)
    {
        var id = Inserir(StatusSolicitacao.EmManutencao);
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");
        using var primeira = await tecnico.PutAsync($"/api/solicitacoes/{id}/concluir", CptmApiFactory.Json("{ \"laudo\": \"ok\" }"));
        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);

        using var segunda = await tecnico.PutAsync($"/api/solicitacoes/{id}/{acao}", CptmApiFactory.Json(corpo));

        Assert.Equal(HttpStatusCode.BadRequest, segunda.StatusCode);
        Assert.Contains("(status atual: CONCLUIDA)", (await LerJson(segunda)).GetProperty("erro").GetString());
        var (_, ordem, historico) = Estado(id);
        Assert.Equal("ok", ordem!.Laudo);
        Assert.Single(historico);
    }

    [Theory]
    [InlineData("concluir", "{ \"laudo\": \"invasor\" }")]
    [InlineData("nao-executar", "{ \"motivo\": \"invasor\" }")]
    public async Task Outro_tecnico_403_sem_gravar(string acao, string corpo)
    {
        GarantirSegundoTecnico();
        var id = Inserir(StatusSolicitacao.EmManutencao, "tec123");
        using var outro = await _api.CriarClienteLogadoAsync("tec456");

        using var resposta = await outro.PutAsync($"/api/solicitacoes/{id}/{acao}", CptmApiFactory.Json(corpo));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        var (s, ordem, historico) = Estado(id);
        Assert.Equal("EM MANUTENCAO", s.Status);
        Assert.Null(ordem!.DataConclusao);
        Assert.Empty(historico);
    }

    [Theory]
    [InlineData("admin123", "concluir")]
    [InlineData("comum123", "concluir")]
    [InlineData("admin123", "nao-executar")]
    [InlineData("comum123", "nao-executar")]
    public async Task Perfil_nao_tecnico_403(string matricula, string acao)
    {
        var id = Inserir(StatusSolicitacao.EmManutencao);
        using var cliente = await _api.CriarClienteLogadoAsync(matricula);

        using var resposta = await cliente.PutAsync($"/api/solicitacoes/{id}/{acao}",
            CptmApiFactory.Json("{ \"laudo\": \"x\", \"motivo\": \"x\" }"));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("EM MANUTENCAO", Estado(id).Solicitacao.Status);
    }

    [Theory]
    [InlineData("concluir")]
    [InlineData("nao-executar")]
    public async Task Sem_token_401(string acao)
    {
        using var anonimo = _api.CriarCliente();
        using var resposta = await anonimo.PutAsync($"/api/solicitacoes/10/{acao}", CptmApiFactory.Json("{ \"laudo\": \"x\" }"));
        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Theory]
    [InlineData("concluir", "{ \"laudo\": \"x\" }")]
    [InlineData("nao-executar", "{ \"motivo\": \"x\" }")]
    public async Task Inexistente_404(string acao, string corpo)
    {
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");
        using var resposta = await tecnico.PutAsync($"/api/solicitacoes/9999/{acao}", CptmApiFactory.Json(corpo));
        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.Equal("Solicitação 9999 não encontrada.", (await LerJson(resposta)).GetProperty("erro").GetString());
    }

    [Fact]
    public async Task Concluir_e_nao_executar_simultaneos_so_um_vence()
    {
        var id = Inserir(StatusSolicitacao.EmManutencao);
        var token = await _api.LoginAsync("tec123");
        var clientes = Enumerable.Range(0, 6).Select(_ => _api.CriarCliente(token)).ToList();

        var respostas = await Task.WhenAll(clientes.Select((c, i) => i % 2 == 0
            ? c.PutAsync($"/api/solicitacoes/{id}/concluir", CptmApiFactory.Json("{ \"laudo\": \"ok\" }"))
            : c.PutAsync($"/api/solicitacoes/{id}/nao-executar", CptmApiFactory.Json("{ \"motivo\": \"x\" }"))));

        Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(respostas.Where(r => r.StatusCode != HttpStatusCode.OK),
            r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict }));
        var (s, ordem, historico) = Estado(id);
        Assert.Contains(s.Status, new[] { "CONCLUIDA", "NAO EXECUTADA" });
        Assert.True(ordem!.Laudo is null ^ ordem.MotivoNaoExecucao is null);
        Assert.Single(historico);
        foreach (var r in respostas) r.Dispose();
        foreach (var c in clientes) c.Dispose();
    }

    [Fact]
    public async Task Osm_encerrada_aparece_em_minhas_e_no_detalhe_e_no_filtro()
    {
        var id = Inserir(StatusSolicitacao.EmManutencao);
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");
        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        using var r = await tecnico.PutAsync($"/api/solicitacoes/{id}/nao-executar", CptmApiFactory.Json("{ \"motivo\": \"Sem peça\" }"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        using var minhas = await tecnico.GetAsync("/api/ordens-servico/minhas");
        var osm = (await LerJson(minhas)).EnumerateArray().Single(o => o.GetProperty("solicitacaoId").GetInt32() == id);
        Assert.Equal("NAO EXECUTADA", osm.GetProperty("statusSolicitacao").GetString());
        Assert.Equal("Sem peça", osm.GetProperty("motivoNaoExecucao").GetString());
        Assert.EndsWith("Z", osm.GetProperty("dataConclusao").GetString());

        using var filtro = await admin.GetAsync("/api/solicitacoes?status=NAO%20EXECUTADA");
        Assert.Equal(HttpStatusCode.OK, filtro.StatusCode);
        Assert.Contains((await LerJson(filtro)).EnumerateArray(), x => x.GetProperty("id").GetInt32() == id);

        using var detalhe = await admin.GetAsync($"/api/solicitacoes/{id}");
        Assert.Equal("NAO EXECUTADA", (await LerJson(detalhe)).GetProperty("status").GetString());
    }
}
