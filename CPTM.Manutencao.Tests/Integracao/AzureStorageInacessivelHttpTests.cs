using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CPTM.Manutencao.Tests.Integracao;

// Modo Azure com o storage fora do ar: login e transições seguem funcionando; só o upload da foto responde 503
public sealed class AzureStorageInacessivelHttpTests : IDisposable
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };

    private readonly CptmApiFactory _api = new() { ConexaoAzureStorage = AzureStorageResilienciaTests.ConexaoInacessivel };

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Login_responde_200_rapido_com_o_storage_inacessivel()
    {
        using var cliente = _api.CriarCliente();
        var cronometro = Stopwatch.StartNew();

        using var resposta = await cliente.PostAsJsonAsync("/api/Auth/login", new { matricula = "admin123", senha = "senha" });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(15), $"Login levou {cronometro.Elapsed}.");
    }

    [Fact]
    public async Task Transicao_responde_200_e_o_evento_fica_no_espelho_local()
    {
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var aprovar = await admin.PutAsync("/api/solicitacoes/1/aprovar", null);
        Assert.Equal(HttpStatusCode.OK, aprovar.StatusCode);

        using var eventos = await admin.GetAsync("/api/admin/eventos-fila?limite=5");
        using var json = JsonDocument.Parse(await eventos.Content.ReadAsStringAsync());
        Assert.Contains(json.RootElement.EnumerateArray(), e =>
            e.GetProperty("tipoEvento").GetString() == "MUDANCA_STATUS" &&
            e.GetProperty("solicitacaoId").GetInt32() == 1);
    }

    [Fact]
    public async Task Criar_ssl_com_blob_inacessivel_responde_503_com_erro_tratado()
    {
        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        using var form = new MultipartFormDataContent
        {
            { new StringContent("1"), "equipamentoId" },
            { new StringContent("Escada parada"), "descricao" }
        };
        var foto = new ByteArrayContent(Png);
        foto.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(foto, "foto", "foto.png");

        using var resposta = await comum.PostAsync("/api/solicitacoes", form);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resposta.StatusCode);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        Assert.Contains("indisponível", json.RootElement.GetProperty("erro").GetString());
    }

    [Fact]
    public async Task Reenviar_com_foto_e_blob_inacessivel_responde_503_e_a_ssl_continua_devolvida()
    {
        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        using var devolvida = await admin.PutAsync("/api/solicitacoes/2/devolver", CptmApiFactory.Json("{\"motivo\":\"Foto escura\"}"));
        Assert.Equal(HttpStatusCode.OK, devolvida.StatusCode);
        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        using var form = new MultipartFormDataContent();
        var foto = new ByteArrayContent(Png);
        foto.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(foto, "foto", "foto.png");

        using var resposta = await comum.PutAsync("/api/solicitacoes/2/reenviar", form);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resposta.StatusCode);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        Assert.Contains("indisponível", json.RootElement.GetProperty("erro").GetString());
        using var detalhe = await admin.GetAsync("/api/solicitacoes/2");
        using var ssl = JsonDocument.Parse(await detalhe.Content.ReadAsStringAsync());
        Assert.Equal("DEVOLVIDA", ssl.RootElement.GetProperty("status").GetString());
    }
}
