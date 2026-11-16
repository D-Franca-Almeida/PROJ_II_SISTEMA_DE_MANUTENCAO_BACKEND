using System.Net;
using System.Text.Json;
using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CPTM.Manutencao.Tests.Integracao;

// CPTM-90 (US-27): GET /api/dashboard sobre a massa de carga (SSLs 1 a 12), sem alterar o banco
public sealed class DashboardHttpTests : IClassFixture<CptmApiFactory>
{
    private readonly CptmApiFactory _api;

    public DashboardHttpTests(CptmApiFactory api)
    {
        _api = api;
    }

    internal static async Task<JsonElement> LerJson(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task Sem_token_401()
    {
        using var anonimo = _api.CriarCliente();

        using var resposta = await anonimo.GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Theory]
    [InlineData("comum123")]
    [InlineData("tec123")]
    public async Task Perfil_que_nao_e_admin_403(string matricula)
    {
        using var cliente = await _api.CriarClienteLogadoAsync(matricula);

        using var resposta = await cliente.GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task Admin_200_com_contagem_por_status_igual_a_massa_de_carga()
    {
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await LerJson(resposta);
        Assert.Equal(12, corpo.GetProperty("totalSolicitacoes").GetInt32());
        var porStatus = corpo.GetProperty("porStatus").EnumerateArray()
            .ToDictionary(c => c.GetProperty("status").GetString()!, c => c.GetProperty("quantidade").GetInt32());
        Assert.Equal(StatusSolicitacao.Todos.Length, porStatus.Count);
        Assert.Equal(6, porStatus["SOLICITADA"]);
        Assert.Equal(1, porStatus["REENVIADA"]);
        Assert.Equal(1, porStatus["APROVADA"]);
        Assert.Equal(1, porStatus["DEVOLVIDA"]);
        Assert.Equal(1, porStatus["CANCELADA"]);
        Assert.Equal(1, porStatus["EM MANUTENCAO"]);
        Assert.Equal(1, porStatus["CONCLUIDA"]);
        Assert.Equal(0, porStatus["RETIRADO"]);
        Assert.Equal(0, porStatus["EXPIRADA"]);
    }

    [Fact]
    public async Task Admin_200_com_tempos_medios_do_historico_de_carga()
    {
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.GetAsync("/api/dashboard");

        var tempos = (await LerJson(resposta)).GetProperty("temposMedios").EnumerateArray().ToList();
        Assert.Equal(3, tempos.Count);
        Assert.Equal("SOLICITADA", tempos[0].GetProperty("de").GetString());
        Assert.Equal("APROVADA", tempos[0].GetProperty("para").GetString());
        Assert.Equal(1, tempos[0].GetProperty("quantidade").GetInt32());
        Assert.Equal(0.02, tempos[0].GetProperty("mediaHoras").GetDouble(), 2);
        Assert.Equal(JsonValueKind.Null, tempos[1].GetProperty("mediaHoras").ValueKind);
        Assert.Equal(0, tempos[1].GetProperty("quantidade").GetInt32());
        Assert.Equal("RECEBIDA", tempos[2].GetProperty("de").GetString());
        Assert.Equal(JsonValueKind.Null, tempos[2].GetProperty("mediaHoras").ValueKind);
    }

    [Fact]
    public async Task Admin_200_lista_as_da_carga_ja_vencidas_com_dias_zero_e_datas_em_utc()
    {
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.GetAsync("/api/dashboard");

        var lista = (await LerJson(resposta)).GetProperty("expiramEmAteDoisDias").EnumerateArray().ToList();
        Assert.Equal(new[] { 8, 9 }, lista.Select(s => s.GetProperty("id").GetInt32()));
        Assert.All(lista, s => Assert.Equal(0, s.GetProperty("diasParaExpirar").GetInt32()));
        Assert.All(lista, s => Assert.EndsWith("Z", s.GetProperty("dataAbertura").GetString()));
    }
}

// Uma SSL APROVADA com a aprovação há 9 dias entra na lista com 1 dia; usa fábrica própria porque grava no banco
public sealed class DashboardHttpComMassaTests : IClassFixture<CptmApiFactory>
{
    private readonly CptmApiFactory _api;

    public DashboardHttpComMassaTests(CptmApiFactory api)
    {
        _api = api;
    }

    private int InserirAprovadaHaDiasComHistorico(int dias, int horasAteAprovar)
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
        var aprovacao = DateTime.UtcNow.AddDays(-dias);
        var s = new Solicitacao
        {
            Descricao = "SSL aprovada", Status = StatusSolicitacao.Aprovada, MatriculaSolicitante = "comum123", EquipamentoId = 1,
            DataAbertura = aprovacao.AddHours(-horasAteAprovar)
        };
        db.Solicitacoes.Add(s);
        db.SaveChanges();
        db.HistoricoStatus.AddRange(
            new HistoricoStatus { SolicitacaoId = s.Id, StatusNovo = "SOLICITADA", Matricula = "comum123", Data = s.DataAbertura },
            new HistoricoStatus { SolicitacaoId = s.Id, StatusAnterior = "SOLICITADA", StatusNovo = "APROVADA", Matricula = "admin123", Data = aprovacao });
        db.SaveChanges();
        return s.Id;
    }

    [Fact]
    public async Task Ssl_aprovada_ha_9_dias_entra_na_lista_com_1_dia_e_conta_no_status_e_no_tempo_medio()
    {
        var perto = InserirAprovadaHaDiasComHistorico(dias: 9, horasAteAprovar: 30);
        var longe = InserirAprovadaHaDiasComHistorico(dias: 3, horasAteAprovar: 2);
        using var admin = await _api.CriarClienteLogadoAsync("admin123");

        using var resposta = await admin.GetAsync("/api/dashboard");

        var corpo = await DashboardHttpTests.LerJson(resposta);
        var lista = corpo.GetProperty("expiramEmAteDoisDias").EnumerateArray().ToList();
        var item = Assert.Single(lista, s => s.GetProperty("id").GetInt32() == perto);
        Assert.Equal(1, item.GetProperty("diasParaExpirar").GetInt32());
        Assert.DoesNotContain(lista, s => s.GetProperty("id").GetInt32() == longe);
        var aprovadas = corpo.GetProperty("porStatus").EnumerateArray().Single(c => c.GetProperty("status").GetString() == "APROVADA");
        Assert.Equal(3, aprovadas.GetProperty("quantidade").GetInt32());
        var etapa = corpo.GetProperty("temposMedios")[0];
        Assert.Equal(3, etapa.GetProperty("quantidade").GetInt32());
        Assert.Equal((0.0166667 + 30 + 2) / 3, etapa.GetProperty("mediaHoras").GetDouble(), 1);
    }
}
