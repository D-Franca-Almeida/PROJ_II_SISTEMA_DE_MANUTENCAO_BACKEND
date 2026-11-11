using System.Net;
using System.Text.Json;
using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CPTM.Manutencao.Tests.Integracao;

public sealed class PedidoPecaHttpTests : IClassFixture<CptmApiFactory>
{
    private const string CorpoPedido = "{ \"item\": \"Capacitor 100uF\", \"quantidade\": 2, \"justificativa\": \"Capacitor estufado na placa\" }";

    private readonly CptmApiFactory _api;

    public PedidoPecaHttpTests(CptmApiFactory api)
    {
        _api = api;
    }

    private (int SolicitacaoId, int OrdemId) Osm(string status = StatusSolicitacao.EmManutencao, string tecnico = "tec123")
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        var s = new Solicitacao
        {
            Descricao = "SSL de teste", Status = status, MatriculaSolicitante = "comum123", EquipamentoId = 1,
            DataAbertura = DateTime.UtcNow.AddDays(-2), MatriculaTecnico = tecnico,
            OrdemServico = new OrdemServico { MatriculaTecnico = tecnico, DataAbertura = DateTime.UtcNow.AddHours(-3) }
        };
        db.Solicitacoes.Add(s);
        db.SaveChanges();
        return (s.Id, s.OrdemServico.Id);
    }

    private int InserirPedido(int ordemId, string status, string item = "Peça")
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        var p = new PedidoPeca
        {
            OrdemServicoId = ordemId, Item = item, Quantidade = 1, Justificativa = "j", Status = status,
            MatriculaSolicitante = "tec123", DataPedido = DateTime.UtcNow
        };
        db.PedidosPeca.Add(p);
        db.SaveChanges();
        return p.Id;
    }

    private void GarantirSegundoTecnico()
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        if (db.Usuarios.Any(u => u.Matricula == "tec456")) return;
        var hash = db.Usuarios.AsNoTracking().Single(u => u.Matricula == "tec123").SenhaHash;
        db.Usuarios.Add(new Usuario { Matricula = "tec456", Nome = "Técnico 2", SenhaHash = hash, Perfil = "Tecnico" });
        db.SaveChanges();
    }

    private PedidoPeca Pedido(int id)
    {
        using var escopo = _api.Services.CreateScope();
        return escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>().PedidosPeca.AsNoTracking().Single(p => p.Id == id);
    }

    private List<PedidoPeca> PedidosDa(int ordemId)
    {
        using var escopo = _api.Services.CreateScope();
        return escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>().PedidosPeca.AsNoTracking()
            .Where(p => p.OrdemServicoId == ordemId).OrderBy(p => p.Id).ToList();
    }

    private (Solicitacao Solicitacao, OrdemServico Ordem, List<HistoricoStatus> Historico) Estado(int sslId)
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        return (db.Solicitacoes.AsNoTracking().Single(s => s.Id == sslId),
            db.OrdensServico.AsNoTracking().Single(o => o.SolicitacaoId == sslId),
            db.HistoricoStatus.AsNoTracking().Where(h => h.SolicitacaoId == sslId).ToList());
    }

    private static async Task<JsonElement> LerJson(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<string?> Erro(HttpResponseMessage resposta) => (await LerJson(resposta)).GetProperty("erro").GetString();

    // ---------- POST /api/ordens-servico/{id}/pecas ----------

    [Fact]
    public async Task Tecnico_dono_pede_peca_201_solicitado()
    {
        var (sslId, ordemId) = Osm();
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        using var resposta = await tecnico.PostAsync($"/api/ordens-servico/{ordemId}/pecas", CptmApiFactory.Json(CorpoPedido));

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var corpo = await LerJson(resposta);
        var id = corpo.GetProperty("id").GetInt32();
        Assert.Equal($"/api/pedidos-peca/{id}", resposta.Headers.Location!.OriginalString);
        Assert.Equal(ordemId, corpo.GetProperty("ordemServicoId").GetInt32());
        Assert.Equal(sslId, corpo.GetProperty("solicitacaoId").GetInt32());
        Assert.Equal("EM MANUTENCAO", corpo.GetProperty("statusSolicitacao").GetString());
        Assert.Equal("tec123", corpo.GetProperty("matriculaTecnico").GetString());
        Assert.Equal("Capacitor 100uF", corpo.GetProperty("item").GetString());
        Assert.Equal(2, corpo.GetProperty("quantidade").GetInt32());
        Assert.Equal("Capacitor estufado na placa", corpo.GetProperty("justificativa").GetString());
        Assert.Equal("SOLICITADO", corpo.GetProperty("status").GetString());
        Assert.EndsWith("Z", corpo.GetProperty("dataPedido").GetString());
        Assert.Equal(JsonValueKind.Null, corpo.GetProperty("dataEncaminhamento").ValueKind);
        Assert.Equal("SOLICITADO", Pedido(id).Status);
        Assert.Equal("EM MANUTENCAO", Estado(sslId).Solicitacao.Status);
    }

    [Theory]
    [InlineData("{ \"item\": \" \", \"quantidade\": 1, \"justificativa\": \"j\" }", "O item (nome da peça) é obrigatório.")]
    [InlineData("{ \"item\": \"Peça\", \"quantidade\": 0, \"justificativa\": \"j\" }", "A quantidade deve ser maior que zero.")]
    [InlineData("{ \"item\": \"Peça\", \"quantidade\": -1, \"justificativa\": \"j\" }", "A quantidade deve ser maior que zero.")]
    [InlineData("{ \"item\": \"Peça\", \"justificativa\": \"j\" }", "A quantidade deve ser maior que zero.")]
    [InlineData("{ \"item\": \"Peça\", \"quantidade\": 1 }", "A justificativa é obrigatória.")]
    [InlineData(null, "O item (nome da peça) é obrigatório.")]
    public async Task Pedido_invalido_400(string? corpo, string erro)
    {
        var (_, ordemId) = Osm();
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        using var resposta = await tecnico.PostAsync($"/api/ordens-servico/{ordemId}/pecas", corpo is null ? null : CptmApiFactory.Json(corpo));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal(erro, await Erro(resposta));
        Assert.Empty(PedidosDa(ordemId));
    }

    [Theory]
    [InlineData("CONCLUIDA")]
    [InlineData("NAO EXECUTADA")]
    public async Task Pedido_em_osm_encerrada_400(string status)
    {
        var (_, ordemId) = Osm(status);
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        using var resposta = await tecnico.PostAsync($"/api/ordens-servico/{ordemId}/pecas", CptmApiFactory.Json(CorpoPedido));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal($"Só é possível pedir peças para uma OSM em manutenção (status atual da solicitação: {status}).", await Erro(resposta));
        Assert.Empty(PedidosDa(ordemId));
    }

    [Fact]
    public async Task Pedido_por_outro_tecnico_403()
    {
        GarantirSegundoTecnico();
        var (_, ordemId) = Osm(tecnico: "tec123");
        using var outro = await _api.CriarClienteLogadoAsync("tec456");

        using var resposta = await outro.PostAsync($"/api/ordens-servico/{ordemId}/pecas", CptmApiFactory.Json(CorpoPedido));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Empty(PedidosDa(ordemId));
    }

    [Theory]
    [InlineData("admin123")]
    [InlineData("comum123")]
    public async Task Pedido_por_perfil_nao_tecnico_403(string matricula)
    {
        var (_, ordemId) = Osm();
        using var cliente = await _api.CriarClienteLogadoAsync(matricula);

        using var resposta = await cliente.PostAsync($"/api/ordens-servico/{ordemId}/pecas", CptmApiFactory.Json(CorpoPedido));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Empty(PedidosDa(ordemId));
    }

    [Fact]
    public async Task Pedido_sem_token_401_e_osm_inexistente_404()
    {
        using var anonimo = _api.CriarCliente();
        using var semToken = await anonimo.PostAsync("/api/ordens-servico/1/pecas", CptmApiFactory.Json(CorpoPedido));
        Assert.Equal(HttpStatusCode.Unauthorized, semToken.StatusCode);

        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");
        using var inexistente = await tecnico.PostAsync("/api/ordens-servico/9999/pecas", CptmApiFactory.Json(CorpoPedido));
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal("Ordem de serviço 9999 não encontrada.", await Erro(inexistente));
    }

    // ---------- GET /api/ordens-servico/{id}/pecas ----------

    [Fact]
    public async Task Pedidos_da_osm_200_para_dono_e_admin()
    {
        var (_, ordemId) = Osm();
        var a = InserirPedido(ordemId, StatusPedidoPeca.Solicitado, "A");
        var b = InserirPedido(ordemId, StatusPedidoPeca.Recebido, "B");
        InserirPedido(Osm().OrdemId, StatusPedidoPeca.Solicitado, "outra OSM");
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        foreach (var cliente in new[] { tecnico, admin })
        {
            using var resposta = await cliente.GetAsync($"/api/ordens-servico/{ordemId}/pecas");
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            var lista = (await LerJson(resposta)).EnumerateArray().ToList();
            Assert.Equal(new[] { a, b }, lista.Select(p => p.GetProperty("id").GetInt32()));
            Assert.Equal(new[] { "SOLICITADO", "RECEBIDO" }, lista.Select(p => p.GetProperty("status").GetString()));
        }
    }

    [Fact]
    public async Task Pedidos_da_osm_sem_pedidos_lista_vazia()
    {
        var (_, ordemId) = Osm();
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        using var resposta = await tecnico.GetAsync($"/api/ordens-servico/{ordemId}/pecas");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(0, (await LerJson(resposta)).GetArrayLength());
    }

    [Fact]
    public async Task Pedidos_da_osm_403_outro_tecnico_e_comum_401_404()
    {
        GarantirSegundoTecnico();
        var (_, ordemId) = Osm();
        using var outro = await _api.CriarClienteLogadoAsync("tec456");
        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        using var anonimo = _api.CriarCliente();
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var r1 = await outro.GetAsync($"/api/ordens-servico/{ordemId}/pecas");
        using var r2 = await comum.GetAsync($"/api/ordens-servico/{ordemId}/pecas");
        using var r3 = await anonimo.GetAsync($"/api/ordens-servico/{ordemId}/pecas");
        using var r4 = await admin.GetAsync("/api/ordens-servico/9999/pecas");

        Assert.Equal(HttpStatusCode.Forbidden, r1.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, r2.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, r3.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, r4.StatusCode);
    }

    // ---------- GET /api/pedidos-peca (aba de logística) ----------

    [Fact]
    public async Task Fila_de_logistica_filtra_por_status()
    {
        var (_, ordemId) = Osm();
        var solicitado = InserirPedido(ordemId, StatusPedidoPeca.Solicitado);
        var encaminhado = InserirPedido(ordemId, StatusPedidoPeca.Encaminhado);
        var recebido = InserirPedido(ordemId, StatusPedidoPeca.Recebido);
        var cancelado = InserirPedido(ordemId, StatusPedidoPeca.Cancelado);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        async Task<List<int>> Ids(string url)
        {
            using var r = await admin.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            return (await LerJson(r)).EnumerateArray().Select(p => p.GetProperty("id").GetInt32()).ToList();
        }

        var todos = await Ids("/api/pedidos-peca");
        Assert.Subset(todos.ToHashSet(), new HashSet<int> { solicitado, encaminhado, recebido, cancelado });
        var pendentes = await Ids("/api/pedidos-peca?status=PENDENTES");
        Assert.Contains(solicitado, pendentes);
        Assert.Contains(encaminhado, pendentes);
        Assert.DoesNotContain(recebido, pendentes);
        Assert.DoesNotContain(cancelado, pendentes);
        Assert.Contains(recebido, await Ids("/api/pedidos-peca?status=recebido"));
        Assert.DoesNotContain(solicitado, await Ids("/api/pedidos-peca?status=CANCELADO"));
    }

    [Fact]
    public async Task Fila_de_logistica_status_invalido_400_e_perfis_403_401()
    {
        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");
        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        using var anonimo = _api.CriarCliente();

        using var invalido = await admin.GetAsync("/api/pedidos-peca?status=ENTREGUE");
        using var r1 = await tecnico.GetAsync("/api/pedidos-peca");
        using var r2 = await comum.GetAsync("/api/pedidos-peca");
        using var r3 = await anonimo.GetAsync("/api/pedidos-peca");

        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        Assert.StartsWith("Status de pedido inválido: ENTREGUE.", await Erro(invalido));
        Assert.Equal(HttpStatusCode.Forbidden, r1.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, r2.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, r3.StatusCode);
    }

    // ---------- PUT encaminhar / receber / cancelar ----------

    [Fact]
    public async Task Admin_encaminha_e_confirma_chegada_200()
    {
        var (_, ordemId) = Osm();
        var id = InserirPedido(ordemId, StatusPedidoPeca.Solicitado);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var encaminhar = await admin.PutAsync($"/api/pedidos-peca/{id}/encaminhar",
            CptmApiFactory.Json("{ \"observacaoAlmoxarifado\": \"Requisição RM-2026-0042 / Fornecedor Eletrocomp\" }"));
        Assert.Equal(HttpStatusCode.OK, encaminhar.StatusCode);
        var e = await LerJson(encaminhar);
        Assert.Equal("ENCAMINHADO", e.GetProperty("status").GetString());
        Assert.Equal("Requisição RM-2026-0042 / Fornecedor Eletrocomp", e.GetProperty("observacaoAlmoxarifado").GetString());
        Assert.Equal("admin123", e.GetProperty("matriculaEncaminhamento").GetString());
        Assert.EndsWith("Z", e.GetProperty("dataEncaminhamento").GetString());

        using var receber = await admin.PutAsync($"/api/pedidos-peca/{id}/receber", null);
        Assert.Equal(HttpStatusCode.OK, receber.StatusCode);
        var r = await LerJson(receber);
        Assert.Equal("RECEBIDO", r.GetProperty("status").GetString());
        Assert.Equal("admin123", r.GetProperty("matriculaRecebimento").GetString());
        Assert.EndsWith("Z", r.GetProperty("dataRecebimento").GetString());
        Assert.Equal(StatusPedidoPeca.Recebido, Pedido(id).Status);
    }

    [Fact]
    public async Task Encaminhar_sem_observacao_ou_fora_de_solicitado_400()
    {
        var (_, ordemId) = Osm();
        var solicitado = InserirPedido(ordemId, StatusPedidoPeca.Solicitado);
        var encaminhado = InserirPedido(ordemId, StatusPedidoPeca.Encaminhado);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var semObs = await admin.PutAsync($"/api/pedidos-peca/{solicitado}/encaminhar", null);
        using var deNovo = await admin.PutAsync($"/api/pedidos-peca/{encaminhado}/encaminhar", CptmApiFactory.Json("{ \"observacaoAlmoxarifado\": \"x\" }"));

        Assert.Equal(HttpStatusCode.BadRequest, semObs.StatusCode);
        Assert.Equal("A observação do almoxarifado (nº de requisição/fornecedor) é obrigatória.", await Erro(semObs));
        Assert.Equal(HttpStatusCode.BadRequest, deNovo.StatusCode);
        Assert.Equal("Transição inválida: só é possível encaminhar ao almoxarifado um pedido SOLICITADO (status atual: ENCAMINHADO).", await Erro(deNovo));
        Assert.Equal(StatusPedidoPeca.Solicitado, Pedido(solicitado).Status);
    }

    [Theory]
    [InlineData("SOLICITADO")]
    [InlineData("RECEBIDO")]
    [InlineData("CANCELADO")]
    public async Task Receber_fora_de_encaminhado_400(string status)
    {
        var (_, ordemId) = Osm();
        var id = InserirPedido(ordemId, status);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PutAsync($"/api/pedidos-peca/{id}/receber", null);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Contains($"(status atual: {status})", await Erro(resposta));
        Assert.Equal(status, Pedido(id).Status);
    }

    [Theory]
    [InlineData("SOLICITADO")]
    [InlineData("ENCAMINHADO")]
    public async Task Tecnico_dono_cancela_pendente_200(string status)
    {
        var (_, ordemId) = Osm();
        var id = InserirPedido(ordemId, status);
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        using var resposta = await tecnico.PutAsync($"/api/pedidos-peca/{id}/cancelar", CptmApiFactory.Json("{ \"motivo\": \"Peça encontrada no estoque da oficina\" }"));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await LerJson(resposta);
        Assert.Equal("CANCELADO", corpo.GetProperty("status").GetString());
        Assert.Equal("Peça encontrada no estoque da oficina", corpo.GetProperty("motivoCancelamento").GetString());
        Assert.Equal("tec123", corpo.GetProperty("matriculaCancelamento").GetString());
        Assert.EndsWith("Z", corpo.GetProperty("dataCancelamento").GetString());
    }

    [Fact]
    public async Task Cancelar_sem_corpo_200_e_cancelar_resolvido_400()
    {
        var (_, ordemId) = Osm();
        var pendente = InserirPedido(ordemId, StatusPedidoPeca.Solicitado);
        var recebido = InserirPedido(ordemId, StatusPedidoPeca.Recebido);
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        using var semCorpo = await tecnico.PutAsync($"/api/pedidos-peca/{pendente}/cancelar", null);
        using var resolvido = await tecnico.PutAsync($"/api/pedidos-peca/{recebido}/cancelar", null);
        using var deNovo = await tecnico.PutAsync($"/api/pedidos-peca/{pendente}/cancelar", null);

        Assert.Equal(HttpStatusCode.OK, semCorpo.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await LerJson(semCorpo)).GetProperty("motivoCancelamento").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, resolvido.StatusCode);
        Assert.Equal("Transição inválida: só é possível cancelar um pedido SOLICITADO ou ENCAMINHADO (status atual: RECEBIDO).", await Erro(resolvido));
        Assert.Equal(HttpStatusCode.BadRequest, deNovo.StatusCode);
    }

    [Fact]
    public async Task Cancelar_por_outro_tecnico_403_e_por_admin_ou_comum_403()
    {
        GarantirSegundoTecnico();
        var (_, ordemId) = Osm();
        var id = InserirPedido(ordemId, StatusPedidoPeca.Solicitado);

        foreach (var matricula in new[] { "tec456", "admin123", "comum123" })
        {
            using var cliente = await _api.CriarClienteLogadoAsync(matricula);
            using var resposta = await cliente.PutAsync($"/api/pedidos-peca/{id}/cancelar", null);
            Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        }
        Assert.Equal(StatusPedidoPeca.Solicitado, Pedido(id).Status);
    }

    [Theory]
    [InlineData("encaminhar")]
    [InlineData("receber")]
    public async Task Acoes_do_admin_403_para_tecnico_e_comum(string acao)
    {
        var (_, ordemId) = Osm();
        var id = InserirPedido(ordemId, acao == "receber" ? StatusPedidoPeca.Encaminhado : StatusPedidoPeca.Solicitado);

        foreach (var matricula in new[] { "tec123", "comum123" })
        {
            using var cliente = await _api.CriarClienteLogadoAsync(matricula);
            using var resposta = await cliente.PutAsync($"/api/pedidos-peca/{id}/{acao}", CptmApiFactory.Json("{ \"observacaoAlmoxarifado\": \"x\" }"));
            Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        }
    }

    [Theory]
    [InlineData("encaminhar", "admin123")]
    [InlineData("receber", "admin123")]
    [InlineData("cancelar", "tec123")]
    public async Task Acoes_401_sem_token_e_404_inexistente(string acao, string matricula)
    {
        using var anonimo = _api.CriarCliente();
        using var semToken = await anonimo.PutAsync($"/api/pedidos-peca/1/{acao}", CptmApiFactory.Json("{ \"observacaoAlmoxarifado\": \"x\" }"));
        Assert.Equal(HttpStatusCode.Unauthorized, semToken.StatusCode);

        using var cliente = await _api.CriarClienteLogadoAsync(matricula);
        using var inexistente = await cliente.PutAsync($"/api/pedidos-peca/99999/{acao}", CptmApiFactory.Json("{ \"observacaoAlmoxarifado\": \"x\" }"));
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal("Pedido de peça 99999 não encontrado.", await Erro(inexistente));
    }

    [Fact]
    public async Task Receber_e_cancelar_simultaneos_so_um_vence()
    {
        var (_, ordemId) = Osm();
        var id = InserirPedido(ordemId, StatusPedidoPeca.Encaminhado);
        var tokenAdmin = await _api.LoginAsync("admin123");
        var tokenTecnico = await _api.LoginAsync("tec123");
        var clientes = Enumerable.Range(0, 6).Select(i => _api.CriarCliente(i % 2 == 0 ? tokenAdmin : tokenTecnico)).ToList();

        var respostas = await Task.WhenAll(clientes.Select((c, i) =>
            c.PutAsync($"/api/pedidos-peca/{id}/{(i % 2 == 0 ? "receber" : "cancelar")}", null)));

        Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(respostas.Where(r => r.StatusCode != HttpStatusCode.OK),
            r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict }));
        Assert.Contains(Pedido(id).Status, new[] { "RECEBIDO", "CANCELADO" });
        foreach (var r in respostas) r.Dispose();
        foreach (var c in clientes) c.Dispose();
    }

    // ---------- Bloqueio da conclusão e não execução ----------

    [Fact]
    public async Task Concluir_com_pedido_pendente_400_listando_pendentes()
    {
        var (sslId, ordemId) = Osm();
        var solicitado = InserirPedido(ordemId, StatusPedidoPeca.Solicitado, "Motor 220V");
        var encaminhado = InserirPedido(ordemId, StatusPedidoPeca.Encaminhado, "Correia");
        InserirPedido(ordemId, StatusPedidoPeca.Recebido, "Fusível");
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        using var resposta = await tecnico.PutAsync($"/api/solicitacoes/{sslId}/concluir", CptmApiFactory.Json("{ \"laudo\": \"Reparado\" }"));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var corpo = await LerJson(resposta);
        Assert.Equal($"Não é possível concluir a OSM {ordemId}: há 2 pedido(s) de peça pendente(s) (#{solicitado} Motor 220V x1 - SOLICITADO; #{encaminhado} Correia x1 - ENCAMINHADO). Aguarde a chegada ou cancele os pedidos.",
            corpo.GetProperty("erro").GetString());
        var pendentes = corpo.GetProperty("pedidosPendentes").EnumerateArray().ToList();
        Assert.Equal(new[] { solicitado, encaminhado }, pendentes.Select(p => p.GetProperty("id").GetInt32()));
        Assert.Equal("Motor 220V", pendentes[0].GetProperty("item").GetString());
        Assert.Equal(1, pendentes[0].GetProperty("quantidade").GetInt32());
        Assert.Equal("SOLICITADO", pendentes[0].GetProperty("status").GetString());
        var (s, ordem, historico) = Estado(sslId);
        Assert.Equal("EM MANUTENCAO", s.Status);
        Assert.Null(ordem.Laudo);
        Assert.Empty(historico);
    }

    [Fact]
    public async Task Concluir_com_pedidos_recebidos_ou_cancelados_200()
    {
        var (sslId, ordemId) = Osm();
        InserirPedido(ordemId, StatusPedidoPeca.Recebido);
        InserirPedido(ordemId, StatusPedidoPeca.Cancelado);
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        using var resposta = await tecnico.PutAsync($"/api/solicitacoes/{sslId}/concluir", CptmApiFactory.Json("{ \"laudo\": \"Reparado\" }"));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(0, (await LerJson(resposta)).GetProperty("pedidosCancelados").GetArrayLength());
        Assert.Equal("CONCLUIDA", Estado(sslId).Solicitacao.Status);
    }

    [Fact]
    public async Task Fluxo_pedir_encaminhar_receber_e_concluir_pela_api()
    {
        var (sslId, ordemId) = Osm();
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var criar = await tecnico.PostAsync($"/api/ordens-servico/{ordemId}/pecas", CptmApiFactory.Json(CorpoPedido));
        var id = (await LerJson(criar)).GetProperty("id").GetInt32();
        using var bloqueado = await tecnico.PutAsync($"/api/solicitacoes/{sslId}/concluir", CptmApiFactory.Json("{ \"laudo\": \"ok\" }"));
        Assert.Equal(HttpStatusCode.BadRequest, bloqueado.StatusCode);
        using var encaminhar = await admin.PutAsync($"/api/pedidos-peca/{id}/encaminhar", CptmApiFactory.Json("{ \"observacaoAlmoxarifado\": \"RM-1\" }"));
        Assert.Equal(HttpStatusCode.OK, encaminhar.StatusCode);
        using var aindaBloqueado = await tecnico.PutAsync($"/api/solicitacoes/{sslId}/concluir", CptmApiFactory.Json("{ \"laudo\": \"ok\" }"));
        Assert.Equal(HttpStatusCode.BadRequest, aindaBloqueado.StatusCode);
        using var receber = await admin.PutAsync($"/api/pedidos-peca/{id}/receber", null);
        Assert.Equal(HttpStatusCode.OK, receber.StatusCode);
        using var concluir = await tecnico.PutAsync($"/api/solicitacoes/{sslId}/concluir", CptmApiFactory.Json("{ \"laudo\": \"ok\" }"));
        Assert.Equal(HttpStatusCode.OK, concluir.StatusCode);

        using var depois = await tecnico.PostAsync($"/api/ordens-servico/{ordemId}/pecas", CptmApiFactory.Json(CorpoPedido));
        Assert.Equal(HttpStatusCode.BadRequest, depois.StatusCode);
    }

    [Fact]
    public async Task Nao_executar_cancela_os_pendentes_200()
    {
        var (sslId, ordemId) = Osm();
        var solicitado = InserirPedido(ordemId, StatusPedidoPeca.Solicitado);
        var encaminhado = InserirPedido(ordemId, StatusPedidoPeca.Encaminhado);
        var recebido = InserirPedido(ordemId, StatusPedidoPeca.Recebido);
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        using var resposta = await tecnico.PutAsync($"/api/solicitacoes/{sslId}/nao-executar", CptmApiFactory.Json("{ \"motivo\": \"Peça descontinuada\" }"));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await LerJson(resposta);
        Assert.Equal(new[] { solicitado, encaminhado }, corpo.GetProperty("pedidosCancelados").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal("NAO EXECUTADA", corpo.GetProperty("novoStatus").GetString());
        var pedidos = PedidosDa(ordemId);
        Assert.Equal("CANCELADO", pedidos.Single(p => p.Id == solicitado).Status);
        Assert.Equal("CANCELADO", pedidos.Single(p => p.Id == encaminhado).Status);
        Assert.Equal("Cancelado automaticamente: OSM não executada.", pedidos.Single(p => p.Id == encaminhado).MotivoCancelamento);
        Assert.Equal("RECEBIDO", pedidos.Single(p => p.Id == recebido).Status);
        Assert.Equal("NAO EXECUTADA", Estado(sslId).Solicitacao.Status);
    }
}
