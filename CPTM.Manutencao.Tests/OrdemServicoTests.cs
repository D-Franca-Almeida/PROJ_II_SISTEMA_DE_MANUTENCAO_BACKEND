using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using CPTM.Manutencao.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CPTM.Manutencao.Tests;

public sealed class OrdemServicoTests : IDisposable
{
    private static readonly DateTime T0 = new(2027, 5, 10, 9, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _conexao = new("DataSource=:memory:");
    private readonly ManutencaoDbContext _db;
    private readonly RelogioFalso _relogio = new(T0);
    private readonly WorkflowService _servico;
    private int _sequencialSsm;

    public OrdemServicoTests()
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

    private int Criar(string status = StatusSolicitacao.Recebida, DateTime? dataRecebimento = null)
    {
        var s = new Solicitacao
        {
            Descricao = "Teste", Status = status, MatriculaSolicitante = "1001", EquipamentoId = 3,
            NumeroSsm = status == StatusSolicitacao.Recebida ? $"SSM-2027-{++_sequencialSsm:D6}" : null,
            DataRecebimento = status == StatusSolicitacao.Recebida ? dataRecebimento ?? T0.AddDays(-1) : null
        };
        _db.Solicitacoes.Add(s);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return s.Id;
    }

    private List<OrdemServico> Ordens(int solicitacaoId) =>
        _db.OrdensServico.AsNoTracking().Where(o => o.SolicitacaoId == solicitacaoId).ToList();

    private List<HistoricoStatus> Historico(int id) =>
        _db.HistoricoStatus.AsNoTracking().Where(h => h.SolicitacaoId == id).OrderBy(h => h.Id).ToList();

    [Fact]
    public void Assumir_recebida_abre_osm_e_vai_para_em_manutencao_numa_unica_gravacao()
    {
        var id = Criar();

        var ordem = _servico.IniciarManutencao(id, " tec123 ");

        Assert.NotEqual(0, ordem.Id);
        Assert.NotNull(ordem.Solicitacao?.NumeroSsm);
        var salva = Assert.Single(Ordens(id));
        Assert.Equal(ordem.Id, salva.Id);
        Assert.Equal("tec123", salva.MatriculaTecnico);
        Assert.Equal(T0, salva.DataAbertura);
        Assert.Equal(DateTimeKind.Utc, salva.DataAbertura.Kind);
        Assert.Null(salva.DataConclusao);
        Assert.Null(salva.Laudo);
        Assert.Null(salva.MotivoNaoExecucao);

        var s = _db.Solicitacoes.AsNoTracking().Single(x => x.Id == id);
        Assert.Equal(StatusSolicitacao.EmManutencao, s.Status);
        Assert.Equal("tec123", s.MatriculaTecnico);
        Assert.Equal(T0, s.DataInicioManutencao);
        var h = Assert.Single(Historico(id));
        Assert.Equal(StatusSolicitacao.Recebida, h.StatusAnterior);
        Assert.Equal(StatusSolicitacao.EmManutencao, h.StatusNovo);
        Assert.Equal("tec123", h.Matricula);
    }

    [Theory]
    [InlineData(StatusSolicitacao.Aprovada)]
    [InlineData(StatusSolicitacao.Solicitada)]
    [InlineData(StatusSolicitacao.Reenviada)]
    [InlineData(StatusSolicitacao.Devolvida)]
    [InlineData(StatusSolicitacao.EmManutencao)]
    [InlineData(StatusSolicitacao.Concluida)]
    [InlineData(StatusSolicitacao.Cancelada)]
    [InlineData(StatusSolicitacao.Expirada)]
    public void Fora_de_recebida_lanca_regra_sem_osm_nem_historico(string status)
    {
        var id = Criar(status);

        var ex = Assert.Throws<RegraDeNegocioException>(() => _servico.IniciarManutencao(id, "tec123"));

        Assert.Equal($"Transição inválida: só é possível iniciar a manutenção de uma solicitação RECEBIDA na oficina (status atual: {status}).", ex.Message);
        Assert.Empty(Ordens(id));
        Assert.Empty(Historico(id));
    }

    [Fact]
    public void Inexistente_lanca_nao_encontrada()
    {
        Assert.Throws<SolicitacaoNaoEncontradaException>(() => _servico.IniciarManutencao(99999, "tec123"));
    }

    [Fact]
    public void Segundo_tecnico_depois_do_primeiro_recebe_regra_de_negocio()
    {
        var id = Criar();
        _servico.IniciarManutencao(id, "tec123");

        Assert.Throws<RegraDeNegocioException>(() => _servico.IniciarManutencao(id, "tec456"));

        Assert.Equal("tec123", Assert.Single(Ordens(id)).MatriculaTecnico);
    }

    [Fact]
    public void Dois_tecnicos_ao_mesmo_tempo_so_um_vence_e_o_outro_recebe_conflito_sem_gravar_nada()
    {
        var id = Criar();
        using var contextoB = NovoContexto();
        var servicoB = new WorkflowService(new SolicitacaoRepository(contextoB, _relogio), _relogio);
        Assert.Equal(StatusSolicitacao.Recebida, contextoB.Solicitacoes.Find(id)!.Status);

        _servico.IniciarManutencao(id, "tec123");
        Assert.Throws<ConflitoDeConcorrenciaException>(() => servicoB.IniciarManutencao(id, "tec456"));

        Assert.Equal("tec123", Assert.Single(Ordens(id)).MatriculaTecnico);
        var s = _db.Solicitacoes.AsNoTracking().Single(x => x.Id == id);
        Assert.Equal("tec123", s.MatriculaTecnico);
        Assert.Single(Historico(id));
    }

    [Fact]
    public void Indice_unico_impede_duas_osms_para_a_mesma_solicitacao()
    {
        var id = Criar(StatusSolicitacao.EmManutencao);
        _db.OrdensServico.Add(new OrdemServico { SolicitacaoId = id, MatriculaTecnico = "tec123", DataAbertura = T0 });
        _db.SaveChanges();

        _db.OrdensServico.Add(new OrdemServico { SolicitacaoId = id, MatriculaTecnico = "tec456", DataAbertura = T0 });
        Assert.Throws<DbUpdateException>(() => _db.SaveChanges());
    }

    [Fact]
    public void Minhas_osms_traz_so_as_do_tecnico_mais_recentes_primeiro_com_resumo_da_ssl()
    {
        var a = Criar();
        var b = Criar();
        var c = Criar();
        _servico.IniciarManutencao(a, "tec123");
        _relogio.Avancar(TimeSpan.FromHours(1));
        _servico.IniciarManutencao(b, "tec123");
        _servico.IniciarManutencao(c, "tec456");
        var repo = new OrdemServicoRepository(_db);

        var minhas = repo.ListarDoTecnico("tec123");

        Assert.Equal(new[] { b, a, 11, 10 }, minhas.Select(o => o.SolicitacaoId));
        Assert.All(minhas, o => Assert.Equal("tec123", o.MatriculaTecnico));
        var primeira = minhas[0];
        Assert.Equal(StatusSolicitacao.EmManutencao, primeira.StatusSolicitacao);
        Assert.StartsWith("SSM-", primeira.NumeroSsm);
        Assert.Equal(3, primeira.Equipamento!.Id);
        Assert.Equal("Luz", primeira.Estacao!.Nome);
        Assert.Equal(T0.AddHours(1), primeira.DataAbertura);
        var concluida = minhas.Single(o => o.SolicitacaoId == 11);
        Assert.Equal(StatusSolicitacao.Concluida, concluida.StatusSolicitacao);
        Assert.NotNull(concluida.DataConclusao);
        Assert.Equal(c, Assert.Single(repo.ListarDoTecnico("tec456")).SolicitacaoId);
        Assert.Empty(repo.ListarDoTecnico("ninguem"));
    }

    [Fact]
    public void Fila_do_tecnico_traz_so_recebidas_mais_antigas_na_oficina_primeiro()
    {
        var recente = Criar(dataRecebimento: T0.AddHours(-1));
        var antiga = Criar(dataRecebimento: T0.AddDays(-3));
        var aprovada = Criar(StatusSolicitacao.Aprovada);
        var repo = new SolicitacaoRepository(_db, _relogio);

        var fila = repo.ListarRecebidas();

        Assert.All(fila, s => Assert.Equal(StatusSolicitacao.Recebida, s.Status));
        Assert.Equal(new[] { antiga, recente }, fila.Select(s => s.Id));
        Assert.DoesNotContain(fila, s => s.Id == aprovada);
        Assert.All(fila, s => Assert.NotNull(s.NumeroSsm));

        _servico.IniciarManutencao(antiga, "tec123");
        Assert.Equal(new[] { recente }, repo.ListarRecebidas().Select(s => s.Id));
    }

    [Fact]
    public void Detalhe_expoe_id_da_osm()
    {
        var id = Criar();
        var ordem = _servico.IniciarManutencao(id, "tec123");
        var repo = new SolicitacaoRepository(_db, _relogio);

        Assert.Equal(ordem.Id, repo.ObterDetalhe(id)!.OrdemServicoId);
        Assert.Equal(1, repo.ObterDetalhe(10)!.OrdemServicoId);
        Assert.Null(repo.ObterDetalhe(1)!.OrdemServicoId);
    }

    [Fact]
    public void Carga_inicial_tem_osm_para_as_ssls_ja_na_oficina()
    {
        var ordens = _db.OrdensServico.AsNoTracking().OrderBy(o => o.Id).ToList();

        Assert.Equal(new[] { 10, 11 }, ordens.Select(o => o.SolicitacaoId));
        Assert.All(ordens, o => Assert.Equal("tec123", o.MatriculaTecnico));
        Assert.All(_db.Solicitacoes.AsNoTracking().Where(s => s.Id == 10 || s.Id == 11).ToList(),
            s => Assert.Equal("tec123", s.MatriculaTecnico));
    }
}
