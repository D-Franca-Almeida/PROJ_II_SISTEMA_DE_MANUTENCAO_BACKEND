using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using CPTM.Manutencao.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CPTM.Manutencao.Tests;

public sealed class RecebimentoSsmTests : IDisposable
{
    private static readonly DateTime T0 = new(2027, 3, 15, 13, 30, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _conexao = new("DataSource=:memory:");
    private readonly ManutencaoDbContext _db;
    private readonly RelogioFalso _relogio = new(T0);
    private readonly WorkflowService _servico;

    public RecebimentoSsmTests()
    {
        _conexao.Open();
        _db = NovoContexto();
        _db.Database.EnsureCreated();
        _servico = new WorkflowService(new SolicitacaoRepository(_db, _relogio), _relogio);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conexao.Dispose();
    }

    private ManutencaoDbContext NovoContexto() =>
        new(new DbContextOptionsBuilder<ManutencaoDbContext>().UseSqlite(_conexao).Options);

    private int Criar(string status = StatusSolicitacao.Aprovada, string? numeroSsm = null)
    {
        var s = new Solicitacao { Descricao = "Teste", Status = status, MatriculaSolicitante = "1001", NumeroSsm = numeroSsm };
        _db.Solicitacoes.Add(s);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return s.Id;
    }

    private Solicitacao Ler(int id) => _db.Solicitacoes.AsNoTracking().Single(x => x.Id == id);

    private List<HistoricoStatus> Historico(int id) =>
        _db.HistoricoStatus.AsNoTracking().Where(h => h.SolicitacaoId == id).OrderBy(h => h.Id).ToList();

    [Fact]
    public void Receber_aprovada_vira_recebida_com_ssm_data_e_uma_linha_de_historico()
    {
        var id = Criar();

        var r = _servico.ReceberNaOficina(id, "admin123");

        Assert.Equal("SSM-2027-000001", r.NumeroSsm);
        Assert.Equal(T0, r.DataRecebimento);
        var s = Ler(id);
        Assert.Equal(StatusSolicitacao.Recebida, s.Status);
        Assert.Equal("SSM-2027-000001", s.NumeroSsm);
        Assert.Equal(T0, s.DataRecebimento);
        Assert.Equal(DateTimeKind.Utc, s.DataRecebimento!.Value.Kind);
        var h = Assert.Single(Historico(id));
        Assert.Equal(StatusSolicitacao.Aprovada, h.StatusAnterior);
        Assert.Equal(StatusSolicitacao.Recebida, h.StatusNovo);
        Assert.Equal("admin123", h.Matricula);
        Assert.Equal("SSM SSM-2027-000001 gerada.", h.Observacao);
    }

    [Fact]
    public void Numeros_sao_sequenciais_por_ano_e_reiniciam_no_ano_seguinte()
    {
        var a = Criar();
        var b = Criar();
        var c = Criar();

        Assert.Equal("SSM-2027-000001", _servico.ReceberNaOficina(a, "admin123").NumeroSsm);
        Assert.Equal("SSM-2027-000002", _servico.ReceberNaOficina(b, "admin123").NumeroSsm);
        _relogio.Avancar(TimeSpan.FromDays(365));
        Assert.Equal("SSM-2028-000001", _servico.ReceberNaOficina(c, "admin123").NumeroSsm);
    }

    [Fact]
    public void Continua_a_partir_do_maior_numero_do_ano_incluindo_a_carga_inicial()
    {
        var relogio2026 = new RelogioFalso(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        var servico = new WorkflowService(new SolicitacaoRepository(_db, relogio2026), relogio2026);
        var id = Criar();

        Assert.Equal("SSM-2026-000003", servico.ReceberNaOficina(id, "admin123").NumeroSsm);
    }

    [Theory]
    [InlineData(StatusSolicitacao.Solicitada)]
    [InlineData(StatusSolicitacao.Reenviada)]
    [InlineData(StatusSolicitacao.Devolvida)]
    [InlineData(StatusSolicitacao.Recebida)]
    [InlineData(StatusSolicitacao.EmManutencao)]
    [InlineData(StatusSolicitacao.Cancelada)]
    [InlineData(StatusSolicitacao.Expirada)]
    public void Status_diferente_de_aprovada_400_sem_ssm_nem_historico(string status)
    {
        var id = Criar(status);

        var ex = Assert.Throws<RegraDeNegocioException>(() => _servico.ReceberNaOficina(id, "admin123"));

        Assert.Contains($"status atual: {status}", ex.Message);
        Assert.Null(Ler(id).NumeroSsm);
        Assert.Empty(Historico(id));
    }

    [Fact]
    public void Inexistente_lanca_nao_encontrada()
    {
        Assert.Throws<SolicitacaoNaoEncontradaException>(() => _servico.ReceberNaOficina(99999, "admin123"));
    }

    [Fact]
    public void Receber_duas_vezes_a_mesma_ssl_gera_um_unico_numero()
    {
        var id = Criar();
        _servico.ReceberNaOficina(id, "admin123");

        Assert.Throws<RegraDeNegocioException>(() => _servico.ReceberNaOficina(id, "admin123"));

        Assert.Equal("SSM-2027-000001", Ler(id).NumeroSsm);
        Assert.Single(Historico(id));
    }

    [Fact]
    public void Indice_unico_impede_numero_ssm_duplicado()
    {
        Criar(StatusSolicitacao.Recebida, "SSM-2027-000050");

        Assert.Throws<DbUpdateException>(() => Criar(StatusSolicitacao.Recebida, "SSM-2027-000050"));
    }

    [Fact]
    public void Colisao_de_numero_e_resolvida_com_nova_tentativa()
    {
        Criar(StatusSolicitacao.Recebida, "SSM-2027-000001");
        var id = Criar();
        var repo = new RepositorioComLeituraDesatualizada(new SolicitacaoRepository(_db, _relogio), leiturasDesatualizadas: 1);
        var servico = new WorkflowService(repo, _relogio);

        var r = servico.ReceberNaOficina(id, "admin123");

        Assert.Equal("SSM-2027-000002", r.NumeroSsm);
        Assert.Equal(2, repo.Consultas);
        Assert.Equal(StatusSolicitacao.Recebida, Ler(id).Status);
        Assert.Single(Historico(id));
    }

    [Fact]
    public void Colisao_persistente_desiste_apos_as_tentativas_sem_gravar()
    {
        Criar(StatusSolicitacao.Recebida, "SSM-2027-000001");
        var id = Criar();
        var repo = new RepositorioComLeituraDesatualizada(new SolicitacaoRepository(_db, _relogio), leiturasDesatualizadas: int.MaxValue);
        var servico = new WorkflowService(repo, _relogio);

        var ex = Assert.Throws<ConflitoDeConcorrenciaException>(() => servico.ReceberNaOficina(id, "admin123"));
        Assert.IsAssignableFrom<DbUpdateException>(ex.InnerException);

        Assert.Equal(WorkflowService.TentativasSsm, repo.Consultas);
        Assert.Equal(StatusSolicitacao.Aprovada, Ler(id).Status);
        Assert.Empty(Historico(id));
    }

    [Fact]
    public void Status_alterado_por_outra_operacao_entre_leitura_e_gravacao_lanca_conflito_e_nada_grava()
    {
        var id = Criar();
        using var outroContexto = NovoContexto();
        var outroWorkflow = new WorkflowService(new SolicitacaoRepository(outroContexto, _relogio), _relogio);
        var carregadaAntes = outroContexto.Solicitacoes.Single(x => x.Id == id);

        _servico.ReceberNaOficina(id, "admin123");

        Assert.Throws<ConflitoDeConcorrenciaException>(() =>
            outroWorkflow.Transicionar(carregadaAntes, StatusSolicitacao.Cancelada, "comum123"));
        Assert.Equal(StatusSolicitacao.Recebida, Ler(id).Status);
        Assert.Equal(StatusSolicitacao.Recebida, Assert.Single(Historico(id)).StatusNovo);
    }

    [Fact]
    public void Recebida_nao_pode_ser_cancelada_por_comum_nem_admin()
    {
        var id = Criar();
        _servico.ReceberNaOficina(id, "admin123");

        Assert.Throws<RegraDeNegocioException>(() => _servico.CancelarTriagem(id, "1001", "Comum"));
        Assert.Throws<RegraDeNegocioException>(() => _servico.CancelarTriagem(id, "admin123", "Administrativo"));
        Assert.Equal(StatusSolicitacao.Recebida, Ler(id).Status);
    }

    [Fact]
    public void Recebida_nao_e_candidata_a_expiracao()
    {
        var id = Criar();
        _servico.ReceberNaOficina(id, "admin123");
        _relogio.Avancar(TimeSpan.FromDays(60));
        var repo = new SolicitacaoRepository(_db, _relogio);

        var r = new ExpiracaoService(repo, _servico, _relogio, new PrazosOptions()).Executar();

        Assert.DoesNotContain(r.Solicitacoes, x => x.Id == id);
        Assert.Equal(StatusSolicitacao.Recebida, Ler(id).Status);
        Assert.Null(repo.ObterDetalhe(id)!.DiasParaExpirar);
    }

    [Fact]
    public void Detalhe_e_listagem_expoem_numero_ssm_e_data_de_recebimento()
    {
        var id = Criar();
        _servico.ReceberNaOficina(id, "admin123");
        var repo = new SolicitacaoRepository(_db, _relogio);

        var detalhe = repo.ObterDetalhe(id)!;
        var item = repo.Listar(StatusSolicitacao.Recebida, null).Single(x => x.Id == id);

        Assert.Equal("SSM-2027-000001", detalhe.NumeroSsm);
        Assert.Equal(T0, detalhe.DataRecebimento);
        Assert.Equal("SSM-2027-000001", item.NumeroSsm);
        Assert.Equal(T0, item.DataRecebimento);
    }

    private sealed class RepositorioComLeituraDesatualizada : ISolicitacaoRepository
    {
        private readonly ISolicitacaoRepository _real;
        private int _desatualizadasRestantes;

        public RepositorioComLeituraDesatualizada(ISolicitacaoRepository real, int leiturasDesatualizadas)
        {
            _real = real;
            _desatualizadasRestantes = leiturasDesatualizadas;
        }

        public int Consultas { get; private set; }

        public string? UltimoNumeroSsm(string prefixo)
        {
            Consultas++;
            if (_desatualizadasRestantes-- > 0) return null;
            return _real.UltimoNumeroSsm(prefixo);
        }

        public Solicitacao? ObterPorId(int id) => _real.ObterPorId(id);
        public List<SolicitacaoDto> Listar(string? status, string? matriculaSolicitante) => _real.Listar(status, matriculaSolicitante);
        public SolicitacaoDetalheDto? ObterDetalhe(int id) => _real.ObterDetalhe(id);
        public List<SolicitacaoDto> ListarRecebidas() => _real.ListarRecebidas();
        public List<CandidatoExpiracao> ObterCandidatosExpiracao() => _real.ObterCandidatosExpiracao();
        public void Salvar(Solicitacao solicitacao, HistoricoStatus historico) => _real.Salvar(solicitacao, historico);
    }
}
