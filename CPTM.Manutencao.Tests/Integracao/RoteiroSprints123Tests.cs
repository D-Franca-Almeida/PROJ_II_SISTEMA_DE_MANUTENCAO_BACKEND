using System.Net;
using System.Text.Json;
using CPTM.Manutencao.Api.Models;
using Xunit.Sdk;

namespace CPTM.Manutencao.Tests.Integracao;

public sealed class RoteiroSprints123Tests : IClassFixture<CptmApiFactory>
{
    private readonly CptmApiFactory _api;

    public RoteiroSprints123Tests(CptmApiFactory api)
    {
        _api = api;
    }

    private static async Task<JsonElement?> Passo(string passo, HttpStatusCode esperado, Task<HttpResponseMessage> chamada)
    {
        using var resposta = await chamada;
        var corpo = await resposta.Content.ReadAsStringAsync();
        if (resposta.StatusCode != esperado)
            throw new XunitException($"Passo #{passo}: esperado {(int)esperado}, obtido {(int)resposta.StatusCode}. Corpo: {corpo}");
        return string.IsNullOrWhiteSpace(corpo) ? null : JsonDocument.Parse(corpo).RootElement.Clone();
    }

    private static void TemPropriedade(string passo, JsonElement? corpo, string propriedade)
    {
        if (corpo is null || !corpo.Value.TryGetProperty(propriedade, out var valor) || string.IsNullOrWhiteSpace(valor.ToString()))
            throw new XunitException($"Passo #{passo}: corpo sem '{propriedade}': {corpo}");
    }

    private static MultipartFormDataContent Formulario(params (string Nome, string Valor)[] campos)
    {
        var form = new MultipartFormDataContent();
        foreach (var (nome, valor) in campos)
            form.Add(new StringContent(valor), nome);
        return form;
    }

    [Fact]
    public async Task Roteiro_sprints_1_2_3_na_ordem()
    {
        using var anonimo = _api.CriarCliente();
        using var comum = await _api.CriarClienteLogadoAsync("comum123");
        using var admin = await _api.CriarClienteLogadoAsync("admin123");
        using var tecnico = await _api.CriarClienteLogadoAsync("tec123");

        await Passo("1", HttpStatusCode.Unauthorized, anonimo.PutAsync("/api/Solicitacoes/1/aprovar", null));
        await Passo("2", HttpStatusCode.Forbidden, comum.PutAsync("/api/Solicitacoes/1/aprovar", null));

        await Passo("3", HttpStatusCode.Forbidden, comum.PutAsync("/api/Solicitacoes/4/cancelar", null));
        TemPropriedade("4", await Passo("4", HttpStatusCode.OK, comum.PutAsync("/api/Solicitacoes/3/cancelar", null)), "message");
        TemPropriedade("5", await Passo("5", HttpStatusCode.BadRequest,
            admin.PutAsync("/api/Solicitacoes/2/devolver", CptmApiFactory.Json("{ \"motivo\": \"   \" }"))), "erro");
        TemPropriedade("6", await Passo("6", HttpStatusCode.OK,
            admin.PutAsync("/api/Solicitacoes/2/devolver", CptmApiFactory.Json("{ \"motivo\": \"Foto do equipamento ilegivel\" }"))), "message");
        TemPropriedade("7", await Passo("7", HttpStatusCode.OK, admin.PutAsync("/api/Solicitacoes/1/aprovar", null)), "message");
        TemPropriedade("7b", await Passo("7b", HttpStatusCode.BadRequest, admin.PutAsync("/api/Solicitacoes/3/aprovar", null)), "erro");

        await Passo("8a", HttpStatusCode.Forbidden, admin.PutAsync("/api/Solicitacoes/1/iniciar-manutencao", null));
        TemPropriedade("8", await Passo("8", HttpStatusCode.BadRequest, tecnico.PutAsync("/api/Solicitacoes/2/iniciar-manutencao", null)), "erro");
        TemPropriedade("9a", await Passo("9a", HttpStatusCode.BadRequest, tecnico.PutAsync("/api/Solicitacoes/1/iniciar-manutencao", null)), "erro");
        TemPropriedade("9b", await Passo("9b", HttpStatusCode.OK, admin.PutAsync("/api/Solicitacoes/1/receber", null)), "numeroSsm");
        var passo9 = await Passo("9", HttpStatusCode.OK, tecnico.PutAsync("/api/Solicitacoes/1/iniciar-manutencao", null));
        Assert.Equal(StatusSolicitacao.EmManutencao, passo9!.Value.GetProperty("novoStatus").GetString());
        Assert.Equal("tec123", passo9.Value.GetProperty("tecnicoResponsavel").GetString());
        Assert.Equal(1, passo9.Value.GetProperty("solicitacaoId").GetInt32());
        TemPropriedade("9", passo9, "ordemServicoId");

        TemPropriedade("10", await Passo("10", HttpStatusCode.BadRequest,
            comum.PutAsync("/api/Solicitacoes/2/reenviar", Formulario(("descricao", "   ")))), "erro");
        var passo11 = await Passo("11", HttpStatusCode.OK,
            comum.PutAsync("/api/Solicitacoes/2/reenviar", Formulario(("descricao", "Foto refeita com flash"))));
        Assert.Equal(StatusSolicitacao.Reenviada, passo11!.Value.GetProperty("status").GetString());

        TemPropriedade("12", await Passo("12", HttpStatusCode.OK, admin.PutAsync("/api/Solicitacoes/2/aprovar", null)), "message");
        TemPropriedade("13", await Passo("13", HttpStatusCode.OK, comum.PutAsync("/api/Solicitacoes/2/cancelar", null)), "message");
        TemPropriedade("14", await Passo("14", HttpStatusCode.BadRequest, comum.PutAsync("/api/Solicitacoes/1/cancelar", null)), "erro");

        await Passo("15", HttpStatusCode.Unauthorized, anonimo.PutAsync("/api/Solicitacoes/6/receber", null));
        await Passo("16", HttpStatusCode.Forbidden, comum.PutAsync("/api/Solicitacoes/6/receber", null));
        TemPropriedade("17", await Passo("17", HttpStatusCode.BadRequest, admin.PutAsync("/api/Solicitacoes/6/receber", null)), "erro");
        TemPropriedade("18", await Passo("18", HttpStatusCode.OK, admin.PutAsync("/api/Solicitacoes/6/aprovar", null)), "message");
        var passo19 = await Passo("19", HttpStatusCode.OK, admin.PutAsync("/api/Solicitacoes/6/receber", null));
        Assert.Equal(StatusSolicitacao.Recebida, passo19!.Value.GetProperty("novoStatus").GetString());
        Assert.Matches(@"^SSM-\d{4}-\d{6}$", passo19.Value.GetProperty("numeroSsm").GetString());
        TemPropriedade("20", await Passo("20", HttpStatusCode.BadRequest, comum.PutAsync("/api/Solicitacoes/6/cancelar", null)), "erro");
        TemPropriedade("21", await Passo("21", HttpStatusCode.BadRequest, admin.PutAsync("/api/Solicitacoes/6/receber", null)), "erro");

        await Passo("22", HttpStatusCode.Forbidden, comum.GetAsync("/api/Solicitacoes/recebidas"));
        var passo23 = await Passo("23", HttpStatusCode.OK, tecnico.GetAsync("/api/Solicitacoes/recebidas"));
        Assert.Contains(passo23!.Value.EnumerateArray(), s => s.GetProperty("id").GetInt32() == 6);
        var passo24 = await Passo("24", HttpStatusCode.OK, tecnico.PutAsync("/api/Solicitacoes/6/iniciar-manutencao", null));
        TemPropriedade("24", passo24, "ordemServicoId");
        TemPropriedade("25", await Passo("25", HttpStatusCode.BadRequest, tecnico.PutAsync("/api/Solicitacoes/6/iniciar-manutencao", null)), "erro");
        var passo26 = await Passo("26", HttpStatusCode.OK, tecnico.GetAsync("/api/ordens-servico/minhas"));
        var solicitacoesDasOsms = passo26!.Value.EnumerateArray().Select(o => o.GetProperty("solicitacaoId").GetInt32()).ToList();
        Assert.Contains(1, solicitacoesDasOsms);
        Assert.Contains(6, solicitacoesDasOsms);
        await Passo("27", HttpStatusCode.Forbidden, admin.GetAsync("/api/ordens-servico/minhas"));

        await Passo("28", HttpStatusCode.Unauthorized, anonimo.PutAsync("/api/Solicitacoes/1/concluir", CptmApiFactory.Json("{ \"laudo\": \"x\" }")));
        await Passo("29", HttpStatusCode.Forbidden, admin.PutAsync("/api/Solicitacoes/1/concluir", CptmApiFactory.Json("{ \"laudo\": \"x\" }")));
        TemPropriedade("30", await Passo("30", HttpStatusCode.BadRequest,
            tecnico.PutAsync("/api/Solicitacoes/1/concluir", CptmApiFactory.Json("{ \"laudo\": \"  \" }"))), "erro");
        var passo31 = await Passo("31", HttpStatusCode.OK,
            tecnico.PutAsync("/api/Solicitacoes/1/concluir", CptmApiFactory.Json("{ \"laudo\": \"Fonte substituida e testada\" }")));
        Assert.Equal(StatusSolicitacao.Concluida, passo31!.Value.GetProperty("novoStatus").GetString());
        TemPropriedade("31", passo31, "dataConclusao");
        TemPropriedade("32", await Passo("32", HttpStatusCode.BadRequest,
            tecnico.PutAsync("/api/Solicitacoes/1/concluir", CptmApiFactory.Json("{ \"laudo\": \"x\" }"))), "erro");
        TemPropriedade("33", await Passo("33", HttpStatusCode.BadRequest,
            tecnico.PutAsync("/api/Solicitacoes/6/nao-executar", CptmApiFactory.Json("{ }"))), "erro");
        var osmDaSsl6 = passo24!.Value.GetProperty("ordemServicoId").GetInt32();
        var passo33a = await Passo("33a", HttpStatusCode.Created, tecnico.PostAsync($"/api/ordens-servico/{osmDaSsl6}/pecas",
            CptmApiFactory.Json("{ \"item\": \"Motor 220V\", \"quantidade\": 1, \"justificativa\": \"Motor queimado\" }")));
        var pedidoDaSsl6 = passo33a!.Value.GetProperty("id").GetInt32();
        var passo34 = await Passo("34", HttpStatusCode.OK,
            tecnico.PutAsync("/api/Solicitacoes/6/nao-executar", CptmApiFactory.Json("{ \"motivo\": \"Equipamento sem conserto viavel\" }")));
        Assert.Equal(StatusSolicitacao.NaoExecutada, passo34!.Value.GetProperty("novoStatus").GetString());
        Assert.Equal(pedidoDaSsl6, Assert.Single(passo34.Value.GetProperty("pedidosCancelados").EnumerateArray()).GetInt32());
        var passo34a = await Passo("34a", HttpStatusCode.OK, tecnico.GetAsync($"/api/ordens-servico/{osmDaSsl6}/pecas"));
        Assert.Equal("CANCELADO", Assert.Single(passo34a!.Value.EnumerateArray()).GetProperty("status").GetString());
        TemPropriedade("35", await Passo("35", HttpStatusCode.NotFound,
            tecnico.PutAsync("/api/Solicitacoes/9999/concluir", CptmApiFactory.Json("{ \"laudo\": \"x\" }"))), "erro");

        const string pedido = "{ \"item\": \"Capacitor 100uF\", \"quantidade\": 2, \"justificativa\": \"Capacitor estufado na placa\" }";
        await Passo("36", HttpStatusCode.Unauthorized, anonimo.PostAsync("/api/ordens-servico/1/pecas", CptmApiFactory.Json(pedido)));
        await Passo("37", HttpStatusCode.Forbidden, admin.PostAsync("/api/ordens-servico/1/pecas", CptmApiFactory.Json(pedido)));
        TemPropriedade("38", await Passo("38", HttpStatusCode.BadRequest, tecnico.PostAsync("/api/ordens-servico/1/pecas",
            CptmApiFactory.Json("{ \"item\": \"Capacitor\", \"quantidade\": 0, \"justificativa\": \"x\" }"))), "erro");
        var passo39 = await Passo("39", HttpStatusCode.Created, tecnico.PostAsync("/api/ordens-servico/1/pecas", CptmApiFactory.Json(pedido)));
        Assert.Equal("SOLICITADO", passo39!.Value.GetProperty("status").GetString());
        var pedidoId = passo39.Value.GetProperty("id").GetInt32();
        var passo40 = await Passo("40", HttpStatusCode.BadRequest,
            tecnico.PutAsync("/api/Solicitacoes/10/concluir", CptmApiFactory.Json("{ \"laudo\": \"Placa reparada\" }")));
        Assert.Equal(pedidoId, Assert.Single(passo40!.Value.GetProperty("pedidosPendentes").EnumerateArray()).GetProperty("id").GetInt32());
        await Passo("41", HttpStatusCode.Forbidden, comum.GetAsync("/api/pedidos-peca"));
        var passo42 = await Passo("42", HttpStatusCode.OK, admin.GetAsync("/api/pedidos-peca?status=PENDENTES"));
        Assert.Contains(passo42!.Value.EnumerateArray(), p => p.GetProperty("id").GetInt32() == pedidoId);
        TemPropriedade("43", await Passo("43", HttpStatusCode.BadRequest, admin.PutAsync($"/api/pedidos-peca/{pedidoId}/receber", null)), "erro");
        TemPropriedade("44", await Passo("44", HttpStatusCode.BadRequest,
            admin.PutAsync($"/api/pedidos-peca/{pedidoId}/encaminhar", CptmApiFactory.Json("{ }"))), "erro");
        var passo45 = await Passo("45", HttpStatusCode.OK, admin.PutAsync($"/api/pedidos-peca/{pedidoId}/encaminhar",
            CptmApiFactory.Json("{ \"observacaoAlmoxarifado\": \"Requisicao RM-2026-0042\" }")));
        Assert.Equal("ENCAMINHADO", passo45!.Value.GetProperty("status").GetString());
        var passo46 = await Passo("46", HttpStatusCode.OK, admin.PutAsync($"/api/pedidos-peca/{pedidoId}/receber", null));
        Assert.Equal("RECEBIDO", passo46!.Value.GetProperty("status").GetString());
        TemPropriedade("47", await Passo("47", HttpStatusCode.BadRequest, tecnico.PutAsync($"/api/pedidos-peca/{pedidoId}/cancelar", null)), "erro");
        var passo48 = await Passo("48", HttpStatusCode.OK, tecnico.GetAsync("/api/ordens-servico/1/pecas"));
        Assert.Equal("RECEBIDO", Assert.Single(passo48!.Value.EnumerateArray()).GetProperty("status").GetString());
        var passo49 = await Passo("49", HttpStatusCode.OK,
            tecnico.PutAsync("/api/Solicitacoes/10/concluir", CptmApiFactory.Json("{ \"laudo\": \"Placa reparada\" }")));
        Assert.Equal(StatusSolicitacao.Concluida, passo49!.Value.GetProperty("novoStatus").GetString());
    }
}
