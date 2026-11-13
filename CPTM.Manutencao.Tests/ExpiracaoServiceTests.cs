using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using CPTM.Manutencao.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CPTM.Manutencao.Tests;

public sealed class ExpiracaoServiceTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _conexao = new("DataSource=:memory:");
    private readonly ManutencaoDbContext _db;
    private readonly RelogioFalso _relogio = new(T0);
    private readonly PrazosOptions _prazos = new() { Dias = 10 };

    public ExpiracaoServiceTests()
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

    private ExpiracaoService Servico()
    {
        var repo = new SolicitacaoRepository(_db, _relogio, _prazos);
        return new ExpiracaoService(repo, new WorkflowService(repo, _relogio), _relogio, _prazos);
    }

    private int Criar(string status, DateTime? entrouNoStatus = null)
    {
        var s = new Solicitacao { Descricao = "Teste", Status = status, MatriculaSolicitante = "comum123", DataAbertura = T0.AddDays(-60) };
        _db.Solicitacoes.Add(s);
        if (entrouNoStatus is not null)
            _db.HistoricoStatus.Add(new HistoricoStatus
            {
                Solicitacao = s, StatusAnterior = StatusSolicitacao.Solicitada, StatusNovo = status,
                Matricula = "admin123", Data = entrouNoStatus.Value
            });
        _db.SaveChanges();
        return s.Id;
    }

    private string StatusDe(int id) => _db.Solicitacoes.AsNoTracking().Single(s => s.Id == id).Status;

    private List<HistoricoStatus> Historico(int id) =>
        _db.HistoricoStatus.AsNoTracking().Where(h => h.SolicitacaoId == id).OrderBy(h => h.Id).ToList();

    [Theory]
    [InlineData("DEVOLVIDA")]
    [InlineData("APROVADA")]
    public void Expira_apos_o_prazo_com_sistema_e_observacao(string status)
    {
        var id = Criar(status, T0.AddDays(-11));

        var r = Servico().Executar();

        Assert.Equal(1, r.Quantidade);
        Assert.Equal(id, Assert.Single(r.Solicitacoes).Id);
        Assert.Equal(status, r.Solicitacoes[0].StatusAnterior);
        Assert.Equal("EXPIRADA", StatusDe(id));
        var h = Historico(id).Last();
        Assert.Equal(status, h.StatusAnterior);
        Assert.Equal("EXPIRADA", h.StatusNovo);
        Assert.Equal(HistoricoStatus.MatriculaSistema, h.Matricula);
        Assert.Contains("10 dias", h.Observacao);
        Assert.Equal(T0, h.Data);
    }

    [Fact]
    public void Nao_expira_no_limite_exato_e_expira_depois_de_avancar_o_relogio()
    {
        var id = Criar("DEVOLVIDA", T0.AddDays(-10));

        Assert.Equal(0, Servico().Executar().Quantidade);
        Assert.Equal("DEVOLVIDA", StatusDe(id));

        _relogio.Avancar(TimeSpan.FromMinutes(1));

        Assert.Equal(1, Servico().Executar().Quantidade);
        Assert.Equal("EXPIRADA", StatusDe(id));
    }

    [Fact]
    public void Prazo_configuravel()
    {
        _prazos.Dias = 3;
        var id = Criar("APROVADA", T0.AddDays(-4));

        Assert.Equal(1, Servico().Executar().Quantidade);
        Assert.Equal("EXPIRADA", StatusDe(id));
    }

    [Theory]
    [InlineData("SOLICITADA")]
    [InlineData("REENVIADA")]
    [InlineData("EM MANUTENCAO")]
    [InlineData("CONCLUIDA")]
    [InlineData("CANCELADA")]
    [InlineData("EXPIRADA")]
    public void Outros_status_nunca_expiram(string status)
    {
        var id = Criar(status, T0.AddDays(-100));

        Assert.Equal(0, Servico().Executar().Quantidade);
        Assert.Equal(status, StatusDe(id));
    }

    [Fact]
    public void Usa_a_data_da_ultima_entrada_no_status_atual()
    {
        var id = Criar("DEVOLVIDA", T0.AddDays(-30));
        _db.HistoricoStatus.Add(new HistoricoStatus
        {
            SolicitacaoId = id, StatusAnterior = "REENVIADA", StatusNovo = "DEVOLVIDA", Matricula = "admin123", Data = T0.AddDays(-2)
        });
        _db.SaveChanges();

        Assert.Equal(0, Servico().Executar().Quantidade);
    }

    [Fact]
    public void Sem_historico_usa_data_de_abertura()
    {
        var id = Criar("APROVADA");

        Assert.Equal(1, Servico().Executar().Quantidade);
        Assert.Equal("EXPIRADA", StatusDe(id));
    }

    [Fact]
    public void Segunda_execucao_nao_expira_de_novo()
    {
        var id = Criar("DEVOLVIDA", T0.AddDays(-11));

        Assert.Equal(1, Servico().Executar().Quantidade);
        Assert.Equal(0, Servico().Executar().Quantidade);
        Assert.Equal(2, Historico(id).Count);
    }

    [Fact]
    public void Execucao_manual_registra_quem_acionou_mas_grava_sistema()
    {
        var id = Criar("DEVOLVIDA", T0.AddDays(-11));

        Servico().Executar("admin123");

        var h = Historico(id).Last();
        Assert.Equal(HistoricoStatus.MatriculaSistema, h.Matricula);
        Assert.Contains("admin123", h.Observacao);
    }

    [Fact]
    public void DiasParaExpirar_calculado_na_listagem_apenas_para_devolvida_e_aprovada()
    {
        var devolvida = Criar("DEVOLVIDA", T0.AddDays(-8));
        var aprovada = Criar("APROVADA", T0.AddDays(-20));
        var solicitada = Criar("SOLICITADA", T0.AddDays(-8));
        var repo = new SolicitacaoRepository(_db, _relogio, _prazos);

        var lista = repo.Listar(null, null).ToDictionary(x => x.Id);

        Assert.Equal(2, lista[devolvida].DiasParaExpirar);
        Assert.Equal(0, lista[aprovada].DiasParaExpirar);
        Assert.Null(lista[solicitada].DiasParaExpirar);
        Assert.Equal(2, repo.ObterDetalhe(devolvida)!.DiasParaExpirar);
    }

    [Fact]
    public void Transicionar_para_o_mesmo_status_e_recusado_e_nao_grava_historico()
    {
        var id = Criar("APROVADA");
        var workflow = new WorkflowService(new SolicitacaoRepository(_db));
        var s = _db.Solicitacoes.Single(x => x.Id == id);

        Assert.Throws<RegraDeNegocioException>(() => workflow.Transicionar(s, "APROVADA", "admin123"));
        Assert.Empty(Historico(id));
    }

    [Fact]
    public void Falha_em_uma_solicitacao_nao_interrompe_as_demais()
    {
        var primeira = Criar("DEVOLVIDA", T0.AddDays(-11));
        var segunda = Criar("APROVADA", T0.AddDays(-11));
        var repo = new SolicitacaoRepository(_db, _relogio, _prazos);
        var workflow = new WorkflowQueFalha(new WorkflowService(repo, _relogio), primeira);

        var r = new ExpiracaoService(repo, workflow, _relogio, _prazos).Executar();

        Assert.Equal(segunda, Assert.Single(r.Solicitacoes).Id);
        Assert.Equal("DEVOLVIDA", StatusDe(primeira));
        Assert.Equal("EXPIRADA", StatusDe(segunda));
    }

    private sealed class WorkflowQueFalha : IWorkflowService
    {
        private readonly IWorkflowService _real;
        private readonly int _idQueFalha;

        public WorkflowQueFalha(IWorkflowService real, int idQueFalha)
        {
            _real = real;
            _idQueFalha = idQueFalha;
        }

        public void AprovarTriagem(int id, string matriculaLogada) => _real.AprovarTriagem(id, matriculaLogada);
        public void DevolverTriagem(int id, string motivo, string matriculaLogada) => _real.DevolverTriagem(id, motivo, matriculaLogada);
        public void CancelarTriagem(int id, string matriculaLogada, string perfilLogado) => _real.CancelarTriagem(id, matriculaLogada, perfilLogado);
        public OrdemServico IniciarManutencao(int id, string matriculaTecnico) => _real.IniciarManutencao(id, matriculaTecnico);
        public RecebimentoResultado ReceberNaOficina(int id, string matriculaAdmin) => _real.ReceberNaOficina(id, matriculaAdmin);
        public EncerramentoOsm ConcluirManutencao(int id, string? laudo, string matriculaTecnico) => _real.ConcluirManutencao(id, laudo, matriculaTecnico);
        public EncerramentoOsm NaoExecutarManutencao(int id, string? motivo, string matriculaTecnico) => _real.NaoExecutarManutencao(id, motivo, matriculaTecnico);
        public void DisponibilizarParaRetirada(int id, string matriculaAdmin, string? observacao = null) => _real.DisponibilizarParaRetirada(id, matriculaAdmin, observacao);
        public void CondenarSemReparo(int id, string matriculaAdmin, string? observacao = null) => _real.CondenarSemReparo(id, matriculaAdmin, observacao);
        public void RegistrarRetirada(int id, string matriculaAdmin, string identificacaoRecebedor) => _real.RegistrarRetirada(id, matriculaAdmin, identificacaoRecebedor);

        public void Transicionar(Solicitacao solicitacao, string novoStatus, string matricula, string? observacao = null)
        {
            if (solicitacao.Id == _idQueFalha) throw new InvalidOperationException("falha simulada");
            _real.Transicionar(solicitacao, novoStatus, matricula, observacao);
        }
    }
}
