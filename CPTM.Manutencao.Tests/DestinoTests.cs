using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Features.Destino.Commands;
using CPTM.Manutencao.Api.Models;
using CPTM.Manutencao.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CPTM.Manutencao.Tests;

// CPTM-77 (US-10): origens do liberar/sem reparo e o inutilizar
public sealed class DestinoTests : IDisposable
{
    private readonly SqliteConnection _conexao = new("DataSource=:memory:");
    private readonly ManutencaoDbContext _db;
    private readonly WorkflowService _workflow;
    private readonly InutilizarEquipamentoHandler _inutilizar;

    public DestinoTests()
    {
        _conexao.Open();
        var opcoes = new DbContextOptionsBuilder<ManutencaoDbContext>().UseSqlite(_conexao).Options;
        _db = new ManutencaoDbContext(opcoes);
        _db.Database.EnsureCreated();
        var repositorio = new SolicitacaoRepository(_db);
        _workflow = new WorkflowService(repositorio);
        _inutilizar = new InutilizarEquipamentoHandler(repositorio, _workflow);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conexao.Dispose();
    }

    private Solicitacao Criar(string status)
    {
        var s = new Solicitacao { Descricao = "Teste", Status = status, MatriculaSolicitante = "1001" };
        _db.Solicitacoes.Add(s);
        _db.SaveChanges();
        return s;
    }

    private List<HistoricoStatus> Historico(int id) =>
        _db.HistoricoStatus.AsNoTracking().Where(h => h.SolicitacaoId == id).ToList();

    [Theory]
    [InlineData(StatusSolicitacao.Concluida)]
    [InlineData(StatusSolicitacao.NaoExecutada)]
    [InlineData(StatusSolicitacao.SemReparo)]
    public void Liberar_retirada_aceita_as_origens_da_us10(string origem)
    {
        var s = Criar(origem);

        _workflow.DisponibilizarParaRetirada(s.Id, "admin123");

        Assert.Equal(StatusSolicitacao.AguardandoRetirada, s.Status);
        var h = Assert.Single(Historico(s.Id));
        Assert.Equal(origem, h.StatusAnterior);
    }

    [Theory]
    [InlineData(StatusSolicitacao.NaoExecutada)]
    [InlineData(StatusSolicitacao.SemReparo)]
    [InlineData(StatusSolicitacao.AguardandoRetirada)]
    public void Sem_reparo_so_a_partir_de_concluida(string origem)
    {
        var s = Criar(origem);

        var erro = Assert.Throws<RegraDeNegocioException>(() => _workflow.CondenarSemReparo(s.Id, "admin123"));

        Assert.Contains("CONCLUIDA", erro.Message);
        Assert.Empty(Historico(s.Id));
    }

    [Fact]
    public void Inutilizar_sem_reparo_vai_para_inutilizado_com_historico()
    {
        var s = Criar(StatusSolicitacao.SemReparo);

        _inutilizar.Executar(s.Id, "admin123", null);

        Assert.Equal(StatusSolicitacao.Inutilizado, _db.Solicitacoes.AsNoTracking().Single(x => x.Id == s.Id).Status);
        var h = Assert.Single(Historico(s.Id));
        Assert.Equal(StatusSolicitacao.SemReparo, h.StatusAnterior);
        Assert.Equal(StatusSolicitacao.Inutilizado, h.StatusNovo);
        Assert.Equal(InutilizarEquipamentoHandler.ObservacaoPadrao, h.Observacao);
    }

    [Theory]
    [InlineData(StatusSolicitacao.Concluida)]
    [InlineData(StatusSolicitacao.AguardandoRetirada)]
    [InlineData(StatusSolicitacao.Inutilizado)]
    public void Inutilizar_fora_de_sem_reparo_400(string origem)
    {
        var s = Criar(origem);

        Assert.Throws<RegraDeNegocioException>(() => _inutilizar.Executar(s.Id, "admin123", null));

        Assert.Empty(Historico(s.Id));
    }

    [Fact]
    public void Inutilizar_inexistente_404()
    {
        Assert.Throws<SolicitacaoNaoEncontradaException>(() => _inutilizar.Executar(424242, "admin123", null));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("  ok  ", "ok")]
    public void Observacao_e_aparada_e_vazia_vira_nula(string? entrada, string? esperado)
    {
        Assert.Equal(esperado, ObservacaoDestino.Normalizar(entrada));
    }

    [Fact]
    public void Observacao_acima_do_limite_400()
    {
        Assert.Throws<RegraDeNegocioException>(() => ObservacaoDestino.Normalizar(new string('x', ObservacaoDestino.TamanhoMaximo + 1)));
    }
}
