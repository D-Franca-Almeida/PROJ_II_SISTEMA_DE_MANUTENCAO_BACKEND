using CPTM.Manutencao.Api.Controllers;
using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CPTM.Manutencao.Tests;

public sealed class CatalogoRepositoryTests : IDisposable
{
    private readonly SqliteConnection _conexao = new("DataSource=:memory:");
    private readonly ManutencaoDbContext _db;
    private readonly CatalogoRepository _repo;
    private readonly CatalogoController _controller;

    public CatalogoRepositoryTests()
    {
        _conexao.Open();
        var opcoes = new DbContextOptionsBuilder<ManutencaoDbContext>().UseSqlite(_conexao).Options;
        _db = new ManutencaoDbContext(opcoes);
        _db.Database.EnsureCreated();
        _repo = new CatalogoRepository(_db);
        _controller = new CatalogoController(_repo);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conexao.Dispose();
    }

    [Fact]
    public void Seed_tem_pelo_menos_3_estacoes_com_3_equipamentos_cada()
    {
        var estacoes = _repo.ListarEstacoes();
        Assert.True(estacoes.Count >= 3);
        foreach (var e in estacoes)
            Assert.True(_repo.ListarEquipamentos(e.Id).Count >= 3);
    }

    [Fact]
    public void Estacoes_vem_ordenadas_por_numero_da_linha_e_nome()
    {
        _db.Estacoes.Add(new Estacao { Nome = "Santo Amaro", Linha = "9-Esmeralda" });
        _db.Estacoes.Add(new Estacao { Nome = "Osasco", Linha = "9-Esmeralda" });
        _db.SaveChanges();

        var estacoes = _repo.ListarEstacoes();
        Assert.Equal(
            new[] { "7-Rubi:Luz", "9-Esmeralda:Osasco", "9-Esmeralda:Santo Amaro", "10-Turquesa:Brás", "11-Coral:Corinthians-Itaquera", "12-Safira:Calmon Viana", "13-Jade:Aeroporto-Guarulhos" },
            estacoes.Select(e => $"{e.Linha}:{e.Nome}").ToArray());
    }

    [Fact]
    public void Equipamentos_so_da_estacao_pedida()
    {
        var equipamentos = _repo.ListarEquipamentos(1);
        Assert.NotEmpty(equipamentos);
        Assert.All(equipamentos, e => Assert.Equal(1, e.EstacaoId));
    }

    [Fact]
    public void Patrimonio_duplicado_e_rejeitado()
    {
        _db.Equipamentos.Add(new Equipamento { EstacaoId = 1, Descricao = "Duplicado", Patrimonio = "PAT-LUZ-0001" });
        Assert.Throws<DbUpdateException>(() => _db.SaveChanges());
    }

    [Fact]
    public void Controller_estacoes_devolve_200_com_dtos()
    {
        var resultado = Assert.IsType<OkObjectResult>(_controller.ListarEstacoes());
        Assert.IsType<List<EstacaoDto>>(resultado.Value);
    }

    [Fact]
    public void Controller_equipamentos_sem_estacaoId_devolve_400()
    {
        Assert.IsType<BadRequestObjectResult>(_controller.ListarEquipamentos(null));
    }

    [Fact]
    public void Controller_equipamentos_estacao_inexistente_devolve_404()
    {
        Assert.IsType<NotFoundObjectResult>(_controller.ListarEquipamentos(9999));
    }

    [Fact]
    public void Controller_equipamentos_devolve_200_com_dtos()
    {
        var resultado = Assert.IsType<OkObjectResult>(_controller.ListarEquipamentos(1));
        Assert.IsType<List<EquipamentoDto>>(resultado.Value);
    }
}
