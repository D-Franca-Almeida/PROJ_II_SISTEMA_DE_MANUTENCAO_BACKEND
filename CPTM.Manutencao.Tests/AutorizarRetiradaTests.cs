using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Features.Retirada.Commands;
using CPTM.Manutencao.Api.Models;
using CPTM.Manutencao.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CPTM.Manutencao.Tests;

// CPTM-79 (US-24): regras do AutorizarRetiradaHandler
public sealed class AutorizarRetiradaTests : IDisposable
{
    private readonly SqliteConnection _conexao = new("DataSource=:memory:");
    private readonly ManutencaoDbContext _db;
    private readonly AutorizarRetiradaHandler _handler;

    public AutorizarRetiradaTests()
    {
        _conexao.Open();
        var opcoes = new DbContextOptionsBuilder<ManutencaoDbContext>().UseSqlite(_conexao).Options;
        _db = new ManutencaoDbContext(opcoes);
        _db.Database.EnsureCreated();
        var repositorio = new SolicitacaoRepository(_db);
        _handler = new AutorizarRetiradaHandler(repositorio, new WorkflowService(repositorio));
    }

    public void Dispose()
    {
        _db.Dispose();
        _conexao.Dispose();
    }

    private Solicitacao Criar(string status = StatusSolicitacao.AguardandoRetirada, string solicitante = "1001")
    {
        var s = new Solicitacao { Descricao = "Teste", Status = status, MatriculaSolicitante = solicitante };
        _db.Solicitacoes.Add(s);
        _db.SaveChanges();
        return s;
    }

    private static AutorizarRetiradaRequest Pedido(string? nome = "Maria", string? documento = "12345678900") =>
        new() { Nome = nome, Documento = documento };

    private Solicitacao Recarregar(int id) => _db.Solicitacoes.AsNoTracking().Single(x => x.Id == id);

    [Theory]
    [InlineData("1001", "Comum")]
    [InlineData("admin123", "Administrativo")]
    public void Dono_ou_admin_autoriza(string matricula, string perfil)
    {
        var s = Criar();

        _handler.Executar(s.Id, Pedido(" Maria ", " 123.456.789-00 "), matricula, perfil);

        var gravada = Recarregar(s.Id);
        Assert.Equal(StatusSolicitacao.RetiradaAutorizada, gravada.Status);
        Assert.Equal("Maria", gravada.AutorizadoNome);
        Assert.Equal("123.456.789-00", gravada.AutorizadoDocumento);
        var h = Assert.Single(_db.HistoricoStatus.AsNoTracking().Where(x => x.SolicitacaoId == s.Id));
        Assert.Equal(matricula, h.Matricula);
    }

    [Theory]
    [InlineData("2002", "Comum")]
    [InlineData("tec123", "Tecnico")]
    public void Outro_comum_ou_tecnico_403(string matricula, string perfil)
    {
        var s = Criar();

        Assert.Throws<UnauthorizedAccessException>(() => _handler.Executar(s.Id, Pedido(), matricula, perfil));

        Assert.Equal(StatusSolicitacao.AguardandoRetirada, Recarregar(s.Id).Status);
    }

    [Fact]
    public void Dono_verificado_antes_do_status()
    {
        var s = Criar(StatusSolicitacao.Concluida);

        Assert.Throws<UnauthorizedAccessException>(() => _handler.Executar(s.Id, Pedido(), "2002", "Comum"));
    }

    [Theory]
    [InlineData(null, "123")]
    [InlineData("Maria", null)]
    [InlineData("Maria", "12 3")]
    [InlineData("Maria", "...")]
    [InlineData("Maria", "-/-")]
    public void Dados_invalidos_400(string? nome, string? documento)
    {
        var s = Criar();

        Assert.Throws<RegraDeNegocioException>(() => _handler.Executar(s.Id, Pedido(nome, documento), "1001", "Comum"));

        Assert.Null(Recarregar(s.Id).AutorizadoNome);
    }

    [Fact]
    public void Requisicao_nula_400()
    {
        var s = Criar();

        Assert.Throws<RegraDeNegocioException>(() => _handler.Executar(s.Id, null, "1001", "Comum"));
    }

    [Theory]
    [InlineData(StatusSolicitacao.Concluida)]
    [InlineData(StatusSolicitacao.RetiradaAutorizada)]
    [InlineData(StatusSolicitacao.Retirado)]
    public void Fora_de_aguardando_retirada_400(string origem)
    {
        var s = Criar(origem);

        Assert.Throws<RegraDeNegocioException>(() => _handler.Executar(s.Id, Pedido(), "1001", "Comum"));
    }

    [Fact]
    public void Inexistente_404()
    {
        Assert.Throws<SolicitacaoNaoEncontradaException>(() => _handler.Executar(424242, Pedido(), "admin123", "Administrativo"));
    }
}
