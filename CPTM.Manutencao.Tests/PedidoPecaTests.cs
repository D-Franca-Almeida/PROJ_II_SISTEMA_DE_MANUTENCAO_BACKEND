using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using CPTM.Manutencao.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CPTM.Manutencao.Tests;

public sealed class PedidoPecaTests : IDisposable
{
    private static readonly DateTime T0 = new(2027, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _conexao = new("DataSource=:memory:");
    private readonly ManutencaoDbContext _db;
    private readonly RelogioFalso _relogio = new(T0);
    private readonly PedidoPecaService _pedidos;
    private readonly WorkflowService _workflow;

    public PedidoPecaTests()
    {
        _conexao.Open();
        _db = NovoContexto();
        _db.Database.EnsureCreated();
        _pedidos = new PedidoPecaService(new PedidoPecaRepository(_db), _relogio);
        _workflow = new WorkflowService(new SolicitacaoRepository(_db, _relogio), _relogio);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conexao.Dispose();
    }

    private ManutencaoDbContext NovoContexto() =>
        new(new DbContextOptionsBuilder<ManutencaoDbContext>().UseSqlite(_conexao).Options);

    private (int SolicitacaoId, int OrdemId) Osm(string status = StatusSolicitacao.EmManutencao, string tecnico = "tec123")
    {
        var s = new Solicitacao
        {
            Descricao = "Teste", Status = status, MatriculaSolicitante = "1001", EquipamentoId = 3, MatriculaTecnico = tecnico,
            OrdemServico = new OrdemServico { MatriculaTecnico = tecnico, DataAbertura = T0.AddHours(-1) }
        };
        _db.Solicitacoes.Add(s);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return (s.Id, s.OrdemServico.Id);
    }

    private static CriarPedidoPecaRequest Req(string? item = "Capacitor 100uF", int? quantidade = 2, string? justificativa = "Capacitor estufado") =>
        new() { Item = item, Quantidade = quantidade, Justificativa = justificativa };

    private PedidoPecaDto Pedir(int ordemId, string item = "Capacitor 100uF")
    {
        var dto = _pedidos.Criar(ordemId, Req(item), "tec123");
        _db.ChangeTracker.Clear();
        return dto;
    }

    private PedidoPeca Pedido(int id) => _db.PedidosPeca.AsNoTracking().Single(p => p.Id == id);

    private Solicitacao Ssl(int id) => _db.Solicitacoes.AsNoTracking().Single(s => s.Id == id);

    // ---------- Criar (US-08) ----------

    [Fact]
    public void Criar_grava_solicitado_com_dados_do_tecnico_e_data_utc()
    {
        var (sslId, ordemId) = Osm();

        var dto = _pedidos.Criar(ordemId, Req(" Capacitor 100uF ", 2, " Capacitor estufado "), " tec123 ");

        Assert.Equal(StatusPedidoPeca.Solicitado, dto.Status);
        Assert.Equal(ordemId, dto.OrdemServicoId);
        Assert.Equal(sslId, dto.SolicitacaoId);
        Assert.Equal("Capacitor 100uF", dto.Item);
        Assert.Equal(2, dto.Quantidade);
        Assert.Equal("Capacitor estufado", dto.Justificativa);
        Assert.Equal("tec123", dto.MatriculaTecnico);
        Assert.Equal(T0, dto.DataPedido);
        Assert.Equal(DateTimeKind.Utc, dto.DataPedido.Kind);
        var salvo = Pedido(dto.Id);
        Assert.Equal("tec123", salvo.MatriculaSolicitante);
        Assert.Equal(StatusPedidoPeca.Solicitado, salvo.Status);
        Assert.Equal(StatusSolicitacao.EmManutencao, Ssl(sslId).Status);
        Assert.Empty(_db.HistoricoStatus.Where(h => h.SolicitacaoId == sslId));
    }

    [Theory]
    [InlineData(null, 1, "j", "O item (nome da peça) é obrigatório.")]
    [InlineData("  ", 1, "j", "O item (nome da peça) é obrigatório.")]
    [InlineData("Peça", null, "j", "A quantidade deve ser maior que zero.")]
    [InlineData("Peça", 0, "j", "A quantidade deve ser maior que zero.")]
    [InlineData("Peça", -3, "j", "A quantidade deve ser maior que zero.")]
    [InlineData("Peça", 1, null, "A justificativa é obrigatória.")]
    [InlineData("Peça", 1, " ", "A justificativa é obrigatória.")]
    public void Criar_com_campo_invalido_lanca_regra_sem_gravar(string? item, int? quantidade, string? justificativa, string erro)
    {
        var (_, ordemId) = Osm();

        var ex = Assert.Throws<RegraDeNegocioException>(() => _pedidos.Criar(ordemId, Req(item, quantidade, justificativa), "tec123"));

        Assert.Equal(erro, ex.Message);
        Assert.Empty(_db.PedidosPeca.AsNoTracking());
    }

    [Fact]
    public void Criar_com_textos_acima_do_limite_lanca_regra()
    {
        var (_, ordemId) = Osm();

        Assert.Throws<RegraDeNegocioException>(() => _pedidos.Criar(ordemId, Req(item: new string('x', 201)), "tec123"));
        Assert.Throws<RegraDeNegocioException>(() => _pedidos.Criar(ordemId, Req(justificativa: new string('x', 501)), "tec123"));
        Assert.Empty(_db.PedidosPeca.AsNoTracking());
    }

    [Theory]
    [InlineData(StatusSolicitacao.Concluida)]
    [InlineData(StatusSolicitacao.NaoExecutada)]
    [InlineData(StatusSolicitacao.Recebida)]
    public void Criar_com_osm_fora_de_manutencao_lanca_regra(string status)
    {
        var (_, ordemId) = Osm(status);

        var ex = Assert.Throws<RegraDeNegocioException>(() => _pedidos.Criar(ordemId, Req(), "tec123"));

        Assert.Equal($"Só é possível pedir peças para uma OSM em manutenção (status atual da solicitação: {status}).", ex.Message);
        Assert.Empty(_db.PedidosPeca.AsNoTracking());
    }

    [Fact]
    public void Criar_por_outro_tecnico_lanca_acesso_negado()
    {
        var (_, ordemId) = Osm(tecnico: "tec123");

        Assert.Throws<UnauthorizedAccessException>(() => _pedidos.Criar(ordemId, Req(), "tec456"));
        Assert.Empty(_db.PedidosPeca.AsNoTracking());
    }

    [Fact]
    public void Criar_em_osm_inexistente_lanca_nao_encontrada()
    {
        var ex = Assert.Throws<OrdemServicoNaoEncontradaException>(() => _pedidos.Criar(9999, Req(), "tec123"));
        Assert.Equal("Ordem de serviço 9999 não encontrada.", ex.Message);
    }

    [Fact]
    public void Criar_depois_de_a_osm_ser_encerrada_por_outra_operacao_lanca_conflito_sem_gravar()
    {
        var (sslId, ordemId) = Osm();
        using var contextoB = NovoContexto();
        var repositorioB = new PedidoPecaRepository(contextoB);
        var ordemLida = repositorioB.ObterOrdem(ordemId)!;
        Assert.Equal(StatusSolicitacao.EmManutencao, ordemLida.Solicitacao!.Status);

        _workflow.ConcluirManutencao(sslId, "Laudo", "tec123");
        var servicoB = new PedidoPecaService(repositorioB, _relogio);

        Assert.Throws<ConflitoDeConcorrenciaException>(() => servicoB.Criar(ordemId, Req(), "tec123"));
        Assert.Empty(_db.PedidosPeca.AsNoTracking());
        Assert.Equal(StatusSolicitacao.Concluida, Ssl(sslId).Status);
    }

    // ---------- Ciclo (US-23) ----------

    [Fact]
    public void Ciclo_completo_solicitado_encaminhado_recebido_grava_quem_e_quando()
    {
        var (_, ordemId) = Osm();
        var id = Pedir(ordemId).Id;

        _relogio.Avancar(TimeSpan.FromHours(1));
        var encaminhado = _pedidos.Encaminhar(id, " RM-2026-0042 / Fornecedor X ", " admin123 ");
        _db.ChangeTracker.Clear();
        _relogio.Avancar(TimeSpan.FromDays(2));
        var recebido = _pedidos.ConfirmarRecebimento(id, "admin123");

        Assert.Equal(StatusPedidoPeca.Encaminhado, encaminhado.Status);
        Assert.Equal("RM-2026-0042 / Fornecedor X", encaminhado.ObservacaoAlmoxarifado);
        Assert.Equal(StatusPedidoPeca.Recebido, recebido.Status);
        var p = Pedido(id);
        Assert.Equal(StatusPedidoPeca.Recebido, p.Status);
        Assert.Equal("admin123", p.MatriculaEncaminhamento);
        Assert.Equal(T0.AddHours(1), p.DataEncaminhamento);
        Assert.Equal("admin123", p.MatriculaRecebimento);
        Assert.Equal(T0.AddHours(1).AddDays(2), p.DataRecebimento);
        Assert.Equal(DateTimeKind.Utc, p.DataRecebimento!.Value.Kind);
        Assert.Null(p.DataCancelamento);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Encaminhar_sem_observacao_lanca_regra(string? observacao)
    {
        var (_, ordemId) = Osm();
        var id = Pedir(ordemId).Id;

        var ex = Assert.Throws<RegraDeNegocioException>(() => _pedidos.Encaminhar(id, observacao, "admin123"));

        Assert.Equal("A observação do almoxarifado (nº de requisição/fornecedor) é obrigatória.", ex.Message);
        Assert.Equal(StatusPedidoPeca.Solicitado, Pedido(id).Status);
    }

    [Fact]
    public void Transicoes_fora_de_ordem_lancam_regra_com_status_atual()
    {
        var (_, ordemId) = Osm();
        var id = Pedir(ordemId).Id;

        var receberSolicitado = Assert.Throws<RegraDeNegocioException>(() => _pedidos.ConfirmarRecebimento(id, "admin123"));
        Assert.Equal("Transição inválida: só é possível confirmar a chegada de um pedido ENCAMINHADO (status atual: SOLICITADO).", receberSolicitado.Message);

        _pedidos.Encaminhar(id, "RM-1", "admin123");
        _db.ChangeTracker.Clear();
        var encaminharDeNovo = Assert.Throws<RegraDeNegocioException>(() => _pedidos.Encaminhar(id, "RM-2", "admin123"));
        Assert.Equal("Transição inválida: só é possível encaminhar ao almoxarifado um pedido SOLICITADO (status atual: ENCAMINHADO).", encaminharDeNovo.Message);

        _pedidos.ConfirmarRecebimento(id, "admin123");
        _db.ChangeTracker.Clear();
        Assert.Throws<RegraDeNegocioException>(() => _pedidos.ConfirmarRecebimento(id, "admin123"));
        var cancelarRecebido = Assert.Throws<RegraDeNegocioException>(() => _pedidos.Cancelar(id, null, "tec123"));
        Assert.Equal("Transição inválida: só é possível cancelar um pedido SOLICITADO ou ENCAMINHADO (status atual: RECEBIDO).", cancelarRecebido.Message);
        Assert.Equal("RM-1", Pedido(id).ObservacaoAlmoxarifado);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tecnico_dono_cancela_pedido_solicitado_ou_encaminhado(bool encaminharAntes)
    {
        var (_, ordemId) = Osm();
        var id = Pedir(ordemId).Id;
        if (encaminharAntes)
        {
            _pedidos.Encaminhar(id, "RM-1", "admin123");
            _db.ChangeTracker.Clear();
        }

        var dto = _pedidos.Cancelar(id, " Peça reaproveitada do estoque ", "tec123");

        Assert.Equal(StatusPedidoPeca.Cancelado, dto.Status);
        var p = Pedido(id);
        Assert.Equal("tec123", p.MatriculaCancelamento);
        Assert.Equal(T0, p.DataCancelamento);
        Assert.Equal("Peça reaproveitada do estoque", p.MotivoCancelamento);
        _db.ChangeTracker.Clear();
        Assert.Throws<RegraDeNegocioException>(() => _pedidos.Cancelar(id, null, "tec123"));
    }

    [Fact]
    public void Cancelar_sem_motivo_e_permitido_e_outro_tecnico_nao_cancela()
    {
        var (_, ordemId) = Osm();
        var id = Pedir(ordemId).Id;

        Assert.Throws<UnauthorizedAccessException>(() => _pedidos.Cancelar(id, null, "tec456"));
        Assert.Equal(StatusPedidoPeca.Solicitado, Pedido(id).Status);

        _db.ChangeTracker.Clear();
        _pedidos.Cancelar(id, "  ", "tec123");
        Assert.Null(Pedido(id).MotivoCancelamento);
    }

    [Fact]
    public void Pedido_inexistente_lanca_nao_encontrado()
    {
        Assert.Throws<PedidoPecaNaoEncontradoException>(() => _pedidos.Encaminhar(9999, "x", "admin123"));
        Assert.Throws<PedidoPecaNaoEncontradoException>(() => _pedidos.ConfirmarRecebimento(9999, "admin123"));
        var ex = Assert.Throws<PedidoPecaNaoEncontradoException>(() => _pedidos.Cancelar(9999, null, "tec123"));
        Assert.Equal("Pedido de peça 9999 não encontrado.", ex.Message);
    }

    [Fact]
    public void Admin_recebe_e_tecnico_cancela_ao_mesmo_tempo_so_um_vence()
    {
        var (_, ordemId) = Osm();
        var id = Pedir(ordemId).Id;
        _pedidos.Encaminhar(id, "RM-1", "admin123");
        _db.ChangeTracker.Clear();
        using var contextoB = NovoContexto();
        var repositorioB = new PedidoPecaRepository(contextoB);
        Assert.Equal(StatusPedidoPeca.Encaminhado, repositorioB.ObterPorId(id)!.Status);

        _pedidos.ConfirmarRecebimento(id, "admin123");
        var servicoB = new PedidoPecaService(repositorioB, _relogio);
        var ex = Assert.Throws<ConflitoDeConcorrenciaException>(() => servicoB.Cancelar(id, null, "tec123"));

        Assert.Equal($"O pedido de peça {id} foi alterado por outra operação ao mesmo tempo. Atualize a lista e tente novamente.", ex.Message);
        Assert.Equal(StatusPedidoPeca.Recebido, Pedido(id).Status);
    }

    // ---------- Listagens ----------

    [Fact]
    public void Listar_da_ordem_traz_so_os_pedidos_dela_em_ordem_de_pedido()
    {
        var (_, ordemA) = Osm();
        var (_, ordemB) = Osm();
        var primeiro = Pedir(ordemA, "A1").Id;
        _relogio.Avancar(TimeSpan.FromMinutes(5));
        var segundo = Pedir(ordemA, "A2").Id;
        Pedir(ordemB, "B1");

        var lista = _pedidos.ListarDaOrdem(ordemA, "tec123", "Tecnico");

        Assert.Equal(new[] { primeiro, segundo }, lista.Select(p => p.Id));
        Assert.Equal(lista, _pedidos.ListarDaOrdem(ordemA, "admin123", "Administrativo"));
        Assert.Throws<UnauthorizedAccessException>(() => _pedidos.ListarDaOrdem(ordemA, "tec456", "Tecnico"));
        Assert.Throws<OrdemServicoNaoEncontradaException>(() => _pedidos.ListarDaOrdem(9999, "admin123", "Administrativo"));
        Assert.Empty(_pedidos.ListarDaOrdem(Osm().OrdemId, "tec123", "Tecnico"));
    }

    [Fact]
    public void Listar_fila_filtra_por_status_e_pendentes_mais_antigos_primeiro()
    {
        var (_, ordemId) = Osm();
        var solicitado = Pedir(ordemId, "S").Id;
        _relogio.Avancar(TimeSpan.FromMinutes(1));
        var encaminhado = Pedir(ordemId, "E").Id;
        _relogio.Avancar(TimeSpan.FromMinutes(1));
        var recebido = Pedir(ordemId, "R").Id;
        _relogio.Avancar(TimeSpan.FromMinutes(1));
        var cancelado = Pedir(ordemId, "C").Id;
        _pedidos.Encaminhar(encaminhado, "RM", "admin123");
        _pedidos.Encaminhar(recebido, "RM", "admin123");
        _pedidos.ConfirmarRecebimento(recebido, "admin123");
        _pedidos.Cancelar(cancelado, null, "tec123");
        _db.ChangeTracker.Clear();

        Assert.Equal(new[] { solicitado, encaminhado, recebido, cancelado }, _pedidos.Listar(null).Select(p => p.Id));
        Assert.Equal(new[] { solicitado, encaminhado }, _pedidos.Listar("pendentes").Select(p => p.Id));
        Assert.Equal(new[] { encaminhado }, _pedidos.Listar("ENCAMINHADO").Select(p => p.Id));
        Assert.Equal(new[] { recebido }, _pedidos.Listar(" recebido ").Select(p => p.Id));
        Assert.Equal(new[] { cancelado }, _pedidos.Listar("CANCELADO").Select(p => p.Id));
        var ex = Assert.Throws<RegraDeNegocioException>(() => _pedidos.Listar("ENTREGUE"));
        Assert.Equal("Status de pedido inválido: ENTREGUE. Use SOLICITADO, ENCAMINHADO, RECEBIDO, CANCELADO ou PENDENTES.", ex.Message);
    }

    // ---------- Bloqueio da conclusão e não execução (US-23) ----------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Concluir_com_pedido_pendente_e_bloqueado_listando_os_pendentes(bool encaminhado)
    {
        var (sslId, ordemId) = Osm();
        var pendente = Pedir(ordemId, "Motor").Id;
        if (encaminhado)
        {
            _pedidos.Encaminhar(pendente, "RM", "admin123");
            _db.ChangeTracker.Clear();
        }
        var resolvido = Pedir(ordemId, "Correia").Id;
        _pedidos.Cancelar(resolvido, null, "tec123");
        _db.ChangeTracker.Clear();

        var ex = Assert.Throws<ConclusaoBloqueadaException>(() => _workflow.ConcluirManutencao(sslId, "Laudo", "tec123"));

        var status = encaminhado ? "ENCAMINHADO" : "SOLICITADO";
        var item = Assert.Single(ex.Pendentes);
        Assert.Equal(new PedidoPendenteDto(pendente, "Motor", 2, status), item);
        Assert.Equal($"Não é possível concluir a OSM {ordemId}: há 1 pedido(s) de peça pendente(s) (#{pendente} Motor x2 - {status}). Aguarde a chegada ou cancele os pedidos.", ex.Message);
        Assert.IsAssignableFrom<RegraDeNegocioException>(ex);
        _db.ChangeTracker.Clear();
        Assert.Equal(StatusSolicitacao.EmManutencao, Ssl(sslId).Status);
        Assert.Null(_db.OrdensServico.AsNoTracking().Single(o => o.Id == ordemId).Laudo);
        Assert.Empty(_db.HistoricoStatus.Where(h => h.SolicitacaoId == sslId));
    }

    [Fact]
    public void Concluir_depois_que_a_peca_chegou_e_permitido()
    {
        var (sslId, ordemId) = Osm();
        var id = Pedir(ordemId).Id;
        _pedidos.Encaminhar(id, "RM", "admin123");
        _pedidos.ConfirmarRecebimento(id, "admin123");
        _db.ChangeTracker.Clear();

        _workflow.ConcluirManutencao(sslId, "Laudo", "tec123");

        Assert.Equal(StatusSolicitacao.Concluida, Ssl(sslId).Status);
        Assert.Equal(StatusPedidoPeca.Recebido, Pedido(id).Status);
    }

    [Fact]
    public void Nao_executar_cancela_os_pendentes_na_mesma_gravacao_e_preserva_os_resolvidos()
    {
        var (sslId, ordemId) = Osm();
        var solicitado = Pedir(ordemId, "S").Id;
        var encaminhado = Pedir(ordemId, "E").Id;
        var recebido = Pedir(ordemId, "R").Id;
        var canceladoAntes = Pedir(ordemId, "C").Id;
        _pedidos.Encaminhar(encaminhado, "RM", "admin123");
        _pedidos.Encaminhar(recebido, "RM", "admin123");
        _pedidos.ConfirmarRecebimento(recebido, "admin123");
        _pedidos.Cancelar(canceladoAntes, "Não precisa", "tec123");
        _db.ChangeTracker.Clear();
        _relogio.Avancar(TimeSpan.FromHours(3));

        var (ordem, cancelados) = _workflow.NaoExecutarManutencao(sslId, "Sem conserto", "tec123");

        Assert.Equal(new[] { solicitado, encaminhado }, cancelados);
        Assert.Equal(StatusSolicitacao.NaoExecutada, Ssl(sslId).Status);
        foreach (var id in new[] { solicitado, encaminhado })
        {
            var p = Pedido(id);
            Assert.Equal(StatusPedidoPeca.Cancelado, p.Status);
            Assert.Equal(WorkflowService.MotivoCancelamentoAutomatico, p.MotivoCancelamento);
            Assert.Equal("tec123", p.MatriculaCancelamento);
            Assert.Equal(ordem.DataConclusao, p.DataCancelamento);
        }
        Assert.Equal(StatusPedidoPeca.Recebido, Pedido(recebido).Status);
        Assert.Equal("Não precisa", Pedido(canceladoAntes).MotivoCancelamento);
        Assert.Single(_db.HistoricoStatus.Where(h => h.SolicitacaoId == sslId));
    }

    [Fact]
    public void Nao_executar_com_pedido_alterado_por_outra_operacao_lanca_conflito_sem_gravar_nada()
    {
        var (sslId, ordemId) = Osm();
        var id = Pedir(ordemId).Id;
        using var contextoB = NovoContexto();
        var workflowB = new WorkflowService(new SolicitacaoRepository(contextoB, _relogio), _relogio);
        var lida = contextoB.Solicitacoes.Include(s => s.OrdemServico).ThenInclude(o => o!.Pedidos).Single(s => s.Id == sslId);
        Assert.Equal(StatusPedidoPeca.Solicitado, Assert.Single(lida.OrdemServico!.Pedidos).Status);

        _pedidos.Encaminhar(id, "RM", "admin123");
        _db.ChangeTracker.Clear();

        Assert.Throws<ConflitoDeConcorrenciaException>(() => workflowB.NaoExecutarManutencao(sslId, "Sem conserto", "tec123"));

        Assert.Equal(StatusSolicitacao.EmManutencao, Ssl(sslId).Status);
        Assert.Equal(StatusPedidoPeca.Encaminhado, Pedido(id).Status);
        Assert.Null(_db.OrdensServico.AsNoTracking().Single(o => o.Id == ordemId).MotivoNaoExecucao);
        Assert.Empty(_db.HistoricoStatus.Where(h => h.SolicitacaoId == sslId));
    }

    [Fact]
    public void Concluir_com_pedido_criado_entre_a_leitura_e_a_gravacao_e_bloqueado_sem_gravar()
    {
        var (sslId, ordemId) = Osm();
        int criado = 0;
        using var contextoB = NovoContexto();
        var workflowB = new WorkflowService(
            new RepositorioComPedidoAposLeitura(new SolicitacaoRepository(contextoB, _relogio), () => criado = Pedir(ordemId, "Motor").Id),
            _relogio);

        var ex = Assert.Throws<ConclusaoBloqueadaException>(() => workflowB.ConcluirManutencao(sslId, "Laudo", "tec123"));

        Assert.Equal(new PedidoPendenteDto(criado, "Motor", 2, StatusPedidoPeca.Solicitado), Assert.Single(ex.Pendentes));
        Assert.Equal(StatusSolicitacao.EmManutencao, Ssl(sslId).Status);
        var ordem = _db.OrdensServico.AsNoTracking().Single(o => o.Id == ordemId);
        Assert.Null(ordem.Laudo);
        Assert.Null(ordem.DataConclusao);
        Assert.Empty(_db.HistoricoStatus.Where(h => h.SolicitacaoId == sslId));
    }

    [Fact]
    public void Nao_executar_com_pedido_criado_entre_a_leitura_e_a_gravacao_lanca_conflito_sem_gravar_nada()
    {
        var (sslId, ordemId) = Osm();
        var anterior = Pedir(ordemId, "Correia").Id;
        int criado = 0;
        using var contextoB = NovoContexto();
        var workflowB = new WorkflowService(
            new RepositorioComPedidoAposLeitura(new SolicitacaoRepository(contextoB, _relogio), () => criado = Pedir(ordemId, "Motor").Id),
            _relogio);

        Assert.Throws<ConflitoDeConcorrenciaException>(() => workflowB.NaoExecutarManutencao(sslId, "Sem conserto", "tec123"));

        Assert.Equal(StatusSolicitacao.EmManutencao, Ssl(sslId).Status);
        Assert.Equal(StatusPedidoPeca.Solicitado, Pedido(anterior).Status);
        Assert.Equal(StatusPedidoPeca.Solicitado, Pedido(criado).Status);
        Assert.Null(_db.OrdensServico.AsNoTracking().Single(o => o.Id == ordemId).MotivoNaoExecucao);
        Assert.Empty(_db.HistoricoStatus.Where(h => h.SolicitacaoId == sslId));
    }

    private sealed class RepositorioComPedidoAposLeitura : ISolicitacaoRepository
    {
        private readonly ISolicitacaoRepository _real;
        private Action? _aposLeitura;

        public RepositorioComPedidoAposLeitura(ISolicitacaoRepository real, Action aposLeitura)
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
