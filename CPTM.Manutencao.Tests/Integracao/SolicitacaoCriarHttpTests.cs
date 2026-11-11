using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CPTM.Manutencao.Tests.Integracao;

public sealed class SolicitacaoCriarHttpTests : IClassFixture<CptmApiFactory>
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };

    private readonly CptmApiFactory _api;

    public SolicitacaoCriarHttpTests(CptmApiFactory api)
    {
        _api = api;
    }

    private static MultipartFormDataContent Form(string? descricao = "Escada parada", string? equipamentoId = "1",
        byte[]? foto = null, string nome = "foto.png", string contentType = "image/png", bool incluirFoto = true)
    {
        var form = new MultipartFormDataContent();
        if (equipamentoId is not null) form.Add(new StringContent(equipamentoId), "equipamentoId");
        if (descricao is not null) form.Add(new StringContent(descricao), "descricao");
        if (incluirFoto)
        {
            var arquivo = new ByteArrayContent(foto ?? Png);
            arquivo.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(arquivo, "foto", nome);
        }
        return form;
    }

    private static async Task<JsonElement> LerJson(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.Clone();

    private int ContarHistorico(int solicitacaoId)
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        return db.HistoricoStatus.Count(h => h.SolicitacaoId == solicitacaoId);
    }

    private int ArquivosGravados() =>
        Directory.Exists(Path.Combine(_api.PastaUploads, "ssl")) ? Directory.GetFiles(Path.Combine(_api.PastaUploads, "ssl")).Length : 0;

    [Fact]
    public async Task Comum_cria_ssl_201_com_foto_gravada_e_servida_e_historico()
    {
        using var cliente = await _api.CriarClienteLogadoAsync("comum123");
        var antes = DateTime.UtcNow.AddSeconds(-2);

        using var resposta = await cliente.PostAsync("/api/solicitacoes", Form("  Escada rolante parada  ", "2"));

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var corpo = await LerJson(resposta);
        var id = corpo.GetProperty("id").GetInt32();
        Assert.True(id > 12);
        Assert.Equal($"/api/Solicitacoes/{id}", resposta.Headers.Location!.OriginalString);
        Assert.Equal("SOLICITADA", corpo.GetProperty("status").GetString());
        Assert.Equal("Escada rolante parada", corpo.GetProperty("descricao").GetString());
        Assert.Equal("comum123", corpo.GetProperty("matriculaSolicitante").GetString());
        Assert.EndsWith("Z", corpo.GetProperty("dataAbertura").GetString());
        Assert.True(corpo.GetProperty("dataAbertura").GetDateTime().ToUniversalTime() >= antes);
        Assert.Equal(2, corpo.GetProperty("equipamento").GetProperty("id").GetInt32());
        Assert.Equal("PAT-LUZ-0002", corpo.GetProperty("equipamento").GetProperty("patrimonio").GetString());
        Assert.Equal("Luz", corpo.GetProperty("estacao").GetProperty("nome").GetString());
        Assert.Equal("7-Rubi", corpo.GetProperty("estacao").GetProperty("linha").GetString());

        var url = corpo.GetProperty("fotoUrl").GetString()!;
        Assert.Matches(@"^/uploads/ssl/[0-9a-f]{32}\.png$", url);
        Assert.Equal(Png, await File.ReadAllBytesAsync(Path.Combine(_api.PastaUploads, "ssl", Path.GetFileName(url))));

        using var anonimo = _api.CriarCliente();
        using var foto = await anonimo.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, foto.StatusCode);
        Assert.Equal("image/png", foto.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await foto.Content.ReadAsByteArrayAsync());

        Assert.Equal(1, ContarHistorico(id));
        using var escopo = _api.Services.CreateScope();
        var h = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>().HistoricoStatus.Single(x => x.SolicitacaoId == id);
        Assert.Null(h.StatusAnterior);
        Assert.Equal(StatusSolicitacao.Solicitada, h.StatusNovo);
        Assert.Equal("comum123", h.Matricula);
    }

    [Fact]
    public async Task Extensao_jpg_e_jpeg_sao_aceitas()
    {
        using var cliente = await _api.CriarClienteLogadoAsync("comum123");
        foreach (var nome in new[] { "a.jpg", "b.JPEG" })
        {
            using var resposta = await cliente.PostAsync("/api/solicitacoes", Form(nome: nome, contentType: "image/jpeg"));
            Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        }
    }

    [Fact]
    public async Task Foto_exatamente_5MB_e_aceita()
    {
        using var cliente = await _api.CriarClienteLogadoAsync("comum123");
        using var resposta = await cliente.PostAsync("/api/solicitacoes", Form(foto: new byte[5 * 1024 * 1024]));
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    }

    public static IEnumerable<object?[]> Invalidos() => new[]
    {
        new object?[] { "sem foto", "descricao", "1", false, new byte[] { 1 }, "f.png", "image/png", "A foto é obrigatória." },
        new object?[] { "foto vazia", "descricao", "1", true, Array.Empty<byte>(), "f.png", "image/png", "A foto é obrigatória." },
        new object?[] { "foto acima de 5MB", "descricao", "1", true, new byte[5 * 1024 * 1024 + 1], "f.png", "image/png", "A foto deve ter no máximo 5 MB." },
        new object?[] { "extensao gif", "descricao", "1", true, new byte[] { 1 }, "f.gif", "image/gif", "A foto deve ser JPG, JPEG ou PNG." },
        new object?[] { "extensao exe com content type png", "descricao", "1", true, new byte[] { 1 }, "f.exe", "image/png", "A foto deve ser JPG, JPEG ou PNG." },
        new object?[] { "content type pdf com extensao png", "descricao", "1", true, new byte[] { 1 }, "f.png", "application/pdf", "A foto deve ser JPG, JPEG ou PNG." },
        new object?[] { "sem descricao", null, "1", true, new byte[] { 1 }, "f.png", "image/png", "A descrição é obrigatória." },
        new object?[] { "descricao em branco", "   ", "1", true, new byte[] { 1 }, "f.png", "image/png", "A descrição é obrigatória." },
        new object?[] { "descricao longa", new string('x', 501), "1", true, new byte[] { 1 }, "f.png", "image/png", "A descrição deve ter no máximo 500 caracteres." },
        new object?[] { "equipamento inexistente", "descricao", "999", true, new byte[] { 1 }, "f.png", "image/png", "Equipamento 999 não encontrado." },
        new object?[] { "sem equipamento", "descricao", null, true, new byte[] { 1 }, "f.png", "image/png", "O equipamento é obrigatório." }
    };

    [Theory]
    [MemberData(nameof(Invalidos))]
    public async Task Requisicao_invalida_devolve_400_sem_gravar_nada(string cenario, string? descricao, string? equipamentoId,
        bool incluirFoto, byte[] foto, string nome, string contentType, string erroEsperado)
    {
        using var cliente = await _api.CriarClienteLogadoAsync("comum123");
        var arquivosAntes = ArquivosGravados();

        using var resposta = await cliente.PostAsync("/api/solicitacoes",
            Form(descricao, equipamentoId, foto, nome, contentType, incluirFoto));

        Assert.True(resposta.StatusCode == HttpStatusCode.BadRequest, cenario);
        Assert.Equal(erroEsperado, (await LerJson(resposta)).GetProperty("erro").GetString());
        Assert.Equal(arquivosAntes, ArquivosGravados());
    }

    [Fact]
    public async Task Equipamento_nao_numerico_devolve_400()
    {
        using var cliente = await _api.CriarClienteLogadoAsync("comum123");
        using var resposta = await cliente.PostAsync("/api/solicitacoes", Form(equipamentoId: "abc"));
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Theory]
    [InlineData("admin123")]
    [InlineData("tec123")]
    public async Task Outros_perfis_recebem_403(string matricula)
    {
        using var cliente = await _api.CriarClienteLogadoAsync(matricula);
        var arquivosAntes = ArquivosGravados();

        using var resposta = await cliente.PostAsync("/api/solicitacoes", Form());

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal(arquivosAntes, ArquivosGravados());
    }

    [Fact]
    public async Task Sem_token_devolve_401()
    {
        using var anonimo = _api.CriarCliente();
        using var resposta = await anonimo.PostAsync("/api/solicitacoes", Form());
        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public void Seed_das_ssls_1_a_12_tem_equipamento()
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        var seed = db.Solicitacoes.Where(s => s.Id <= 12).ToList();
        Assert.Equal(12, seed.Count);
        Assert.All(seed, s => Assert.NotNull(s.EquipamentoId));
    }
}
