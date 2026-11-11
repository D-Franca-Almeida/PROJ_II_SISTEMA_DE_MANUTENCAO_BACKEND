using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using CPTM.Manutencao.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CPTM.Manutencao.Tests;

public sealed class WorkflowServiceTests : IDisposable
{
    private readonly SqliteConnection _conexao = new("DataSource=:memory:");
    private readonly ManutencaoDbContext _db;
    private readonly WorkflowService _servico;

    public WorkflowServiceTests()
    {
        _conexao.Open();
        var opcoes = new DbContextOptionsBuilder<ManutencaoDbContext>().UseSqlite(_conexao).Options;
        _db = new ManutencaoDbContext(opcoes);
        _db.Database.EnsureCreated();
        _servico = new WorkflowService(new SolicitacaoRepository(_db));
    }

    public void Dispose()
    {
        _db.Dispose();
        _conexao.Dispose();
    }

    private Solicitacao Criar(string status = StatusSolicitacao.Solicitada, string solicitante = "1001")
    {
        var s = new Solicitacao { Descricao = "Teste", Status = status, MatriculaSolicitante = solicitante };
        _db.Solicitacoes.Add(s);
        _db.SaveChanges();
        return s;
    }

    private List<HistoricoStatus> Historico(int solicitacaoId) =>
        _db.HistoricoStatus.AsNoTracking().Where(h => h.SolicitacaoId == solicitacaoId).OrderBy(h => h.Id).ToList();

    [Fact]
    public void Abertura_ComIdZero_GravaStatusAnteriorNulo()
    {
        var nova = new Solicitacao { Descricao = "Nova", MatriculaSolicitante = "1001" };

        _servico.Transicionar(nova, StatusSolicitacao.Solicitada, "1001");

        Assert.NotEqual(0, nova.Id);
        var h = Assert.Single(Historico(nova.Id));
        Assert.Null(h.StatusAnterior);
        Assert.Equal(StatusSolicitacao.Solicitada, h.StatusNovo);
        Assert.Equal("1001", h.Matricula);
        Assert.Null(h.Observacao);
    }

    [Fact]
    public void Aprovar_GravaUmaLinhaComDadosCorretos()
    {
        var s = Criar();
        var antes = DateTime.UtcNow.AddSeconds(-1);

        _servico.AprovarTriagem(s.Id, "2002");

        var h = Assert.Single(Historico(s.Id));
        Assert.Equal(StatusSolicitacao.Solicitada, h.StatusAnterior);
        Assert.Equal(StatusSolicitacao.Aprovada, h.StatusNovo);
        Assert.Equal("2002", h.Matricula);
        Assert.True(h.Data >= antes && h.Data <= DateTime.UtcNow.AddSeconds(1));
        Assert.Null(h.Observacao);
        Assert.Equal(StatusSolicitacao.Aprovada, _db.Solicitacoes.AsNoTracking().Single(x => x.Id == s.Id).Status);
    }

    [Fact]
    public void Devolver_GravaMotivoComoObservacao()
    {
        var s = Criar();

        _servico.DevolverTriagem(s.Id, "  Faltou foto  ", "2002");

        var h = Assert.Single(Historico(s.Id));
        Assert.Equal(StatusSolicitacao.Solicitada, h.StatusAnterior);
        Assert.Equal(StatusSolicitacao.Devolvida, h.StatusNovo);
        Assert.Equal("2002", h.Matricula);
        Assert.Equal("Faltou foto", h.Observacao);
    }

    [Fact]
    public void Cancelar_PeloSolicitante_GravaUmaLinha()
    {
        var s = Criar(solicitante: "1001");

        _servico.CancelarTriagem(s.Id, "1001", "Comum");

        var h = Assert.Single(Historico(s.Id));
        Assert.Equal(StatusSolicitacao.Cancelada, h.StatusNovo);
        Assert.Equal("1001", h.Matricula);
    }

    [Fact]
    public void IniciarManutencao_GravaUmaLinhaComMatriculaDoTecnico()
    {
        var s = Criar(StatusSolicitacao.Aprovada);

        _servico.IniciarManutencao(s.Id, "3003");

        var h = Assert.Single(Historico(s.Id));
        Assert.Equal(StatusSolicitacao.Aprovada, h.StatusAnterior);
        Assert.Equal(StatusSolicitacao.EmManutencao, h.StatusNovo);
        Assert.Equal("3003", h.Matricula);
    }

    [Fact]
    public void FluxoCompleto_GravaUmaLinhaPorTransicao()
    {
        var s = Criar();
        _servico.DevolverTriagem(s.Id, "Corrigir", "2002");
        s = _db.Solicitacoes.Single(x => x.Id == s.Id);
        s.Status = StatusSolicitacao.Reenviada;
        _db.SaveChanges();
        _servico.AprovarTriagem(s.Id, "2002");
        _servico.IniciarManutencao(s.Id, "3003");

        var linhas = Historico(s.Id);
        Assert.Equal(3, linhas.Count);
        Assert.Equal(
            new[] { StatusSolicitacao.Devolvida, StatusSolicitacao.Aprovada, StatusSolicitacao.EmManutencao },
            linhas.Select(l => l.StatusNovo));
    }

    [Fact]
    public void TransicaoInvalida_NaoGravaHistorico()
    {
        var s = Criar(StatusSolicitacao.Aprovada);

        Assert.Throws<RegraDeNegocioException>(() => _servico.AprovarTriagem(s.Id, "2002"));
        Assert.Throws<RegraDeNegocioException>(() => _servico.DevolverTriagem(s.Id, "x", "2002"));
        Assert.Throws<RegraDeNegocioException>(() => _servico.CancelarTriagem(s.Id, "1001", "Administrativo"));

        Assert.Empty(Historico(s.Id));
        Assert.Equal(StatusSolicitacao.Aprovada, _db.Solicitacoes.AsNoTracking().Single(x => x.Id == s.Id).Status);
    }

    [Fact]
    public void IniciarManutencao_StatusErrado_NaoGravaHistorico()
    {
        var s = Criar(StatusSolicitacao.Solicitada);

        Assert.Throws<RegraDeNegocioException>(() => _servico.IniciarManutencao(s.Id, "3003"));

        Assert.Empty(Historico(s.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Devolver_SemMotivo_NaoGravaHistorico(string motivo)
    {
        var s = Criar();

        Assert.Throws<RegraDeNegocioException>(() => _servico.DevolverTriagem(s.Id, motivo, "2002"));

        Assert.Empty(Historico(s.Id));
    }

    [Fact]
    public void Cancelar_SolicitacaoDeOutro_ComoComum_LancaProibidoENaoGravaHistorico()
    {
        var s = Criar(solicitante: "1001");

        Assert.Throws<UnauthorizedAccessException>(() => _servico.CancelarTriagem(s.Id, "9999", "Comum"));

        Assert.Empty(Historico(s.Id));
    }

    [Fact]
    public void Cancelar_Administrativo_PodeCancelarDeQualquerUm()
    {
        var s = Criar(solicitante: "1001");

        _servico.CancelarTriagem(s.Id, "2002", "Administrativo");

        Assert.Equal(StatusSolicitacao.Cancelada, Assert.Single(Historico(s.Id)).StatusNovo);
    }

    [Fact]
    public void SolicitacaoInexistente_LancaNaoEncontradaENaoGravaHistorico()
    {
        var antes = _db.HistoricoStatus.Count();

        Assert.Throws<SolicitacaoNaoEncontradaException>(() => _servico.AprovarTriagem(99999, "2002"));
        Assert.Throws<SolicitacaoNaoEncontradaException>(() => _servico.DevolverTriagem(99999, "x", "2002"));
        Assert.Throws<SolicitacaoNaoEncontradaException>(() => _servico.CancelarTriagem(99999, "1001", "Comum"));
        Assert.Throws<SolicitacaoNaoEncontradaException>(() => _servico.IniciarManutencao(99999, "3003"));

        Assert.Equal(antes, _db.HistoricoStatus.Count());
    }

    [Fact]
    public void Transicionar_SemMatricula_NaoGravaHistorico()
    {
        var s = Criar();
        var antes = _db.HistoricoStatus.Count();

        Assert.Throws<RegraDeNegocioException>(() => _servico.Transicionar(s, StatusSolicitacao.Aprovada, " "));

        Assert.Equal(antes, _db.HistoricoStatus.Count());
    }

    [Fact]
    public void TransicaoAutomatica_UsaMatriculaSistema()
    {
        var s = Criar(StatusSolicitacao.EmManutencao);

        _servico.Transicionar(s, StatusSolicitacao.Concluida, HistoricoStatus.MatriculaSistema);

        Assert.Equal("SISTEMA", Assert.Single(Historico(s.Id)).Matricula);
    }
}
