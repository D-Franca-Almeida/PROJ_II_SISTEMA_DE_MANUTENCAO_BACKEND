using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Features.Notificacoes.Commands;
using CPTM.Manutencao.Api.Features.Notificacoes.Queries;
using CPTM.Manutencao.Api.Features.Retirada.Commands;
using CPTM.Manutencao.Api.Models;
using CPTM.Manutencao.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CPTM.Manutencao.Tests;

// CPTM-84 (US-25): geração das notificações nas transições e no pedido de peça, listagem e marcar como lida
public sealed class NotificacoesTests : IDisposable
{
    private static readonly DateTime T0 = new(2027, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    private const string Solicitante = "1001";
    private const string Admin = Notificacao.PerfilAdministrativo;

    private readonly SqliteConnection _conexao = new("DataSource=:memory:");
    private readonly ManutencaoDbContext _db;
    private readonly RelogioFalso _relogio = new(T0);
    private readonly WorkflowService _workflow;
    private readonly PedidoPecaService _pedidos;

    public NotificacoesTests()
    {
        _conexao.Open();
        _db = NovoContexto();
        _db.Database.EnsureCreated();
        _workflow = NovoWorkflow(_db);
        _pedidos = new PedidoPecaService(new PedidoPecaRepository(_db), _relogio, Gerador(_db));
    }

    public void Dispose()
    {
        _db.Dispose();
        _conexao.Dispose();
    }

    private ManutencaoDbContext NovoContexto() =>
        new(new DbContextOptionsBuilder<ManutencaoDbContext>().UseSqlite(_conexao).Options);

    private static GeradorDeNotificacoes Gerador(ManutencaoDbContext contexto) => new(new NotificacaoRepository(contexto));

    private WorkflowService NovoWorkflow(ManutencaoDbContext contexto, ISolicitacaoRepository? repositorio = null) =>
        new(repositorio ?? new SolicitacaoRepository(contexto, _relogio), _relogio, null, Gerador(contexto));

    private Solicitacao Ssl(string status, string tecnico = "tec123")
    {
        var s = new Solicitacao { Descricao = "Teste", Status = status, MatriculaSolicitante = Solicitante, EquipamentoId = 3 };
        if (status == StatusSolicitacao.EmManutencao)
        {
            s.MatriculaTecnico = tecnico;
            s.OrdemServico = new OrdemServico { MatriculaTecnico = tecnico, DataAbertura = T0.AddHours(-1) };
        }
        _db.Solicitacoes.Add(s);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return s;
    }

    private List<Notificacao> Gravadas() => _db.Notificacoes.AsNoTracking().OrderBy(n => n.Id).ToList();

    private Notificacao Gravar(string? matricula, string? perfil, DateTime data, bool lida = false)
    {
        var n = new Notificacao { MatriculaDestino = matricula, PerfilDestino = perfil, SolicitacaoId = 1, Mensagem = "x", Data = data, Lida = lida };
        _db.Notificacoes.Add(n);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return n;
    }

    // ---------- Geração nas transições ----------

    [Fact]
    public void Aprovar_notifica_o_solicitante_com_a_data_do_historico()
    {
        var s = Ssl(StatusSolicitacao.Solicitada);

        _workflow.AprovarTriagem(s.Id, "admin123");

        var n = Assert.Single(Gravadas());
        Assert.Equal(Solicitante, n.MatriculaDestino);
        Assert.Null(n.PerfilDestino);
        Assert.Equal(s.Id, n.SolicitacaoId);
        Assert.Equal($"Sua SSL {s.Id} foi aprovada. Leve o equipamento à oficina.", n.Mensagem);
        Assert.False(n.Lida);
        Assert.Equal(T0, n.Data);
        Assert.Equal(DateTimeKind.Utc, n.Data.Kind);
        Assert.Equal(_db.HistoricoStatus.AsNoTracking().Single(h => h.SolicitacaoId == s.Id).Data, n.Data);
    }

    [Theory]
    [InlineData(StatusSolicitacao.Solicitada, StatusSolicitacao.Devolvida, "foi devolvida para correção", null)]
    [InlineData(StatusSolicitacao.Aprovada, StatusSolicitacao.Cancelada, "foi cancelada", null)]
    [InlineData(StatusSolicitacao.Devolvida, StatusSolicitacao.Expirada, "expirou", null)]
    [InlineData(StatusSolicitacao.Recebida, StatusSolicitacao.EmManutencao, "entrou em manutenção", null)]
    [InlineData(StatusSolicitacao.EmManutencao, StatusSolicitacao.Concluida, "foi concluída. Aguarde a liberação", "A OSM da SSL {0} foi concluída. Defina o destino do equipamento.")]
    [InlineData(StatusSolicitacao.EmManutencao, StatusSolicitacao.NaoExecutada, "não pôde ser executada", "A OSM da SSL {0} não foi executada. Defina o destino do equipamento.")]
    [InlineData(StatusSolicitacao.Concluida, StatusSolicitacao.AguardandoRetirada, "está pronto para retirada", null)]
    [InlineData(StatusSolicitacao.Concluida, StatusSolicitacao.SemReparo, "não tem reparo", null)]
    [InlineData(StatusSolicitacao.SemReparo, StatusSolicitacao.Inutilizado, "foi inutilizado", null)]
    [InlineData(StatusSolicitacao.AguardandoRetirada, StatusSolicitacao.Retirado, "foi retirado. Ciclo encerrado.", null)]
    public void Transicao_notifica_o_solicitante_e_o_administrativo_quando_previsto(string origem, string destino, string trechoSolicitante, string? textoAdmin)
    {
        var s = Ssl(origem);
        var carregada = _db.Solicitacoes.Find(s.Id)!;

        _workflow.Transicionar(carregada, destino, HistoricoStatus.MatriculaSistema);

        var gravadas = Gravadas();
        var doSolicitante = Assert.Single(gravadas, n => n.MatriculaDestino == Solicitante);
        Assert.Contains($"SSL {s.Id}", doSolicitante.Mensagem);
        Assert.Contains(trechoSolicitante, doSolicitante.Mensagem);
        var doAdmin = gravadas.Where(n => n.PerfilDestino == Admin).ToList();
        if (textoAdmin is null)
            Assert.Empty(doAdmin);
        else
            Assert.Equal(string.Format(textoAdmin, s.Id), Assert.Single(doAdmin).Mensagem);
        Assert.Equal(textoAdmin is null ? 1 : 2, gravadas.Count);
    }

    [Theory]
    [InlineData(StatusSolicitacao.Devolvida, StatusSolicitacao.Reenviada)]
    [InlineData(StatusSolicitacao.Aprovada, StatusSolicitacao.Recebida)]
    public void Reenviar_nao_notifica_e_receber_notifica(string origem, string destino)
    {
        var s = Ssl(origem);
        var carregada = _db.Solicitacoes.Find(s.Id)!;
        carregada.NumeroSsm = "SSM-2027-000009";

        _workflow.Transicionar(carregada, destino, "admin123");

        if (destino == StatusSolicitacao.Reenviada)
            Assert.Empty(Gravadas());
        else
            Assert.Equal($"O equipamento da SSL {s.Id} foi recebido na oficina (SSM SSM-2027-000009).", Assert.Single(Gravadas()).Mensagem);
    }

    [Fact]
    public void Abertura_da_ssl_nao_notifica()
    {
        var nova = new Solicitacao { Descricao = "Nova", MatriculaSolicitante = Solicitante, EquipamentoId = 3 };

        _workflow.Transicionar(nova, StatusSolicitacao.Solicitada, Solicitante);

        Assert.Empty(Gravadas());
    }

    [Fact]
    public void Quem_fez_a_acao_nao_e_notificado()
    {
        var cancelada = Ssl(StatusSolicitacao.Solicitada);
        var autorizada = Ssl(StatusSolicitacao.AguardandoRetirada);
        var handler = new AutorizarRetiradaHandler(new SolicitacaoRepository(_db, _relogio), _workflow);

        _workflow.CancelarTriagem(cancelada.Id, Solicitante, "Comum");
        handler.Executar(autorizada.Id, new AutorizarRetiradaRequest { Nome = "Maria", Documento = "12345678900" }, Solicitante, "Comum");

        Assert.Empty(Gravadas());
    }

    [Fact]
    public void Textos_nao_expoem_documento_autorizado_nem_de_quem_retirou()
    {
        var s = Ssl(StatusSolicitacao.AguardandoRetirada);
        var handler = new AutorizarRetiradaHandler(new SolicitacaoRepository(_db, _relogio), _workflow);

        handler.Executar(s.Id, new AutorizarRetiradaRequest { Nome = "Maria Souza", Documento = "123.456.789-00" }, "admin123", "Administrativo");
        _db.ChangeTracker.Clear();
        _workflow.RegistrarRetirada(s.Id, "admin123", "12345678900");

        var mensagens = Gravadas().Select(n => n.Mensagem).ToList();
        Assert.Equal(new[]
        {
            $"A retirada do equipamento da SSL {s.Id} foi autorizada para Maria Souza.",
            $"O equipamento da SSL {s.Id} foi retirado. Ciclo encerrado."
        }, mensagens);
        Assert.DoesNotContain(mensagens, m => m.Contains("123.456.789-00") || m.Contains("12345678900"));
    }

    [Fact]
    public void Expiracao_pelo_sistema_notifica_o_solicitante()
    {
        var s = Ssl(StatusSolicitacao.Devolvida);
        _relogio.Avancar(TimeSpan.FromDays(30));
        var repositorio = new SolicitacaoRepository(_db, _relogio);
        var expiracao = new ExpiracaoService(repositorio, NovoWorkflow(_db, repositorio), _relogio, new PrazosOptions());

        expiracao.Executar();

        Assert.Contains(Gravadas(), n => n.SolicitacaoId == s.Id && n.MatriculaDestino == Solicitante
            && n.Mensagem == $"Sua SSL {s.Id} expirou por falta de ação no prazo.");
    }

    // ---------- Atomicidade ----------

    [Fact]
    public void Transicao_invalida_nao_grava_notificacao()
    {
        var s = Ssl(StatusSolicitacao.Aprovada);

        Assert.Throws<RegraDeNegocioException>(() => _workflow.AprovarTriagem(s.Id, "admin123"));

        Assert.Empty(Gravadas());
    }

    [Fact]
    public void Conflito_de_concorrencia_nao_grava_notificacao()
    {
        var s = Ssl(StatusSolicitacao.Solicitada);
        using var contextoB = NovoContexto();
        var workflowB = NovoWorkflow(contextoB);
        var lidaPorB = contextoB.Solicitacoes.Find(s.Id)!;

        _workflow.DevolverTriagem(s.Id, "Foto ilegível", "admin123");
        var devolucao = Assert.Single(Gravadas());

        Assert.Throws<ConflitoDeConcorrenciaException>(() => workflowB.Transicionar(lidaPorB, StatusSolicitacao.Aprovada, "admin123"));

        Assert.Equal(devolucao.Id, Assert.Single(Gravadas()).Id);
        Assert.Empty(contextoB.ChangeTracker.Entries<Notificacao>());
    }

    [Fact]
    public void Conclusao_desfeita_na_transacao_nao_grava_notificacao()
    {
        var s = Ssl(StatusSolicitacao.EmManutencao);
        var ordemId = _db.OrdensServico.AsNoTracking().Single(o => o.SolicitacaoId == s.Id).Id;
        using var contextoB = NovoContexto();
        var repositorioB = new RepositorioComAcaoAposLeitura(new SolicitacaoRepository(contextoB, _relogio),
            () => new PedidoPecaService(new PedidoPecaRepository(NovoContexto()), _relogio).Criar(ordemId,
                new CriarPedidoPecaRequest { Item = "Motor", Quantidade = 1, Justificativa = "Queimado" }, "tec123"));
        var workflowB = NovoWorkflow(contextoB, repositorioB);

        Assert.Throws<ConclusaoBloqueadaException>(() => workflowB.ConcluirManutencao(s.Id, "Laudo", "tec123"));

        Assert.Empty(Gravadas());
        Assert.Equal(StatusSolicitacao.EmManutencao, _db.Solicitacoes.AsNoTracking().Single(x => x.Id == s.Id).Status);
    }

    // ---------- Pedido de peça ----------

    [Fact]
    public void Pedido_criado_notifica_o_administrativo_e_recebido_notifica_o_tecnico_dono()
    {
        var s = Ssl(StatusSolicitacao.EmManutencao, "tec777");
        var ordemId = _db.OrdensServico.AsNoTracking().Single(o => o.SolicitacaoId == s.Id).Id;

        var pedido = _pedidos.Criar(ordemId, new CriarPedidoPecaRequest { Item = "Capacitor", Quantidade = 2, Justificativa = "Estufado" }, "tec777");
        _db.ChangeTracker.Clear();
        _pedidos.Encaminhar(pedido.Id, "RM-1", "admin123");
        _db.ChangeTracker.Clear();
        _relogio.Avancar(TimeSpan.FromDays(1));
        _pedidos.ConfirmarRecebimento(pedido.Id, "admin123");

        var gravadas = Gravadas();
        Assert.Equal(2, gravadas.Count);
        Assert.Equal(Admin, gravadas[0].PerfilDestino);
        Assert.Null(gravadas[0].MatriculaDestino);
        Assert.Equal($"Novo pedido de peça na OSM {ordemId} (SSL {s.Id}): Capacitor x2.", gravadas[0].Mensagem);
        Assert.Equal(T0, gravadas[0].Data);
        Assert.Equal("tec777", gravadas[1].MatriculaDestino);
        Assert.Equal($"A peça do pedido {pedido.Id} chegou: Capacitor x2 (OSM {ordemId}, SSL {s.Id}).", gravadas[1].Mensagem);
        Assert.Equal(T0.AddDays(1), gravadas[1].Data);
    }

    [Fact]
    public void Pedido_em_osm_encerrada_por_outra_operacao_nao_grava_notificacao()
    {
        var s = Ssl(StatusSolicitacao.EmManutencao);
        var ordemId = _db.OrdensServico.AsNoTracking().Single(o => o.SolicitacaoId == s.Id).Id;
        using var contextoB = NovoContexto();
        var repositorioB = new PedidoPecaRepository(contextoB);
        repositorioB.ObterOrdem(ordemId);
        _workflow.NaoExecutarManutencao(s.Id, "Sem conserto", "tec123");
        var antes = Gravadas().Count;
        var servicoB = new PedidoPecaService(repositorioB, _relogio, Gerador(contextoB));

        Assert.Throws<ConflitoDeConcorrenciaException>(() =>
            servicoB.Criar(ordemId, new CriarPedidoPecaRequest { Item = "Motor", Quantidade = 1, Justificativa = "x" }, "tec123"));

        Assert.Equal(antes, Gravadas().Count);
        Assert.DoesNotContain(Gravadas(), n => n.Mensagem.StartsWith("Novo pedido"));
    }

    // ---------- Listar ----------

    [Fact]
    public void Listar_traz_as_da_matricula_e_as_do_perfil_mais_novas_primeiro_com_contador()
    {
        var antiga = Gravar("admin123", null, T0.AddHours(-2));
        var doPerfil = Gravar(null, Admin, T0, lida: true);
        var nova = Gravar(null, Admin, T0.AddHours(1));
        Gravar(Solicitante, null, T0.AddHours(2));
        Gravar(null, "Tecnico", T0.AddHours(3));
        var handler = new ListarNotificacoesHandler(new NotificacaoRepository(_db));

        var todas = handler.Executar("admin123", Admin, apenasNaoLidas: false);
        var naoLidas = handler.Executar("admin123", Admin, apenasNaoLidas: true);

        Assert.Equal(new[] { nova.Id, doPerfil.Id, antiga.Id }, todas.Notificacoes.Select(n => n.Id));
        Assert.Equal(2, todas.NaoLidas);
        Assert.Equal(new[] { nova.Id, antiga.Id }, naoLidas.Notificacoes.Select(n => n.Id));
        Assert.Equal(2, naoLidas.NaoLidas);
        Assert.All(todas.Notificacoes, n => Assert.Equal(DateTimeKind.Utc, n.Data.Kind));
    }

    [Fact]
    public void Notificacao_de_matricula_nao_aparece_para_outro_usuario_do_mesmo_perfil()
    {
        Gravar("admin123", Admin, T0);
        var handler = new ListarNotificacoesHandler(new NotificacaoRepository(_db));

        var outro = handler.Executar("admin999", Admin, apenasNaoLidas: false);

        Assert.Empty(outro.Notificacoes);
        Assert.Equal(0, outro.NaoLidas);
    }

    // ---------- Marcar como lida ----------

    private MarcarNotificacaoComoLidaHandler Marcador() => new(new NotificacaoRepository(_db), _relogio);

    [Fact]
    public void Destinatario_marca_como_lida_e_marcar_de_novo_mantem_a_primeira_leitura()
    {
        var n = Gravar(Solicitante, null, T0);
        _relogio.Avancar(TimeSpan.FromMinutes(5));

        var primeira = Marcador().Executar(n.Id, Solicitante, "Comum");
        _db.ChangeTracker.Clear();
        _relogio.Avancar(TimeSpan.FromMinutes(5));
        var segunda = Marcador().Executar(n.Id, Solicitante, "Comum");

        Assert.True(primeira.Lida);
        Assert.Equal(T0.AddMinutes(5), primeira.DataLeitura);
        Assert.Equal(T0.AddMinutes(5), segunda.DataLeitura);
        var gravada = _db.Notificacoes.AsNoTracking().Single(x => x.Id == n.Id);
        Assert.True(gravada.Lida);
        Assert.Equal(DateTimeKind.Utc, gravada.DataLeitura!.Value.Kind);
    }

    [Fact]
    public void Quem_tem_o_perfil_de_destino_marca_a_do_perfil()
    {
        var n = Gravar(null, Admin, T0);

        Assert.True(Marcador().Executar(n.Id, "admin999", Admin).Lida);
    }

    [Theory]
    [InlineData("2002", "Comum")]
    [InlineData("tec123", "Tecnico")]
    [InlineData("admin123", "Administrativo")]
    public void Quem_nao_e_destinatario_recebe_403_e_nada_muda(string matricula, string perfil)
    {
        var n = Gravar(Solicitante, null, T0);
        var doPerfil = Gravar(null, "Tecnico", T0);

        Assert.Throws<UnauthorizedAccessException>(() => Marcador().Executar(n.Id, matricula, perfil));
        if (perfil != "Tecnico")
            Assert.Throws<UnauthorizedAccessException>(() => Marcador().Executar(doPerfil.Id, matricula, perfil));

        Assert.All(Gravadas(), x => Assert.False(x.Lida));
    }

    [Fact]
    public void Notificacao_inexistente_404()
    {
        var ex = Assert.Throws<NotificacaoNaoEncontradaException>(() => Marcador().Executar(9999, Solicitante, "Comum"));
        Assert.Equal("Notificação 9999 não encontrada.", ex.Message);
    }

    private sealed class RepositorioComAcaoAposLeitura : ISolicitacaoRepository
    {
        private readonly ISolicitacaoRepository _real;
        private Action? _aposLeitura;

        public RepositorioComAcaoAposLeitura(ISolicitacaoRepository real, Action aposLeitura)
        {
            _real = real;
            _aposLeitura = aposLeitura;
        }

        public Solicitacao? ObterComOrdemServico(int id)
        {
            var lida = _real.ObterComOrdemServico(id);
            var acao = _aposLeitura;
            _aposLeitura = null;
            acao?.Invoke();
            return lida;
        }

        public Solicitacao? ObterPorId(int id) => _real.ObterPorId(id);
        public List<SolicitacaoDto> Listar(string? status, string? matriculaSolicitante) => _real.Listar(status, matriculaSolicitante);
        public List<SolicitacaoDto> ListarRecebidas() => _real.ListarRecebidas();
        public SolicitacaoDetalheDto? ObterDetalhe(int id) => _real.ObterDetalhe(id);
        public List<CandidatoExpiracao> ObterCandidatosExpiracao() => _real.ObterCandidatosExpiracao();
        public string? UltimoNumeroSsm(string prefixo) => _real.UltimoNumeroSsm(prefixo);
        public void Salvar(Solicitacao solicitacao, HistoricoStatus historico) => _real.Salvar(solicitacao, historico);
        public void EmTransacao(Action acao) => _real.EmTransacao(acao);
        public List<PedidoPendenteDto> ListarPedidosPendentes(int ordemServicoId) => _real.ListarPedidosPendentes(ordemServicoId);
    }
}
