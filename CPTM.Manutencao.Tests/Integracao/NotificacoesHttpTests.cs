using System.Net;
using System.Text.Json;
using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CPTM.Manutencao.Tests.Integracao;

// CPTM-84 (US-25): GET /api/notificacoes e PUT /api/notificacoes/{id}/lida, e a geração pelas rotas de transição
public sealed class NotificacoesHttpTests : IClassFixture<CptmApiFactory>
{
    private readonly CptmApiFactory _api;

    public NotificacoesHttpTests(CptmApiFactory api)
    {
        _api = api;
    }

    private (int SolicitacaoId, int? OrdemId) Inserir(string status)
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        var s = new Solicitacao
        {
            Descricao = "SSL de teste", Status = status, MatriculaSolicitante = "comum123", EquipamentoId = 1,
            DataAbertura = DateTime.UtcNow.AddDays(-2)
        };
        if (status == StatusSolicitacao.EmManutencao)
        {
            s.MatriculaTecnico = "tec123";
            s.OrdemServico = new OrdemServico { MatriculaTecnico = "tec123", DataAbertura = DateTime.UtcNow.AddHours(-1) };
        }
        db.Solicitacoes.Add(s);
        db.SaveChanges();
        return (s.Id, s.OrdemServico?.Id);
    }

    private int GravarNotificacao(string? matricula, string? perfil)
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        var n = new Notificacao { MatriculaDestino = matricula, PerfilDestino = perfil, SolicitacaoId = 1, Mensagem = "Teste", Data = DateTime.UtcNow };
        db.Notificacoes.Add(n);
        db.SaveChanges();
        return n.Id;
    }

    private List<Notificacao> DaSolicitacao(int solicitacaoId)
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        return db.Notificacoes.AsNoTracking().Where(n => n.SolicitacaoId == solicitacaoId).OrderBy(n => n.Id).ToList();
    }

    private static async Task<JsonElement> LerJson(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<JsonElement> Minhas(HttpClient cliente, string consulta = "")
    {
        using var resposta = await cliente.GetAsync($"/api/notificacoes{consulta}");
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return await LerJson(resposta);
    }

    private static List<string> Mensagens(JsonElement corpo) =>
        corpo.GetProperty("notificacoes").EnumerateArray().Select(n => n.GetProperty("mensagem").GetString()!).ToList();

    private static async Task Ok(Task<HttpResponseMessage> chamada)
    {
        using var resposta = await chamada;
        Assert.True(resposta.IsSuccessStatusCode, await resposta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Sem_token_401()
    {
        using var anonimo = _api.CriarCliente();

        using var listar = await anonimo.GetAsync("/api/notificacoes");
        using var marcar = await anonimo.PutAsync("/api/notificacoes/1/lida", null);

        Assert.Equal(HttpStatusCode.Unauthorized, listar.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, marcar.StatusCode);
    }

    [Theory]
    [InlineData("comum123")]
    [InlineData("admin123")]
    [InlineData("tec123")]
    public async Task Qualquer_perfil_lista_200_com_contador(string matricula)
    {
        using var cliente = await _api.CriarClienteLogadoAsync(matricula);

        var corpo = await Minhas(cliente);

        Assert.True(corpo.GetProperty("naoLidas").GetInt32() >= 0);
        Assert.Equal(JsonValueKind.Array, corpo.GetProperty("notificacoes").ValueKind);
    }

    [Fact]
    public async Task Filtro_invalido_400()
    {
        using var comum = await _api.CriarClienteLogadoAsync("comum123");

        using var resposta = await comum.GetAsync("/api/notificacoes?naoLidas=talvez");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Fluxo_da_ssl_notifica_cada_perfil_so_com_as_suas()
    {
        var (sslId, ordemId) = Inserir(StatusSolicitacao.EmManutencao);
        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        using var criado = await tecnico.PostAsync($"/api/ordens-servico/{ordemId}/pecas",
            CptmApiFactory.Json("{ \"item\": \"Capacitor\", \"quantidade\": 2, \"justificativa\": \"Estufado\" }"));
        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
        var pedidoId = (await LerJson(criado)).GetProperty("id").GetInt32();
        await Ok(admin.PutAsync($"/api/pedidos-peca/{pedidoId}/encaminhar", CptmApiFactory.Json("{ \"observacaoAlmoxarifado\": \"RM-1\" }")));
        await Ok(admin.PutAsync($"/api/pedidos-peca/{pedidoId}/receber", null));
        await Ok(tecnico.PutAsync($"/api/solicitacoes/{sslId}/concluir", CptmApiFactory.Json("{ \"laudo\": \"Trocado\" }")));
        await Ok(admin.PutAsync($"/api/solicitacoes/{sslId}/liberar-retirada", null));
        await Ok(admin.PutAsync($"/api/solicitacoes/{sslId}/retirar", CptmApiFactory.Json("{ \"identificacaoRecebedor\": \"comum123\" }")));

        var doComum = Mensagens(await Minhas(comum));
        var doAdmin = Mensagens(await Minhas(admin));
        var doTecnico = Mensagens(await Minhas(tecnico));
        Assert.Contains($"A manutenção da SSL {sslId} foi concluída. Aguarde a liberação para retirada.", doComum);
        Assert.Contains($"O equipamento da SSL {sslId} está pronto para retirada.", doComum);
        Assert.Contains($"O equipamento da SSL {sslId} foi retirado. Ciclo encerrado.", doComum);
        Assert.Contains($"Novo pedido de peça na OSM {ordemId} (SSL {sslId}): Capacitor x2.", doAdmin);
        Assert.Contains($"A OSM da SSL {sslId} foi concluída. Defina o destino do equipamento.", doAdmin);
        Assert.Contains($"A peça do pedido {pedidoId} chegou: Capacitor x2 (OSM {ordemId}, SSL {sslId}).", doTecnico);
        Assert.DoesNotContain(doComum, m => m.Contains("OSM"));
        Assert.DoesNotContain(doTecnico, m => m.Contains($"Sua SSL") || m.StartsWith("Novo pedido"));
        Assert.Equal(6, DaSolicitacao(sslId).Count);
    }

    [Fact]
    public async Task Lista_mais_novas_primeiro_com_datas_utc()
    {
        var (sslId, _) = Inserir(StatusSolicitacao.Solicitada);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        await Ok(admin.PutAsync($"/api/solicitacoes/{sslId}/aprovar", null));
        await Ok(admin.PutAsync($"/api/solicitacoes/{sslId}/receber", null));

        var corpo = await Minhas(comum);

        var itens = corpo.GetProperty("notificacoes").EnumerateArray().ToList();
        var daSsl = itens.Where(n => n.GetProperty("solicitacaoId").GetInt32() == sslId).ToList();
        Assert.Equal(2, daSsl.Count);
        Assert.StartsWith($"O equipamento da SSL {sslId} foi recebido na oficina (SSM SSM-", daSsl[0].GetProperty("mensagem").GetString());
        Assert.Equal($"Sua SSL {sslId} foi aprovada. Leve o equipamento à oficina.", daSsl[1].GetProperty("mensagem").GetString());
        var datas = itens.Select(n => n.GetProperty("data").GetDateTime()).ToList();
        Assert.Equal(datas.OrderByDescending(d => d), datas);
        Assert.All(itens, n => Assert.EndsWith("Z", n.GetProperty("data").GetString()));
        Assert.False(daSsl[0].GetProperty("lida").GetBoolean());
        Assert.Equal(JsonValueKind.Null, daSsl[0].GetProperty("dataLeitura").ValueKind);
    }

    [Fact]
    public async Task Destinatario_marca_como_lida_200_e_sai_das_nao_lidas()
    {
        var id = GravarNotificacao("comum123", null);
        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        var antes = (await Minhas(comum, "?naoLidas=true")).GetProperty("naoLidas").GetInt32();

        using var resposta = await comum.PutAsync($"/api/notificacoes/{id}/lida", null);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await LerJson(resposta);
        Assert.Equal(id, corpo.GetProperty("id").GetInt32());
        Assert.True(corpo.GetProperty("lida").GetBoolean());
        Assert.EndsWith("Z", corpo.GetProperty("dataLeitura").GetString());
        var depois = await Minhas(comum, "?naoLidas=true");
        Assert.Equal(antes - 1, depois.GetProperty("naoLidas").GetInt32());
        Assert.DoesNotContain(depois.GetProperty("notificacoes").EnumerateArray(), n => n.GetProperty("id").GetInt32() == id);
        Assert.Contains((await Minhas(comum)).GetProperty("notificacoes").EnumerateArray(), n => n.GetProperty("id").GetInt32() == id);
    }

    [Fact]
    public async Task Admin_marca_a_do_perfil_200()
    {
        var id = GravarNotificacao(null, Notificacao.PerfilAdministrativo);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PutAsync($"/api/notificacoes/{id}/lida", null);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.True((await LerJson(resposta)).GetProperty("lida").GetBoolean());
    }

    [Fact]
    public async Task Tecnico_marca_a_sua_200()
    {
        var id = GravarNotificacao("tec123", null);
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        using var resposta = await tecnico.PutAsync($"/api/notificacoes/{id}/lida", null);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [Theory]
    [InlineData("tec123", "comum123", null)]
    [InlineData("admin123", "comum123", null)]
    [InlineData("comum123", null, "Administrativo")]
    [InlineData("tec123", null, "Administrativo")]
    public async Task Quem_nao_e_destinatario_403(string quem, string? matricula, string? perfil)
    {
        var id = GravarNotificacao(matricula, perfil);
        using var cliente = await _api.CriarClienteLogadoAsync(quem);

        using var resposta = await cliente.PutAsync($"/api/notificacoes/{id}/lida", null);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.DoesNotContain((await Minhas(cliente)).GetProperty("notificacoes").EnumerateArray(), n => n.GetProperty("id").GetInt32() == id);
    }

    [Fact]
    public async Task Notificacao_inexistente_404()
    {
        using var comum = await _api.CriarClienteLogadoAsync("comum123");

        using var resposta = await comum.PutAsync("/api/notificacoes/99999/lida", null);

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.Equal("Notificação 99999 não encontrada.", (await LerJson(resposta)).GetProperty("erro").GetString());
    }

    [Fact]
    public async Task Transicao_recusada_nao_gera_notificacao()
    {
        var (sslId, _) = Inserir(StatusSolicitacao.Aprovada);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PutAsync($"/api/solicitacoes/{sslId}/aprovar", null);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Empty(DaSolicitacao(sslId));
    }

    [Fact]
    public async Task Texto_da_retirada_autorizada_nao_tem_o_documento()
    {
        var (sslId, _) = Inserir(StatusSolicitacao.AguardandoRetirada);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        await Ok(admin.PutAsync($"/api/solicitacoes/{sslId}/autorizar-retirada",
            CptmApiFactory.Json("{ \"nome\": \"Maria Souza\", \"documento\": \"123.456.789-00\" }")));
        await Ok(admin.PutAsync($"/api/solicitacoes/{sslId}/retirar", CptmApiFactory.Json("{ \"identificacaoRecebedor\": \"12345678900\" }")));

        var mensagens = DaSolicitacao(sslId).Select(n => n.Mensagem).ToList();
        Assert.Equal(2, mensagens.Count);
        Assert.Contains($"A retirada do equipamento da SSL {sslId} foi autorizada para Maria Souza.", mensagens);
        Assert.DoesNotContain(mensagens, m => m.Contains("123.456.789-00") || m.Contains("12345678900"));
    }
}
