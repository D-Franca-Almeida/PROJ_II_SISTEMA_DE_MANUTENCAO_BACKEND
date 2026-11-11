using CPTM.Manutencao.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CPTM.Manutencao.Tests;

public sealed class MigracaoSolicitacaoEquipamentoFotoTests
{
    [Fact]
    public void Linhas_existentes_fora_do_seed_recebem_data_de_abertura_do_historico()
    {
        using var conexao = new SqliteConnection("DataSource=:memory:");
        conexao.Open();
        using var db = new ManutencaoDbContext(new DbContextOptionsBuilder<ManutencaoDbContext>().UseSqlite(conexao).Options);
        var migrador = db.GetService<IMigrator>();
        migrador.Migrate("CatalogoEstacaoEquipamento");

        db.Database.ExecuteSqlRaw(
            "INSERT INTO SOLICITACOES (Id, Descricao, Status, MatriculaSolicitante) VALUES (50, 'antiga', 'SOLICITADA', 'comum123'), (51, 'sem historico', 'SOLICITADA', 'comum123');");
        db.Database.ExecuteSqlRaw(
            "INSERT INTO HISTORICO_STATUS (SolicitacaoId, StatusAnterior, StatusNovo, Matricula, Data) VALUES (50, NULL, 'SOLICITADA', 'comum123', '2026-10-05 08:30:00');");

        migrador.Migrate();

        var antiga = db.Solicitacoes.AsNoTracking().Single(s => s.Id == 50);
        Assert.Equal(new DateTime(2026, 10, 5, 8, 30, 0, DateTimeKind.Utc), antiga.DataAbertura);
        Assert.Equal(DateTimeKind.Utc, antiga.DataAbertura.Kind);
        Assert.True(db.Solicitacoes.AsNoTracking().Single(s => s.Id == 51).DataAbertura > new DateTime(2026, 1, 1));
        Assert.All(db.Solicitacoes.AsNoTracking().Where(s => s.Id <= 12).ToList(),
            s => Assert.Equal(new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc), s.DataAbertura));
    }
}
