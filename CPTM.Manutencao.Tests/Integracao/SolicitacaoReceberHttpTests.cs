using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CPTM.Manutencao.Tests.Integracao;

public sealed class SolicitacaoReceberHttpTests : IClassFixture<CptmApiFactory>
{
    private static readonly Regex FormatoSsm = new(@"^SSM-\d{4}-\d{6}$");
    private readonly CptmApiFactory _api;

    public SolicitacaoReceberHttpTests(CptmApiFactory api)
    {
        _api = api;
    }

    private int Inserir(string status, string dono = "comum123", DateTime? desde = null)
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        var s = new Solicitacao
        {
            Descricao = "SSL de teste",
            Status = status,
            MatriculaSolicitante = dono,
            EquipamentoId = 1,
            DataAbertura = desde ?? DateTime.UtcNow
        };
        db.Solicitacoes.Add(s);
        db.SaveChanges();
        return s.Id;
    }

    private (Solicitacao Solicitacao, List<HistoricoStatus> Historico) Estado(int id)
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        return (db.Solicitacoes.AsNoTracking().Single(x => x.Id == id),
            db.HistoricoStatus.AsNoTracking().Where(h => h.SolicitacaoId == id).OrderBy(h => h.Id).ToList());
    }

    private static async Task<JsonElement> LerJson(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.Clone();

    private static int Sequencial(string numeroSsm) => int.Parse(numeroSsm[^6..]);

    [Fact]
    public async Task Admin_recebe_aprovada_200_com_numero_ssm_e_data_utc()
    {
        var id = Inserir(StatusSolicitacao.Aprovada);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PutAsync($"/api/solicitacoes/{id}/receber", null);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await LerJson(resposta);
        var numero = corpo.GetProperty("numeroSsm").GetString()!;
        Assert.Matches(FormatoSsm, numero);
        Assert.StartsWith($"SSM-{DateTime.UtcNow.Year}-", numero);
        Assert.Equal(id, corpo.GetProperty("solicitacaoId").GetInt32());
        Assert.Equal("RECEBIDA", corpo.GetProperty("novoStatus").GetString());
        Assert.Equal($"Equipamento recebido na oficina. SSM {numero} gerada.", corpo.GetProperty("message").GetString());
        Assert.EndsWith("Z", corpo.GetProperty("dataRecebimento").GetString());

        var (s, historico) = Estado(id);
        Assert.Equal("RECEBIDA", s.Status);
        Assert.Equal(numero, s.NumeroSsm);
        Assert.NotNull(s.DataRecebimento);
        var h = Assert.Single(historico);
        Assert.Equal("APROVADA", h.StatusAnterior);
        Assert.Equal("RECEBIDA", h.StatusNovo);
        Assert.Equal("admin123", h.Matricula);
        Assert.Equal($"SSM {numero} gerada.", h.Observacao);
    }

    [Fact]
    public async Task Numeros_sao_unicos_e_crescentes_continuando_a_carga_inicial()
    {
        var a = Inserir(StatusSolicitacao.Aprovada);
        var b = Inserir(StatusSolicitacao.Aprovada);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var ra = await admin.PutAsync($"/api/solicitacoes/{a}/receber", null);
        using var rb = await admin.PutAsync($"/api/solicitacoes/{b}/receber", null);

        var na = (await LerJson(ra)).GetProperty("numeroSsm").GetString()!;
        var nb = (await LerJson(rb)).GetProperty("numeroSsm").GetString()!;
        Assert.True(Sequencial(nb) > Sequencial(na));
        if (DateTime.UtcNow.Year == 2026)
            Assert.True(Sequencial(na) >= 3);
    }

    [Fact]
    public async Task Detalhe_e_listagem_expoem_numero_ssm_e_data_de_recebimento()
    {
        var id = Inserir(StatusSolicitacao.Aprovada);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        using var recebida = await admin.PutAsync($"/api/solicitacoes/{id}/receber", null);
        var numero = (await LerJson(recebida)).GetProperty("numeroSsm").GetString();

        using var detalhe = await comum.GetAsync($"/api/solicitacoes/{id}");
        using var fila = await admin.GetAsync("/api/solicitacoes?status=RECEBIDA");
        using var minhas = await comum.GetAsync("/api/solicitacoes/minhas");

        var d = await LerJson(detalhe);
        Assert.Equal("RECEBIDA", d.GetProperty("status").GetString());
        Assert.Equal(numero, d.GetProperty("numeroSsm").GetString());
        Assert.EndsWith("Z", d.GetProperty("dataRecebimento").GetString());
        Assert.Equal(JsonValueKind.Null, d.GetProperty("diasParaExpirar").ValueKind);
        Assert.Equal("RECEBIDA", d.GetProperty("historico").EnumerateArray().Last().GetProperty("statusNovo").GetString());

        var itensFila = (await LerJson(fila)).EnumerateArray().ToList();
        Assert.All(itensFila, i => Assert.Equal("RECEBIDA", i.GetProperty("status").GetString()));
        Assert.Equal(numero, itensFila.Single(i => i.GetProperty("id").GetInt32() == id).GetProperty("numeroSsm").GetString());

        var item = (await LerJson(minhas)).EnumerateArray().Single(i => i.GetProperty("id").GetInt32() == id);
        Assert.Equal(numero, item.GetProperty("numeroSsm").GetString());
        Assert.EndsWith("Z", item.GetProperty("dataRecebimento").GetString());
    }

    [Fact]
    public async Task Ssl_sem_recebimento_expoe_numero_ssm_nulo()
    {
        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        using var resposta = await admin.GetAsync("/api/solicitacoes/1");

        var corpo = await LerJson(resposta);
        Assert.Equal(JsonValueKind.Null, corpo.GetProperty("numeroSsm").ValueKind);
        Assert.Equal(JsonValueKind.Null, corpo.GetProperty("dataRecebimento").ValueKind);
    }

    [Theory]
    [InlineData("SOLICITADA")]
    [InlineData("REENVIADA")]
    [InlineData("DEVOLVIDA")]
    [InlineData("RECEBIDA")]
    [InlineData("EM MANUTENCAO")]
    [InlineData("CONCLUIDA")]
    [InlineData("CANCELADA")]
    [InlineData("EXPIRADA")]
    public async Task Fora_de_aprovada_400_sem_numero_nem_historico(string status)
    {
        var id = Inserir(status);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PutAsync($"/api/solicitacoes/{id}/receber", null);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal($"Transição inválida: só é possível receber na oficina uma solicitação APROVADA (status atual: {status}).",
            (await LerJson(resposta)).GetProperty("erro").GetString());
        var (s, historico) = Estado(id);
        Assert.Equal(status, s.Status);
        Assert.Null(s.NumeroSsm);
        Assert.Empty(historico);
    }

    [Fact]
    public async Task Receber_de_novo_400_e_mantem_o_numero()
    {
        var id = Inserir(StatusSolicitacao.Aprovada);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        using var primeira = await admin.PutAsync($"/api/solicitacoes/{id}/receber", null);
        var numero = (await LerJson(primeira)).GetProperty("numeroSsm").GetString();

        using var segunda = await admin.PutAsync($"/api/solicitacoes/{id}/receber", null);

        Assert.Equal(HttpStatusCode.BadRequest, segunda.StatusCode);
        var (s, historico) = Estado(id);
        Assert.Equal(numero, s.NumeroSsm);
        Assert.Single(historico);
    }

    [Fact]
    public async Task Recebimentos_simultaneos_da_mesma_ssl_so_um_vence()
    {
        var id = Inserir(StatusSolicitacao.Aprovada);
        var token = await _api.LoginAsync("admin123");
        var clientes = Enumerable.Range(0, 4).Select(_ => _api.CriarCliente(token)).ToList();

        var respostas = await Task.WhenAll(clientes.Select(c => c.PutAsync($"/api/solicitacoes/{id}/receber", null)));

        Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(respostas.Where(r => r.StatusCode != HttpStatusCode.OK),
            r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict }));
        var (s, historico) = Estado(id);
        Assert.Equal("RECEBIDA", s.Status);
        Assert.Single(historico);
        foreach (var r in respostas) r.Dispose();
        foreach (var c in clientes) c.Dispose();
    }

    [Fact]
    public async Task Recebimentos_simultaneos_de_ssls_diferentes_geram_numeros_distintos()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => Inserir(StatusSolicitacao.Aprovada)).ToList();
        var token = await _api.LoginAsync("admin123");
        var clientes = ids.Select(_ => _api.CriarCliente(token)).ToList();

        var respostas = await Task.WhenAll(ids.Select((id, i) => clientes[i].PutAsync($"/api/solicitacoes/{id}/receber", null)));

        Assert.All(respostas, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var numeros = ids.Select(id => Estado(id).Solicitacao.NumeroSsm).ToList();
        Assert.All(numeros, n => Assert.Matches(FormatoSsm, n!));
        Assert.Equal(numeros.Count, numeros.Distinct().Count());
        foreach (var r in respostas) r.Dispose();
        foreach (var c in clientes) c.Dispose();
    }

    [Theory]
    [InlineData("comum123")]
    [InlineData("tec123")]
    public async Task Perfil_nao_admin_403(string matricula)
    {
        var id = Inserir(StatusSolicitacao.Aprovada);
        using var cliente = await _api.CriarClienteLogadoAsync(matricula);

        using var resposta = await cliente.PutAsync($"/api/solicitacoes/{id}/receber", null);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("APROVADA", Estado(id).Solicitacao.Status);
    }

    [Fact]
    public async Task Sem_token_401()
    {
        using var anonimo = _api.CriarCliente();
        using var resposta = await anonimo.PutAsync("/api/solicitacoes/8/receber", null);
        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Inexistente_404()
    {
        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        using var resposta = await admin.PutAsync("/api/solicitacoes/9999/receber", null);
        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.Equal("Solicitação 9999 não encontrada.", (await LerJson(resposta)).GetProperty("erro").GetString());
    }

    [Fact]
    public async Task Recebida_nao_pode_ser_cancelada_por_comum_nem_admin()
    {
        var id = Inserir(StatusSolicitacao.Aprovada);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        using var recebida = await admin.PutAsync($"/api/solicitacoes/{id}/receber", null);
        Assert.Equal(HttpStatusCode.OK, recebida.StatusCode);

        foreach (var cliente in new[] { comum, admin })
        {
            using var resposta = await cliente.PutAsync($"/api/solicitacoes/{id}/cancelar", null);
            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
            Assert.Equal("Não é possível cancelar uma solicitação com status RECEBIDA.",
                (await LerJson(resposta)).GetProperty("erro").GetString());
        }
        Assert.Equal("RECEBIDA", Estado(id).Solicitacao.Status);
    }

    [Fact]
    public async Task Recebida_antiga_nao_expira()
    {
        var id = Inserir(StatusSolicitacao.Recebida, desde: DateTime.UtcNow.AddDays(-40));
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.PostAsync("/api/admin/expiracao/executar", null);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var ids = (await LerJson(resposta)).GetProperty("solicitacoes").EnumerateArray().Select(x => x.GetProperty("id").GetInt32());
        Assert.DoesNotContain(id, ids);
        Assert.Equal("RECEBIDA", Estado(id).Solicitacao.Status);
    }

    [Fact]
    public async Task Filtro_status_recebida_e_aceito_na_fila_do_admin()
    {
        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        using var resposta = await admin.GetAsync("/api/solicitacoes?status=recebida");
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }
}
