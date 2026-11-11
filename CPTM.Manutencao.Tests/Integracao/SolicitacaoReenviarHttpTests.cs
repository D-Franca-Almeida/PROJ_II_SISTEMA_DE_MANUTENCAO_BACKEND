using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CPTM.Manutencao.Tests.Integracao;

public sealed class SolicitacaoReenviarHttpTests : IClassFixture<CptmApiFactory>
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 9, 8, 7 };

    private readonly CptmApiFactory _api;

    public SolicitacaoReenviarHttpTests(CptmApiFactory api)
    {
        _api = api;
    }

    private static MultipartFormDataContent Form(string? descricao = null, byte[]? foto = null,
        string nome = "nova.jpg", string contentType = "image/jpeg")
    {
        var form = new MultipartFormDataContent();
        if (descricao is not null) form.Add(new StringContent(descricao), "descricao");
        if (foto is not null)
        {
            var arquivo = new ByteArrayContent(foto);
            arquivo.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(arquivo, "foto", nome);
        }
        return form;
    }

    private static async Task<JsonElement> LerJson(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<int> CriarDevolvidaAsync(string motivo = "Foto escura")
    {
        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        using var criada = await comum.PostAsync("/api/solicitacoes", CriarForm());
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        var id = (await LerJson(criada)).GetProperty("id").GetInt32();

        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        using var devolvida = await admin.PutAsync($"/api/solicitacoes/{id}/devolver",
            CptmApiFactory.Json($"{{\"motivo\":\"{motivo}\"}}"));
        Assert.Equal(HttpStatusCode.OK, devolvida.StatusCode);
        return id;
    }

    private static MultipartFormDataContent CriarForm()
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent("2"), "equipamentoId" },
            { new StringContent("Escada parada"), "descricao" }
        };
        var arquivo = new ByteArrayContent(Png);
        arquivo.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(arquivo, "foto", "f.png");
        return form;
    }

    private (string Status, string Descricao, string? Foto, string? Motivo, int Historico) Estado(int id)
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        var s = db.Solicitacoes.AsNoTracking().Single(x => x.Id == id);
        return (s.Status, s.Descricao, s.FotoUrl, s.MotivoDevolucao, db.HistoricoStatus.Count(h => h.SolicitacaoId == id));
    }

    private int ArquivosGravados() =>
        Directory.Exists(Path.Combine(_api.PastaUploads, "ssl")) ? Directory.GetFiles(Path.Combine(_api.PastaUploads, "ssl")).Length : 0;

    [Fact]
    public async Task Dono_reenvia_so_com_descricao_200_reenviada_e_limpa_motivo()
    {
        var id = await CriarDevolvidaAsync();
        var antes = Estado(id);
        using var comum = await _api.CriarClienteLogadoAsync("comum123");

        using var resposta = await comum.PutAsync($"/api/solicitacoes/{id}/reenviar", Form("  Escada parada, foto refeita  "));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await LerJson(resposta);
        Assert.Equal(id, corpo.GetProperty("id").GetInt32());
        Assert.Equal("REENVIADA", corpo.GetProperty("status").GetString());
        Assert.Equal("Escada parada, foto refeita", corpo.GetProperty("descricao").GetString());
        Assert.Equal(antes.Foto, corpo.GetProperty("fotoUrl").GetString());
        Assert.Equal(JsonValueKind.Null, corpo.GetProperty("motivoDevolucao").ValueKind);
        Assert.Equal(2, corpo.GetProperty("equipamento").GetProperty("id").GetInt32());
        Assert.EndsWith("Z", corpo.GetProperty("dataAbertura").GetString());

        var depois = Estado(id);
        Assert.Equal("REENVIADA", depois.Status);
        Assert.Null(depois.Motivo);
        Assert.Equal(antes.Historico + 1, depois.Historico);

        using var detalhe = await comum.GetAsync($"/api/solicitacoes/{id}");
        var historico = (await LerJson(detalhe)).GetProperty("historico");
        var ultima = historico[historico.GetArrayLength() - 1];
        Assert.Equal("DEVOLVIDA", ultima.GetProperty("statusAnterior").GetString());
        Assert.Equal("REENVIADA", ultima.GetProperty("statusNovo").GetString());
        Assert.Equal("comum123", ultima.GetProperty("matricula").GetString());
    }

    [Fact]
    public async Task Dono_reenvia_so_com_foto_troca_arquivo_e_mantem_descricao()
    {
        var id = await CriarDevolvidaAsync();
        var antes = Estado(id);
        var arquivosAntes = ArquivosGravados();
        using var comum = await _api.CriarClienteLogadoAsync("comum123");

        using var resposta = await comum.PutAsync($"/api/solicitacoes/{id}/reenviar", Form(foto: Jpeg));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await LerJson(resposta);
        Assert.Equal("REENVIADA", corpo.GetProperty("status").GetString());
        Assert.Equal(antes.Descricao, corpo.GetProperty("descricao").GetString());
        var url = corpo.GetProperty("fotoUrl").GetString()!;
        Assert.NotEqual(antes.Foto, url);
        Assert.Matches(@"^/uploads/ssl/[0-9a-f]{32}\.jpg$", url);
        Assert.Equal(Jpeg, await File.ReadAllBytesAsync(Path.Combine(_api.PastaUploads, "ssl", Path.GetFileName(url))));
        Assert.False(File.Exists(Path.Combine(_api.PastaUploads, "ssl", Path.GetFileName(antes.Foto!))));
        Assert.Equal(arquivosAntes, ArquivosGravados());
    }

    [Fact]
    public async Task Dono_reenvia_com_descricao_e_foto()
    {
        var id = await CriarDevolvidaAsync();
        using var comum = await _api.CriarClienteLogadoAsync("comum123");

        using var resposta = await comum.PutAsync($"/api/solicitacoes/{id}/reenviar", Form("Nova descricao", Png, "x.png", "image/png"));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await LerJson(resposta);
        Assert.Equal("Nova descricao", corpo.GetProperty("descricao").GetString());
        Assert.EndsWith(".png", corpo.GetProperty("fotoUrl").GetString());
    }

    [Fact]
    public async Task Reenviada_pode_ser_aprovada_ou_devolvida_de_novo_pelo_admin()
    {
        var id1 = await CriarDevolvidaAsync();
        var id2 = await CriarDevolvidaAsync();
        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        foreach (var id in new[] { id1, id2 })
        {
            using var r = await comum.PutAsync($"/api/solicitacoes/{id}/reenviar", Form("Corrigida " + id));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }

        using var aprovada = await admin.PutAsync($"/api/solicitacoes/{id1}/aprovar", null);
        using var devolvida = await admin.PutAsync($"/api/solicitacoes/{id2}/devolver", CptmApiFactory.Json("{\"motivo\":\"Ainda ruim\"}"));

        Assert.Equal(HttpStatusCode.OK, aprovada.StatusCode);
        Assert.Equal(HttpStatusCode.OK, devolvida.StatusCode);
        Assert.Equal("APROVADA", Estado(id1).Status);
        var estado2 = Estado(id2);
        Assert.Equal("DEVOLVIDA", estado2.Status);
        Assert.Equal("Ainda ruim", estado2.Motivo);
    }

    [Fact]
    public async Task Sem_alteracao_400_e_nada_muda()
    {
        var id = await CriarDevolvidaAsync();
        var antes = Estado(id);
        using var comum = await _api.CriarClienteLogadoAsync("comum123");

        foreach (var form in new[] { new MultipartFormDataContent { { new StringContent("1"), "outro" } }, Form(""), Form("   "), Form(antes.Descricao), Form("  " + antes.Descricao + " ") })
        {
            using var resposta = await comum.PutAsync($"/api/solicitacoes/{id}/reenviar", form);
            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
            var txt = await resposta.Content.ReadAsStringAsync(); Assert.True(txt.Contains("Faça a correção antes de reenviar"), txt);
        }
        Assert.Equal(antes, Estado(id));
    }

    [Fact]
    public async Task Status_diferente_de_devolvida_400()
    {
        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        foreach (var id in new[] { 1, 8, 12 })
        {
            using var resposta = await comum.PutAsync($"/api/solicitacoes/{id}/reenviar", Form("Qualquer"));
            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
            Assert.Contains("DEVOLVIDA", (await LerJson(resposta)).GetProperty("erro").GetString());
        }
    }

    [Fact]
    public async Task Descricao_longa_foto_invalida_e_foto_grande_400_sem_gravar_arquivo()
    {
        var id = await CriarDevolvidaAsync();
        var antes = Estado(id);
        var arquivos = ArquivosGravados();
        using var comum = await _api.CriarClienteLogadoAsync("comum123");

        using var longa = await comum.PutAsync($"/api/solicitacoes/{id}/reenviar", Form(new string('a', 501)));
        using var tipo = await comum.PutAsync($"/api/solicitacoes/{id}/reenviar", Form(foto: Png, nome: "x.gif", contentType: "image/gif"));
        using var conteudo = await comum.PutAsync($"/api/solicitacoes/{id}/reenviar", Form(foto: new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, nome: "x.png", contentType: "image/png"));
        var grande = new byte[5 * 1024 * 1024 + 1];
        Png.CopyTo(grande, 0);
        using var tamanho = await comum.PutAsync($"/api/solicitacoes/{id}/reenviar", Form(foto: grande, nome: "x.png", contentType: "image/png"));

        Assert.All(new[] { longa, tipo, conteudo, tamanho }, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Equal("A foto deve ser JPG, JPEG ou PNG.", (await LerJson(tipo)).GetProperty("erro").GetString());
        Assert.Equal(antes, Estado(id));
        Assert.Equal(arquivos, ArquivosGravados());
    }

    [Fact]
    public async Task Comum_nao_dono_403_e_nada_muda()
    {
        int id;
        using (var escopo = _api.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
            var s = new Solicitacao
            {
                Descricao = "De outro dono",
                Status = StatusSolicitacao.Devolvida,
                MotivoDevolucao = "Motivo",
                MatriculaSolicitante = "outro456",
                EquipamentoId = 1,
                DataAbertura = DateTime.UtcNow
            };
            db.Solicitacoes.Add(s);
            db.SaveChanges();
            id = s.Id;
        }
        var antes = Estado(id);
        using var comum = await _api.CriarClienteLogadoAsync("comum123");

        using var resposta = await comum.PutAsync($"/api/solicitacoes/{id}/reenviar", Form("Invasor"));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal(antes, Estado(id));
    }

    [Theory]
    [InlineData("admin123")]
    [InlineData("tec123")]
    public async Task Admin_e_tecnico_403(string matricula)
    {
        var id = await CriarDevolvidaAsync();
        using var cliente = await _api.CriarClienteLogadoAsync(matricula);

        using var resposta = await cliente.PutAsync($"/api/solicitacoes/{id}/reenviar", Form("Nao pode"));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("DEVOLVIDA", Estado(id).Status);
    }

    [Fact]
    public async Task Sem_token_401()
    {
        using var anonimo = _api.CriarCliente();
        using var resposta = await anonimo.PutAsync("/api/solicitacoes/9/reenviar", Form("x"));
        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Inexistente_404()
    {
        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        using var resposta = await comum.PutAsync("/api/solicitacoes/9999/reenviar", Form("x"));
        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.Equal("Solicitação 9999 não encontrada.", (await LerJson(resposta)).GetProperty("erro").GetString());
    }
}
