using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CPTM.Manutencao.Tests.Integracao;

public sealed class SolicitacaoConsultaHttpTests : IClassFixture<CptmApiFactory>
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };

    private readonly CptmApiFactory _api;

    public SolicitacaoConsultaHttpTests(CptmApiFactory api)
    {
        _api = api;
    }

    private static async Task<JsonElement> LerJson(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.Clone();

    private static MultipartFormDataContent Form(string descricao, int equipamentoId)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(equipamentoId.ToString()), "equipamentoId" },
            { new StringContent(descricao), "descricao" }
        };
        var arquivo = new ByteArrayContent(Png);
        arquivo.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(arquivo, "foto", "foto.png");
        return form;
    }

    [Fact]
    public async Task Minhas_devolve_so_as_do_solicitante_do_token_mais_novas_primeiro()
    {
        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        using var criada = await comum.PostAsync("/api/solicitacoes", Form("Minha mais recente", 2));
        var idNova = (await LerJson(criada)).GetProperty("id").GetInt32();

        using var resposta = await comum.GetAsync("/api/solicitacoes/minhas");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var itens = (await LerJson(resposta)).EnumerateArray().ToList();
        Assert.All(itens, i => Assert.Equal("comum123", i.GetProperty("matriculaSolicitante").GetString()));
        var ids = itens.Select(i => i.GetProperty("id").GetInt32()).ToList();
        Assert.DoesNotContain(4, ids);
        Assert.Equal(idNova, ids[0]);
        Assert.Equal(ids.OrderByDescending(x => x), ids);
        Assert.Equal(11, ids.Count(x => x <= 12));
        var item = itens[0];
        Assert.Equal("SOLICITADA", item.GetProperty("status").GetString());
        Assert.EndsWith("Z", item.GetProperty("dataAbertura").GetString());
        Assert.StartsWith("/uploads/ssl/", item.GetProperty("fotoUrl").GetString());
        Assert.Equal(2, item.GetProperty("equipamento").GetProperty("id").GetInt32());
        Assert.Equal("Luz", item.GetProperty("estacao").GetProperty("nome").GetString());
    }

    [Fact]
    public async Task Minhas_nao_inclui_ssl_de_outro_solicitante_e_perfis_sem_ssl_recebem_lista_vazia()
    {
        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        foreach (var cliente in new[] { admin, tecnico })
        {
            using var resposta = await cliente.GetAsync("/api/solicitacoes/minhas");
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            Assert.Empty((await LerJson(resposta)).EnumerateArray());
        }
    }

    [Fact]
    public async Task Minhas_sem_token_recebe_401()
    {
        using var anonimo = _api.CriarCliente();
        using var resposta = await anonimo.GetAsync("/api/solicitacoes/minhas");
        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Comum_ve_detalhe_da_propria_com_historico()
    {
        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        using var resposta = await comum.GetAsync("/api/solicitacoes/1");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await LerJson(resposta);
        Assert.Equal(1, corpo.GetProperty("id").GetInt32());
        Assert.Equal("comum123", corpo.GetProperty("matriculaSolicitante").GetString());
        Assert.EndsWith("Z", corpo.GetProperty("dataAbertura").GetString());
        Assert.Equal(1, corpo.GetProperty("equipamento").GetProperty("id").GetInt32());
        Assert.Equal("Luz", corpo.GetProperty("estacao").GetProperty("nome").GetString());
        Assert.Equal(JsonValueKind.Null, corpo.GetProperty("matriculaTecnico").ValueKind);
        var historico = corpo.GetProperty("historico").EnumerateArray().ToList();
        Assert.Single(historico);
        Assert.Equal(JsonValueKind.Null, historico[0].GetProperty("statusAnterior").ValueKind);
        Assert.Equal("SOLICITADA", historico[0].GetProperty("statusNovo").GetString());
        Assert.Equal("comum123", historico[0].GetProperty("matricula").GetString());
        Assert.EndsWith("Z", historico[0].GetProperty("data").GetString());
    }

    [Fact]
    public async Task Comum_recebe_403_no_detalhe_de_ssl_de_outro_solicitante()
    {
        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        using var resposta = await comum.GetAsync("/api/solicitacoes/4");
        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Theory]
    [InlineData("admin123")]
    [InlineData("tec123")]
    public async Task Administrativo_e_tecnico_veem_detalhe_de_qualquer_ssl(string matricula)
    {
        using var cliente = await _api.CriarClienteLogadoAsync(matricula);

        using var alheia = await cliente.GetAsync("/api/solicitacoes/4");
        Assert.Equal(HttpStatusCode.OK, alheia.StatusCode);
        Assert.Equal("outro456", (await LerJson(alheia)).GetProperty("matriculaSolicitante").GetString());

        using var outra = await cliente.GetAsync("/api/solicitacoes/1");
        Assert.Equal(HttpStatusCode.OK, outra.StatusCode);
    }

    [Theory]
    [InlineData("comum123")]
    [InlineData("admin123")]
    [InlineData("tec123")]
    public async Task Detalhe_de_ssl_inexistente_devolve_404(string matricula)
    {
        using var cliente = await _api.CriarClienteLogadoAsync(matricula);
        using var resposta = await cliente.GetAsync("/api/solicitacoes/9999");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.Equal("Solicitação 9999 não encontrada.", (await LerJson(resposta)).GetProperty("erro").GetString());
    }

    [Fact]
    public async Task Detalhe_sem_token_recebe_401()
    {
        using var anonimo = _api.CriarCliente();
        using var resposta = await anonimo.GetAsync("/api/solicitacoes/1");
        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Detalhe_de_id_nao_numerico_devolve_404()
    {
        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        using var resposta = await admin.GetAsync("/api/solicitacoes/abc");
        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Location_do_POST_resolve_para_o_detalhe_da_ssl_criada()
    {
        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        using var criada = await comum.PostAsync("/api/solicitacoes", Form("Seguir o Location", 5));
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        var corpoCriada = await LerJson(criada);

        using var resposta = await comum.GetAsync(criada.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var detalhe = await LerJson(resposta);
        Assert.Equal(corpoCriada.GetProperty("id").GetInt32(), detalhe.GetProperty("id").GetInt32());
        Assert.Equal("Seguir o Location", detalhe.GetProperty("descricao").GetString());
        Assert.Equal(corpoCriada.GetProperty("fotoUrl").GetString(), detalhe.GetProperty("fotoUrl").GetString());
        Assert.Equal(5, detalhe.GetProperty("equipamento").GetProperty("id").GetInt32());
        Assert.Single(detalhe.GetProperty("historico").EnumerateArray());
    }

    [Fact]
    public async Task Detalhe_de_devolvida_traz_motivo_e_observacao_no_historico()
    {
        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        using var devolucao = await admin.PutAsync("/api/Solicitacoes/3/devolver", CptmApiFactory.Json("{\"motivo\":\"Foto desfocada\"}"));
        Assert.Equal(HttpStatusCode.OK, devolucao.StatusCode);

        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        using var resposta = await comum.GetAsync("/api/solicitacoes/3");

        var corpo = await LerJson(resposta);
        Assert.Equal("DEVOLVIDA", corpo.GetProperty("status").GetString());
        Assert.Equal("Foto desfocada", corpo.GetProperty("motivoDevolucao").GetString());
        var historico = corpo.GetProperty("historico").EnumerateArray().ToList();
        Assert.Equal("DEVOLVIDA", historico[^1].GetProperty("statusNovo").GetString());
        Assert.Equal("admin123", historico[^1].GetProperty("matricula").GetString());
        Assert.Equal("Foto desfocada", historico[^1].GetProperty("observacao").GetString());
        Assert.EndsWith("Z", historico[^1].GetProperty("data").GetString());
    }

    [Fact]
    public async Task Detalhe_em_manutencao_traz_tecnico_e_data_inicio_em_utc()
    {
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");
        using var inicio = await tecnico.PutAsync("/api/Solicitacoes/8/iniciar-manutencao", null);
        Assert.Equal(HttpStatusCode.OK, inicio.StatusCode);

        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        using var resposta = await comum.GetAsync("/api/solicitacoes/8");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await LerJson(resposta);
        Assert.Equal("EM MANUTENCAO", corpo.GetProperty("status").GetString());
        Assert.Equal("tec123", corpo.GetProperty("matriculaTecnico").GetString());
        Assert.EndsWith("Z", corpo.GetProperty("dataInicioManutencao").GetString());
        var ultimo = corpo.GetProperty("historico").EnumerateArray().Last();
        Assert.Equal("EM MANUTENCAO", ultimo.GetProperty("statusNovo").GetString());
        Assert.EndsWith("Z", ultimo.GetProperty("data").GetString());
    }
}
