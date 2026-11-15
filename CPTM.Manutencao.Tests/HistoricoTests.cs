using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Features.Historico.Queries;
using CPTM.Manutencao.Api.Models;
using CPTM.Manutencao.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CPTM.Manutencao.Tests;

// CPTM-87 (US-26): permissão e ordem da linha do tempo
public sealed class HistoricoTests : IDisposable
{
    private static readonly DateTime T0 = new(2027, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _conexao = new("DataSource=:memory:");
    private readonly ManutencaoDbContext _db;
    private readonly ObterHistoricoHandler _handler;

    public HistoricoTests()
    {
        _conexao.Open();
        _db = new ManutencaoDbContext(new DbContextOptionsBuilder<ManutencaoDbContext>().UseSqlite(_conexao).Options);
        _db.Database.EnsureCreated();
        _handler = new ObterHistoricoHandler(new SolicitacaoRepository(_db));
    }

    public void Dispose()
    {
        _db.Dispose();
        _conexao.Dispose();
    }

    private int Ssl(bool comOsm)
    {
        var s = new Solicitacao { Descricao = "Teste", Status = StatusSolicitacao.EmManutencao, MatriculaSolicitante = "1001", EquipamentoId = 3, DataAbertura = T0 };
        if (comOsm)
        {
            s.MatriculaTecnico = "tec1";
            s.OrdemServico = new OrdemServico { MatriculaTecnico = "tec1", DataAbertura = T0 };
        }
        _db.Solicitacoes.Add(s);
        _db.SaveChanges();
        _db.HistoricoStatus.AddRange(
            new HistoricoStatus { SolicitacaoId = s.Id, StatusAnterior = "APROVADA", StatusNovo = "RECEBIDA", Matricula = "adm", Data = T0.AddHours(2) },
            new HistoricoStatus { SolicitacaoId = s.Id, StatusNovo = "SOLICITADA", Matricula = "1001", Data = T0 },
            new HistoricoStatus { SolicitacaoId = s.Id, StatusAnterior = "SOLICITADA", StatusNovo = "APROVADA", Matricula = "adm", Data = T0.AddHours(1), Observacao = "ok" });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return s.Id;
    }

    [Fact]
    public void Devolve_em_ordem_cronologica_com_de_para_quem_e_observacao()
    {
        var id = Ssl(comOsm: true);

        var linhas = _handler.Executar(id, "1001", "Comum");

        Assert.Equal(new[] { "SOLICITADA", "APROVADA", "RECEBIDA" }, linhas.Select(l => l.StatusNovo));
        Assert.Equal(new[] { T0, T0.AddHours(1), T0.AddHours(2) }, linhas.Select(l => l.Data));
        Assert.Null(linhas[0].StatusAnterior);
        Assert.Equal("SOLICITADA", linhas[1].StatusAnterior);
        Assert.Equal("adm", linhas[1].Matricula);
        Assert.Equal("ok", linhas[1].Observacao);
    }

    [Theory]
    [InlineData("1001", "Comum", true)]
    [InlineData("outro", "Comum", false)]
    [InlineData("adm", "Administrativo", true)]
    [InlineData("tec1", "Tecnico", true)]
    [InlineData("tec2", "Tecnico", false)]
    public void Permissao_por_perfil(string matricula, string perfil, bool permitido)
    {
        var id = Ssl(comOsm: true);

        if (permitido)
            Assert.NotEmpty(_handler.Executar(id, matricula, perfil));
        else
            Assert.Throws<UnauthorizedAccessException>(() => _handler.Executar(id, matricula, perfil));
    }

    [Fact]
    public void Tecnico_nao_ve_ssl_sem_osm()
    {
        var id = Ssl(comOsm: false);

        Assert.Throws<UnauthorizedAccessException>(() => _handler.Executar(id, "tec1", "Tecnico"));
    }

    [Fact]
    public void Ssl_inexistente_lanca_nao_encontrada()
    {
        Assert.Throws<SolicitacaoNaoEncontradaException>(() => _handler.Executar(999, "adm", "Administrativo"));
    }
}
