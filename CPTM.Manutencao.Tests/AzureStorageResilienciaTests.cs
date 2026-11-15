using System.Diagnostics;
using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using CPTM.Manutencao.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CPTM.Manutencao.Tests;

public sealed class AzureStorageResilienciaTests : IDisposable
{
    // Chave pública e fixa do emulador Azurite; a porta 1 recusa a conexão na hora, sem depender de rede externa
    public const string ConexaoInacessivel =
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
        "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
        "BlobEndpoint=http://127.0.0.1:1/devstoreaccount1;QueueEndpoint=http://127.0.0.1:1/devstoreaccount1;";

    private static readonly AzureStorageOptions OpcoesInacessiveis = new() { ConnectionString = ConexaoInacessivel };

    private readonly SqliteConnection _conexao = new("DataSource=:memory:");
    private readonly ManutencaoDbContext _db;

    public AzureStorageResilienciaTests()
    {
        _conexao.Open();
        _db = new ManutencaoDbContext(new DbContextOptionsBuilder<ManutencaoDbContext>().UseSqlite(_conexao).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _conexao.Dispose();
    }

    private static AzureQueueNotificacaoService FilaInacessivel() =>
        new(OpcoesInacessiveis, NullLogger<AzureQueueNotificacaoService>.Instance);

    [Theory]
    [InlineData("https://stcptmmanut2026.blob.core.windows.net/uploads/ssl/abc.png", "ssl/abc.png")]
    [InlineData("http://127.0.0.1:10000/devstoreaccount1/uploads/ssl/abc.jpg", "ssl/abc.jpg")]
    [InlineData("https://stcptmmanut2026.blob.core.windows.net/UPLOADS/ssl/abc.png", "ssl/abc.png")]
    public void Nome_do_blob_sai_da_url_do_azure_e_do_azurite(string url, string esperado)
    {
        Assert.Equal(esperado, AzureBlobFotoStorage.NomeDoBlob(url, "uploads"));
    }

    [Theory]
    [InlineData("ssl/abc.png")]
    [InlineData("")]
    public void Url_relativa_nao_gera_nome_de_blob(string url)
    {
        Assert.Null(AzureBlobFotoStorage.NomeDoBlob(url, "uploads"));
    }

    [Fact]
    public void Connection_string_malformada_e_recusada_no_boot_com_mensagem_clara()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AzureStorageOptions.ValidarConnectionString("isto-nao-e-uma-connection-string"));
        Assert.Contains("AzureStorage:ConnectionString", ex.Message);
    }

    [Theory]
    [InlineData("UseDevelopmentStorage=true")]
    [InlineData(ConexaoInacessivel)]
    public void Connection_string_bem_formada_passa_na_validacao(string conexao)
    {
        AzureStorageOptions.ValidarConnectionString(conexao);
    }

    [Fact]
    public void Construtores_nao_acessam_a_rede_nem_lancam_com_storage_inacessivel()
    {
        var cronometro = Stopwatch.StartNew();

        _ = FilaInacessivel();
        _ = new AzureBlobFotoStorage(OpcoesInacessiveis, NullLogger<AzureBlobFotoStorage>.Instance);

        Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Falha_da_fila_e_engolida_e_o_evento_fica_no_espelho_local()
    {
        var fila = FilaInacessivel();
        var cronometro = Stopwatch.StartNew();

        fila.Publicar(new EventoSistemaMensagem("LOGIN_FALHA", "x", null, null, null, null, DateTime.UtcNow));

        Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(15), $"Publicar levou {cronometro.Elapsed}.");
        Assert.Equal("LOGIN_FALHA", Assert.Single(fila.ObterRecentes()).TipoEvento);
    }

    [Fact]
    public void Depois_de_uma_falha_a_fila_nao_tenta_a_rede_durante_a_pausa()
    {
        var fila = FilaInacessivel();
        fila.Publicar(new EventoSistemaMensagem("LOGIN_FALHA", "x", null, null, null, null, DateTime.UtcNow));
        var cronometro = Stopwatch.StartNew();

        fila.Publicar(new EventoSistemaMensagem("LOGIN_SUCESSO", "y", null, null, null, null, DateTime.UtcNow));

        Assert.True(cronometro.Elapsed < TimeSpan.FromMilliseconds(500), $"Segundo Publicar levou {cronometro.Elapsed}.");
        Assert.Equal(new[] { "LOGIN_SUCESSO", "LOGIN_FALHA" }, fila.ObterRecentes().Select(e => e.TipoEvento));
    }

    [Fact]
    public void Transicionar_grava_mesmo_com_a_fila_inacessivel()
    {
        var fila = FilaInacessivel();
        var workflow = new WorkflowService(new SolicitacaoRepository(_db), fila: fila);
        var solicitacao = new Solicitacao { Descricao = "Teste", MatriculaSolicitante = "comum123", EquipamentoId = 3 };

        workflow.Transicionar(solicitacao, StatusSolicitacao.Solicitada, "comum123");

        _db.ChangeTracker.Clear();
        Assert.Equal(StatusSolicitacao.Solicitada, _db.Solicitacoes.AsNoTracking().Single(s => s.Id == solicitacao.Id).Status);
        Assert.Equal("MUDANCA_STATUS", Assert.Single(fila.ObterRecentes()).TipoEvento);
    }

    [Fact]
    public void Login_devolve_token_mesmo_com_a_fila_inacessivel()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = "chave-de-teste-com-mais-de-32-caracteres-123456" })
            .Build();
        var fila = FilaInacessivel();
        var auth = new AuthService(config, _db, new PasswordHasher<Usuario>(), fila);

        Assert.NotNull(auth.RealizarLogin("admin123", "senha"));
        Assert.Equal("LOGIN_SUCESSO", Assert.Single(fila.ObterRecentes()).TipoEvento);
    }

    [Fact]
    public async Task Upload_com_blob_inacessivel_vira_armazenamento_indisponivel()
    {
        var storage = new AzureBlobFotoStorage(OpcoesInacessiveis, NullLogger<AzureBlobFotoStorage>.Instance);
        var conteudo = new MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        var arquivo = new FormFile(conteudo, 0, conteudo.Length, "foto", "foto.png");

        await Assert.ThrowsAsync<ArmazenamentoIndisponivelException>(() => storage.SalvarAsync(arquivo, ".png"));
    }

    [Fact]
    public void Excluir_com_blob_inacessivel_nao_lanca()
    {
        var storage = new AzureBlobFotoStorage(OpcoesInacessiveis, NullLogger<AzureBlobFotoStorage>.Instance);

        storage.Excluir("http://127.0.0.1:1/devstoreaccount1/uploads/ssl/abc.png");
    }
}
