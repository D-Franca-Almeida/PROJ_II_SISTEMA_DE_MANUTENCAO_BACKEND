using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Features.Dashboard.Queries;
using CPTM.Manutencao.Api.Models;
using CPTM.Manutencao.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CPTM.Manutencao.Tests;

// CPTM-90 (US-27): contagem por status, tempos médios entre etapas e SSLs perto de expirar, sobre uma massa conhecida
public sealed class DashboardTests : IDisposable
{
    private static readonly DateTime T0 = new(2027, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _conexao = new("DataSource=:memory:");
    private readonly ManutencaoDbContext _db;
    private readonly ObterDashboardHandler _handler;

    public DashboardTests()
    {
        _conexao.Open();
        _db = new ManutencaoDbContext(new DbContextOptionsBuilder<ManutencaoDbContext>().UseSqlite(_conexao).Options);
        _db.Database.EnsureCreated();
        _db.HistoricoStatus.ExecuteDelete();
        _db.OrdensServico.ExecuteDelete();
        _db.Solicitacoes.ExecuteDelete();
        _handler = new ObterDashboardHandler(new DashboardRepository(_db), new SolicitacaoRepository(_db, new RelogioFalso(T0)));
    }

    public void Dispose()
    {
        _db.Dispose();
        _conexao.Dispose();
    }

    private readonly List<int> _ids = new();

    private void Ssl(string statusAtual, params (string? De, string Para, DateTime Data)[] historico)
    {
        var s = new Solicitacao { Descricao = "Teste", Status = statusAtual, MatriculaSolicitante = "1001", EquipamentoId = 3, DataAbertura = historico[0].Data };
        _db.Solicitacoes.Add(s);
        _db.SaveChanges();
        _ids.Add(s.Id);
        _db.HistoricoStatus.AddRange(historico.Select(h => new HistoricoStatus
        {
            SolicitacaoId = s.Id, StatusAnterior = h.De, StatusNovo = h.Para, Matricula = "x", Data = h.Data
        }));
        _db.SaveChanges();
    }

    private void MassaConhecida()
    {
        var abertura = T0.AddDays(-30);
        Ssl(StatusSolicitacao.Concluida,
            (null, "SOLICITADA", abertura), ("SOLICITADA", "APROVADA", abertura.AddHours(2)),
            ("APROVADA", "RECEBIDA", abertura.AddHours(12)), ("RECEBIDA", "EM MANUTENCAO", abertura.AddHours(13)),
            ("EM MANUTENCAO", "CONCLUIDA", abertura.AddHours(32)));
        Ssl(StatusSolicitacao.Concluida,
            (null, "SOLICITADA", abertura), ("SOLICITADA", "APROVADA", abertura.AddHours(4)),
            ("APROVADA", "RECEBIDA", abertura.AddHours(34)), ("RECEBIDA", "CONCLUIDA", abertura.AddHours(74)));
        Ssl(StatusSolicitacao.Aprovada,
            (null, "SOLICITADA", T0.AddDays(-9)), ("SOLICITADA", "APROVADA", T0.AddDays(-9).AddHours(12)));
        Ssl(StatusSolicitacao.Devolvida,
            (null, "SOLICITADA", T0.AddDays(-10)), ("SOLICITADA", "DEVOLVIDA", T0.AddDays(-9)));
        Ssl(StatusSolicitacao.Aprovada,
            (null, "SOLICITADA", T0.AddDays(-2)), ("SOLICITADA", "APROVADA", T0.AddDays(-1)));
        Ssl(StatusSolicitacao.Devolvida,
            (null, "SOLICITADA", T0.AddDays(-13)), ("SOLICITADA", "DEVOLVIDA", T0.AddDays(-12)));
        Ssl(StatusSolicitacao.Solicitada, (null, "SOLICITADA", T0.AddDays(-1)));
        Ssl(StatusSolicitacao.Cancelada, (null, "SOLICITADA", T0.AddDays(-1)), ("SOLICITADA", "CANCELADA", T0.AddHours(-20)));
    }

    [Fact]
    public void Conta_por_status_com_zero_nos_status_sem_ssl()
    {
        MassaConhecida();

        var dashboard = _handler.Executar();

        Assert.Equal(8, dashboard.TotalSolicitacoes);
        Assert.Equal(StatusSolicitacao.Todos, dashboard.PorStatus.Select(c => c.Status));
        var porStatus = dashboard.PorStatus.ToDictionary(c => c.Status, c => c.Quantidade);
        Assert.Equal(2, porStatus["CONCLUIDA"]);
        Assert.Equal(2, porStatus["APROVADA"]);
        Assert.Equal(2, porStatus["DEVOLVIDA"]);
        Assert.Equal(1, porStatus["SOLICITADA"]);
        Assert.Equal(1, porStatus["CANCELADA"]);
        Assert.Equal(0, porStatus["RETIRADO"]);
        Assert.Equal(8, porStatus.Values.Sum());
    }

    [Fact]
    public void Tempo_medio_de_cada_etapa_sai_do_historico()
    {
        MassaConhecida();

        var tempos = _handler.Executar().TemposMedios;

        Assert.Equal(3, tempos.Count);
        Assert.Equal(new TempoMedioEtapaDto("SOLICITADA", "APROVADA", 10.5, 4), tempos[0]);
        Assert.Equal(new TempoMedioEtapaDto("APROVADA", "RECEBIDA", 20.0, 2), tempos[1]);
        Assert.Equal(new TempoMedioEtapaDto("RECEBIDA", "CONCLUIDA", 30.0, 2), tempos[2]);
    }

    [Fact]
    public void Lista_as_que_expiram_em_ate_2_dias_da_mais_urgente_para_a_menos()
    {
        MassaConhecida();

        var lista = _handler.Executar().ExpiramEmAteDoisDias;

        Assert.Equal(new[] { _ids[5], _ids[3], _ids[2] }, lista.Select(s => s.Id));
        Assert.Equal(new int?[] { 0, 1, 2 }, lista.Select(s => s.DiasParaExpirar));
        Assert.Equal(new[] { "DEVOLVIDA", "DEVOLVIDA", "APROVADA" }, lista.Select(s => s.Status));
    }

    [Fact]
    public void Sem_ssl_tudo_zerado_e_media_nula()
    {
        var dashboard = _handler.Executar();

        Assert.Equal(0, dashboard.TotalSolicitacoes);
        Assert.All(dashboard.PorStatus, c => Assert.Equal(0, c.Quantidade));
        Assert.All(dashboard.TemposMedios, t => { Assert.Null(t.MediaHoras); Assert.Equal(0, t.Quantidade); });
        Assert.Empty(dashboard.ExpiramEmAteDoisDias);
    }

    [Fact]
    public void Etapa_conta_a_primeira_entrada_e_ignora_ssl_que_nao_passou_pelos_dois_status()
    {
        var abertura = T0.AddDays(-5);
        Ssl(StatusSolicitacao.Aprovada,
            (null, "SOLICITADA", abertura), ("SOLICITADA", "DEVOLVIDA", abertura.AddHours(1)), ("DEVOLVIDA", "REENVIADA", abertura.AddHours(5)),
            ("REENVIADA", "APROVADA", abertura.AddHours(8)));
        Ssl(StatusSolicitacao.Devolvida, (null, "SOLICITADA", abertura), ("SOLICITADA", "DEVOLVIDA", abertura.AddHours(1)));

        var tempo = _handler.Executar().TemposMedios[0];

        Assert.Equal(new TempoMedioEtapaDto("SOLICITADA", "APROVADA", 8.0, 1), tempo);
    }
}
