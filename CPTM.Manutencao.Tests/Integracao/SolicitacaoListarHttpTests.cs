using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CPTM.Manutencao.Tests.Integracao;

public sealed class SolicitacaoListarHttpTests : IClassFixture<CptmApiFactory>
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };

    private readonly CptmApiFactory _api;

    public SolicitacaoListarHttpTests(CptmApiFactory api)
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
    public async Task Admin_sem_filtro_recebe_todas_do_mais_novo_para_o_mais_antigo()
    {
        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        using var criada = await comum.PostAsync("/api/solicitacoes", Form("Mais nova de todas", 3));
        var idNova = (await LerJson(criada)).GetProperty("id").GetInt32();

        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        using var resposta = await admin.GetAsync("/api/solicitacoes");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var itens = (await LerJson(resposta)).EnumerateArray().ToList();
        Assert.True(itens.Count >= 13);
        Assert.Equal(idNova, itens[0].GetProperty("id").GetInt32());
        var datas = itens.Select(i => i.GetProperty("dataAbertura").GetDateTime()).ToList();
        Assert.Equal(datas.OrderByDescending(d => d), datas);
        var seed = itens.Select(i => i.GetProperty("id").GetInt32()).Where(id => id <= 12).ToList();
        Assert.Equal(Enumerable.Range(1, 12).Reverse(), seed);
    }

    [Fact]
    public async Task Item_traz_resumo_completo_com_equipamento_estacao_e_data_utc()
    {
        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        using var resposta = await admin.GetAsync("/api/solicitacoes?status=DEVOLVIDA");

        var itens = (await LerJson(resposta)).EnumerateArray().ToList();
        var item = itens.Single(i => i.GetProperty("id").GetInt32() == 9);
        Assert.Equal("DEVOLVIDA", item.GetProperty("status").GetString());
        Assert.Equal("Sinalização apagada (devolvida)", item.GetProperty("descricao").GetString());
        Assert.Equal("comum123", item.GetProperty("matriculaSolicitante").GetString());
        Assert.EndsWith("Z", item.GetProperty("dataAbertura").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("fotoUrl").ValueKind);
        Assert.True(item.TryGetProperty("motivoDevolucao", out _));
        var equipamento = item.GetProperty("equipamento");
        Assert.Equal(13, equipamento.GetProperty("id").GetInt32());
        Assert.False(string.IsNullOrEmpty(equipamento.GetProperty("descricao").GetString()));
        Assert.False(string.IsNullOrEmpty(equipamento.GetProperty("patrimonio").GetString()));
        var estacao = item.GetProperty("estacao");
        Assert.False(string.IsNullOrEmpty(estacao.GetProperty("nome").GetString()));
        Assert.False(string.IsNullOrEmpty(estacao.GetProperty("linha").GetString()));
    }

    [Fact]
    public async Task Filtro_por_status_devolve_so_aquele_status_ignorando_caixa()
    {
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        foreach (var consulta in new[] { "SOLICITADA", "solicitada", "Solicitada" })
        {
            using var resposta = await admin.GetAsync($"/api/solicitacoes?status={consulta}");
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            var itens = (await LerJson(resposta)).EnumerateArray().ToList();
            Assert.NotEmpty(itens);
            Assert.All(itens, i => Assert.Equal("SOLICITADA", i.GetProperty("status").GetString()));
        }
    }

    [Fact]
    public async Task Filtro_aceita_status_com_espaco_e_underscore()
    {
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        foreach (var consulta in new[] { "EM%20MANUTENCAO", "em_manutencao" })
        {
            using var resposta = await admin.GetAsync($"/api/solicitacoes?status={consulta}");
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            var itens = (await LerJson(resposta)).EnumerateArray().ToList();
            Assert.Contains(itens, i => i.GetProperty("id").GetInt32() == 10);
            Assert.All(itens, i => Assert.Equal("EM MANUTENCAO", i.GetProperty("status").GetString()));
        }
    }

    [Fact]
    public async Task Status_invalido_devolve_400_listando_valores_validos()
    {
        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        using var resposta = await admin.GetAsync("/api/solicitacoes?status=XPTO");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var erro = (await LerJson(resposta)).GetProperty("erro").GetString()!;
        Assert.Contains("XPTO", erro);
        foreach (var valido in new[] { "SOLICITADA", "REENVIADA", "APROVADA", "DEVOLVIDA", "CANCELADA", "EM MANUTENCAO", "CONCLUIDA" })
            Assert.Contains(valido, erro);
    }

    [Fact]
    public async Task Apos_devolucao_o_motivo_aparece_na_fila()
    {
        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        using var devolucao = await admin.PutAsync("/api/Solicitacoes/2/devolver", CptmApiFactory.Json("{\"motivo\":\"Foto ilegivel\"}"));
        Assert.Equal(HttpStatusCode.OK, devolucao.StatusCode);

        using var resposta = await admin.GetAsync("/api/solicitacoes?status=devolvida");
        var item = (await LerJson(resposta)).EnumerateArray().Single(i => i.GetProperty("id").GetInt32() == 2);
        Assert.Equal("Foto ilegivel", item.GetProperty("motivoDevolucao").GetString());
    }

    [Theory]
    [InlineData("comum123")]
    [InlineData("tec123")]
    public async Task Comum_e_tecnico_recebem_403(string matricula)
    {
        using var cliente = await _api.CriarClienteLogadoAsync(matricula);
        using var resposta = await cliente.GetAsync("/api/solicitacoes");
        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task Sem_token_ou_token_invalido_recebe_401()
    {
        using var anonimo = _api.CriarCliente();
        using var invalido = _api.CriarCliente("token-invalido");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/solicitacoes")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await invalido.GetAsync("/api/solicitacoes")).StatusCode);
    }
}
