using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using CPTM.Manutencao.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CPTM.Manutencao.Tests;

public sealed class EncerramentoOsmTests : IDisposable
{
    private static readonly DateTime T0 = new(2027, 5, 10, 9, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _conexao = new("DataSource=:memory:");
    private readonly ManutencaoDbContext _db;
    private readonly RelogioFalso _relogio = new(T0);
    private readonly WorkflowService _servico;
    private int _sequencialSsm;

    public EncerramentoOsmTests()
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

    private int EmManutencao(string tecnico = "tec123")
    {
        var s = new Solicitacao
        {
            Descricao = "Teste", Status = StatusSolicitacao.Recebida, MatriculaSolicitante = "1001", EquipamentoId = 3,
            NumeroSsm = $"SSM-2027-{++_sequencialSsm:D6}", DataRecebimento = T0.AddDays(-1)
        };
        _db.Solicitacoes.Add(s);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        _servico.IniciarManutencao(s.Id, tecnico);
        _db.ChangeTracker.Clear();
        _relogio.Avancar(TimeSpan.FromHours(2));
        return s.Id;
    }

    private int Criar(string status)
    {
        var s = new Solicitacao { Descricao = "Teste", Status = status, MatriculaSolicitante = "1001", EquipamentoId = 3 };
        _db.Solicitacoes.Add(s);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return s.Id;
    }

    private (Solicitacao Solicitacao, OrdemServico? Ordem, List<HistoricoStatus> Historico) Estado(int id) =>
        (_db.Solicitacoes.AsNoTracking().Single(x => x.Id == id),
         _db.OrdensServico.AsNoTracking().SingleOrDefault(o => o.SolicitacaoId == id),
         _db.HistoricoStatus.AsNoTracking().Where(h => h.SolicitacaoId == id).OrderBy(h => h.Id).ToList());

    [Fact]
    public void Concluir_grava_laudo_e_data_na_osm_e_vai_para_concluida_com_um_historico()
    {
        var id = EmManutencao();

        var ordem = _servico.ConcluirManutencao(id, "  Placa substituída e testada.  ", " tec123 ");

        var (s, salva, historico) = Estado(id);
        Assert.Equal(StatusSolicitacao.Concluida, s.Status);
        Assert.Equal(ordem.Id, salva!.Id);
        Assert.Equal("Placa substituída e testada.", salva.Laudo);
        Assert.Null(salva.MotivoNaoExecucao);
        Assert.Equal(T0.AddHours(2), salva.DataConclusao);
        Assert.Equal(DateTimeKind.Utc, salva.DataConclusao!.Value.Kind);
        Assert.Equal(2, historico.Count);
        var h = historico[^1];
        Assert.Equal(StatusSolicitacao.EmManutencao, h.StatusAnterior);
        Assert.Equal(StatusSolicitacao.Concluida, h.StatusNovo);
        Assert.Equal("tec123", h.Matricula);
        Assert.Equal($"OSM {ordem.Id} concluída.", h.Observacao);
    }

    [Fact]
    public void Nao_executar_grava_motivo_e_data_e_vai_para_nao_executada_com_um_historico()
    {
        var id = EmManutencao();

        _servico.NaoExecutarManutencao(id, " Equipamento sem conserto viável. ", "tec123");

        var (s, ordem, historico) = Estado(id);
        Assert.Equal(StatusSolicitacao.NaoExecutada, s.Status);
        Assert.Equal("Equipamento sem conserto viável.", ordem!.MotivoNaoExecucao);
        Assert.Null(ordem.Laudo);
        Assert.Equal(T0.AddHours(2), ordem.DataConclusao);
        var h = historico[^1];
        Assert.Equal(StatusSolicitacao.EmManutencao, h.StatusAnterior);
        Assert.Equal(StatusSolicitacao.NaoExecutada, h.StatusNovo);
        Assert.Equal("Equipamento sem conserto viável.", h.Observacao);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Concluir_sem_laudo_lanca_regra_sem_gravar(string? laudo)
    {
        var id = EmManutencao();

        var ex = Assert.Throws<RegraDeNegocioException>(() => _servico.ConcluirManutencao(id, laudo, "tec123"));

        Assert.Equal("O laudo técnico é obrigatório para concluir a OSM.", ex.Message);
        var (s, ordem, historico) = Estado(id);
        Assert.Equal(StatusSolicitacao.EmManutencao, s.Status);
        Assert.Null(ordem!.Laudo);
        Assert.Null(ordem.DataConclusao);
        Assert.Single(historico);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Nao_executar_sem_motivo_lanca_regra_sem_gravar(string? motivo)
    {
        var id = EmManutencao();

        var ex = Assert.Throws<RegraDeNegocioException>(() => _servico.NaoExecutarManutencao(id, motivo, "tec123"));

        Assert.Equal("O motivo da não execução é obrigatório.", ex.Message);
        Assert.Equal(StatusSolicitacao.EmManutencao, Estado(id).Solicitacao.Status);
        Assert.Null(Estado(id).Ordem!.DataConclusao);
    }

    [Fact]
    public void Laudo_e_motivo_acima_do_limite_lancam_regra()
    {
        var id = EmManutencao();

        Assert.Throws<RegraDeNegocioException>(() =>
            _servico.ConcluirManutencao(id, new string('a', WorkflowService.TamanhoMaximoLaudo + 1), "tec123"));
        Assert.Throws<RegraDeNegocioException>(() =>
            _servico.NaoExecutarManutencao(id, new string('a', WorkflowService.TamanhoMaximoMotivoNaoExecucao + 1), "tec123"));

        Assert.Equal(StatusSolicitacao.EmManutencao, Estado(id).Solicitacao.Status);
        _servico.ConcluirManutencao(id, new string('a', WorkflowService.TamanhoMaximoLaudo), "tec123");
        Assert.Equal(StatusSolicitacao.Concluida, Estado(id).Solicitacao.Status);
    }

    [Fact]
    public void Outro_tecnico_nao_pode_encerrar_a_osm()
    {
        var id = EmManutencao("tec123");

        Assert.Throws<UnauthorizedAccessException>(() => _servico.ConcluirManutencao(id, "Laudo", "tec456"));
        Assert.Throws<UnauthorizedAccessException>(() => _servico.NaoExecutarManutencao(id, "Motivo", "tec456"));

        var (s, ordem, historico) = Estado(id);
        Assert.Equal(StatusSolicitacao.EmManutencao, s.Status);
        Assert.Null(ordem!.DataConclusao);
        Assert.Single(historico);
    }

    [Fact]
    public void Outro_tecnico_recebe_403_mesmo_com_a_osm_ja_encerrada()
    {
        var id = EmManutencao("tec123");
        _servico.ConcluirManutencao(id, "Laudo", "tec123");
        _db.ChangeTracker.Clear();

        Assert.Throws<UnauthorizedAccessException>(() => _servico.NaoExecutarManutencao(id, "Motivo", "tec456"));
    }

    [Fact]
    public void Encerrar_de_novo_lanca_regra_com_status_atual()
    {
        var id = EmManutencao();
        _servico.ConcluirManutencao(id, "Laudo", "tec123");
        _db.ChangeTracker.Clear();

        var concluir = Assert.Throws<RegraDeNegocioException>(() => _servico.ConcluirManutencao(id, "Outro", "tec123"));
        var naoExecutar = Assert.Throws<RegraDeNegocioException>(() => _servico.NaoExecutarManutencao(id, "Motivo", "tec123"));

        Assert.Equal("Transição inválida: só é possível concluir uma solicitação EM MANUTENCAO (status atual: CONCLUIDA).", concluir.Message);
        Assert.Equal("Transição inválida: só é possível registrar a não execução de uma solicitação EM MANUTENCAO (status atual: CONCLUIDA).", naoExecutar.Message);
        var (_, ordem, historico) = Estado(id);
        Assert.Equal("Laudo", ordem!.Laudo);
        Assert.Null(ordem.MotivoNaoExecucao);
        Assert.Equal(2, historico.Count);
    }

    [Theory]
    [InlineData(StatusSolicitacao.Solicitada)]
    [InlineData(StatusSolicitacao.Reenviada)]
    [InlineData(StatusSolicitacao.Devolvida)]
    [InlineData(StatusSolicitacao.Aprovada)]
    [InlineData(StatusSolicitacao.Recebida)]
    [InlineData(StatusSolicitacao.Cancelada)]
    [InlineData(StatusSolicitacao.Expirada)]
    public void Status_fora_de_em_manutencao_lanca_regra(string status)
    {
        var id = Criar(status);

        var ex = Assert.Throws<RegraDeNegocioException>(() => _servico.ConcluirManutencao(id, "Laudo", "tec123"));
        Assert.Throws<RegraDeNegocioException>(() => _servico.NaoExecutarManutencao(id, "Motivo", "tec123"));

        Assert.Contains($"(status atual: {status})", ex.Message);
        Assert.Equal(status, Estado(id).Solicitacao.Status);
        Assert.Empty(Estado(id).Historico);
    }

    [Fact]
    public void Inexistente_lanca_nao_encontrada()
    {
        Assert.Throws<SolicitacaoNaoEncontradaException>(() => _servico.ConcluirManutencao(99999, "Laudo", "tec123"));
        Assert.Throws<SolicitacaoNaoEncontradaException>(() => _servico.NaoExecutarManutencao(99999, "Motivo", "tec123"));
    }

    [Fact]
    public void Concluir_e_nao_executar_ao_mesmo_tempo_so_um_vence()
    {
        var id = EmManutencao();
        using var contextoB = NovoContexto();
        var servicoB = new WorkflowService(new SolicitacaoRepository(contextoB, _relogio), _relogio);
        Assert.NotNull(contextoB.Solicitacoes.Include(s => s.OrdemServico).Single(s => s.Id == id).OrdemServico);

        _servico.ConcluirManutencao(id, "Laudo", "tec123");
        Assert.Throws<ConflitoDeConcorrenciaException>(() => servicoB.NaoExecutarManutencao(id, "Motivo", "tec123"));

        var (s, ordem, historico) = Estado(id);
        Assert.Equal(StatusSolicitacao.Concluida, s.Status);
        Assert.Equal("Laudo", ordem!.Laudo);
        Assert.Null(ordem.MotivoNaoExecucao);
        Assert.Equal(2, historico.Count);
    }

    [Fact]
    public void Seed_em_manutencao_pode_ser_concluida_pelo_dono()
    {
        var id = _db.OrdensServico.AsNoTracking().Single(o => o.Id == 1).SolicitacaoId;

        _servico.ConcluirManutencao(id, "Laudo da carga", "tec123");

        Assert.Equal(StatusSolicitacao.Concluida, Estado(id).Solicitacao.Status);
    }

    [Fact]
    public void Nao_executada_entra_na_lista_de_status_validos()
    {
        Assert.Contains(StatusSolicitacao.NaoExecutada, StatusSolicitacao.Todos);
        Assert.Equal("NAO EXECUTADA", StatusSolicitacao.NaoExecutada);
    }
}
