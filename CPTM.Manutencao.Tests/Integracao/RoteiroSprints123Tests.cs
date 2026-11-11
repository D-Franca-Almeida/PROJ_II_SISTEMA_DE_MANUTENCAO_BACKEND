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
        var passo9 = await Passo("9", HttpStatusCode.OK, tecnico.PutAsync("/api/Solicitacoes/1/iniciar-manutencao", null));
        Assert.Equal(StatusSolicitacao.EmManutencao, passo9!.Value.GetProperty("novoStatus").GetString());
        Assert.Equal("tec123", passo9.Value.GetProperty("tecnicoResponsavel").GetString());
        Assert.Equal(1, passo9.Value.GetProperty("solicitacaoId").GetInt32());
    }
}
