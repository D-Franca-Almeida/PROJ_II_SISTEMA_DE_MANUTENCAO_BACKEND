using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace CPTM.Manutencao.Tests.Integracao;

public sealed class CptmApiFactory : WebApplicationFactory<Program>
{
    private readonly string _arquivoBanco = Path.Combine(Path.GetTempPath(), $"cptm-teste-{Guid.NewGuid():N}.db");

    public string PastaUploads { get; } = Path.Combine(Path.GetTempPath(), $"cptm-uploads-{Guid.NewGuid():N}");

    private string StringDeConexao => $"Data Source={_arquivoBanco}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("Database:RecriarAoIniciar", "true");
        builder.UseSetting("ConnectionStrings:Sqlite", StringDeConexao);
        builder.UseSetting("Uploads:Raiz", PastaUploads);
        builder.UseSetting("Prazos:ExecucaoAutomatica", "false");
    }

    public async Task<string> LoginAsync(string matricula, string senha = "senha")
    {
        using var cliente = CreateClient();
        var resposta = await cliente.PostAsJsonAsync("/api/Auth/login", new { matricula, senha });
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("token").GetString()!;
    }

    public HttpClient CriarCliente(string? token = null)
    {
        var cliente = CreateClient();
        if (token is not null)
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return cliente;
    }

    public async Task<HttpClient> CriarClienteLogadoAsync(string matricula) =>
        CriarCliente(await LoginAsync(matricula));

    public static StringContent Json(string corpo) => new(corpo, Encoding.UTF8, "application/json");

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        // Só o pool deste banco: ClearAllPools é global e, com as classes rodando em paralelo, descartava
        // conexões em uso por outras APIs de teste (ObjectDisposedException 'SQLitePCL.sqlite3' -> 500 aleatório).
        using (var conexao = new SqliteConnection(StringDeConexao))
            SqliteConnection.ClearPool(conexao);
        foreach (var arquivo in new[] { _arquivoBanco, _arquivoBanco + "-wal", _arquivoBanco + "-shm", _arquivoBanco + "-journal" })
            try { File.Delete(arquivo); } catch (IOException) { }
        try { Directory.Delete(PastaUploads, true); } catch (IOException) { }
    }
}
