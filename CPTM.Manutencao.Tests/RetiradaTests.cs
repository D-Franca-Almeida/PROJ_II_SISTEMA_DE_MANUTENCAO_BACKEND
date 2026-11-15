using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using CPTM.Manutencao.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CPTM.Manutencao.Tests;

// CPTM-82 (US-11): validação do documento no protocolo de retirada
public sealed class RetiradaTests : IDisposable
{
    private readonly SqliteConnection _conexao = new("DataSource=:memory:");
    private readonly ManutencaoDbContext _db;
    private readonly WorkflowService _workflow;
    private readonly DateTimeOffset _agora = new(2026, 11, 15, 13, 0, 0, TimeSpan.FromHours(-3));

    public RetiradaTests()
    {
        _conexao.Open();
        var opcoes = new DbContextOptionsBuilder<ManutencaoDbContext>().UseSqlite(_conexao).Options;
        _db = new ManutencaoDbContext(opcoes);
        _db.Database.EnsureCreated();
        _workflow = new WorkflowService(new SolicitacaoRepository(_db), new RelogioFalso(_agora.UtcDateTime));
    }

    public void Dispose()
    {
        _db.Dispose();
        _conexao.Dispose();
    }

    private Solicitacao Criar(string status, string? autorizado = null)
    {
        var s = new Solicitacao
        {
            Descricao = "Teste", Status = status, MatriculaSolicitante = "1001",
            AutorizadoNome = autorizado is null ? null : "Maria", AutorizadoDocumento = autorizado
        };
        _db.Solicitacoes.Add(s);
        _db.SaveChanges();
        return s;
    }

    [Fact]
    public void Solicitante_retira_de_aguardando_e_grava_data_utc()
    {
        var s = Criar(StatusSolicitacao.AguardandoRetirada);

        _workflow.RegistrarRetirada(s.Id, "admin123", " 1001 ");

        var gravada = _db.Solicitacoes.AsNoTracking().Single(x => x.Id == s.Id);
        Assert.Equal(StatusSolicitacao.Retirado, gravada.Status);
        Assert.Equal("1001", gravada.IdentificacaoRetirada);
        Assert.Equal(_agora.UtcDateTime, gravada.DataRetirada);
        Assert.Equal(DateTimeKind.Utc, gravada.DataRetirada!.Value.Kind);
    }

    [Fact]
    public void Terceiro_sem_autorizacao_400()
    {
        var s = Criar(StatusSolicitacao.AguardandoRetirada);

        var erro = Assert.Throws<RegraDeNegocioException>(() => _workflow.RegistrarRetirada(s.Id, "admin123", "12345678900"));

        Assert.Contains("autorização de retirada", erro.Message);
        Assert.Equal(StatusSolicitacao.AguardandoRetirada, _db.Solicitacoes.AsNoTracking().Single(x => x.Id == s.Id).Status);
    }

    [Theory]
    [InlineData("12345678900")]
    [InlineData("123.456.789-00")]
    [InlineData("1001")]
    public void Retirada_autorizada_aceita_autorizado_ou_solicitante(string documento)
    {
        var s = Criar(StatusSolicitacao.RetiradaAutorizada, autorizado: "123.456.789-00");

        _workflow.RegistrarRetirada(s.Id, "admin123", documento);

        Assert.Equal(StatusSolicitacao.Retirado, _db.Solicitacoes.AsNoTracking().Single(x => x.Id == s.Id).Status);
    }

    [Theory]
    [InlineData("98765432100")]
    [InlineData("1002")]
    [InlineData("...")]
    public void Retirada_autorizada_com_outro_documento_400(string documento)
    {
        var s = Criar(StatusSolicitacao.RetiradaAutorizada, autorizado: "123.456.789-00");

        Assert.Throws<RegraDeNegocioException>(() => _workflow.RegistrarRetirada(s.Id, "admin123", documento));
    }

    [Fact]
    public void Status_verificado_antes_do_documento()
    {
        var s = Criar(StatusSolicitacao.SemReparo);

        var erro = Assert.Throws<RegraDeNegocioException>(() => _workflow.RegistrarRetirada(s.Id, "admin123", "1001"));

        Assert.StartsWith("Transição inválida", erro.Message);
    }
}
