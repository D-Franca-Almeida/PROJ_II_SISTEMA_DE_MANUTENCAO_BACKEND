using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CPTM.Manutencao.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class OrdemServico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ORDEM_SERVICO",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SolicitacaoId = table.Column<int>(type: "INTEGER", nullable: false),
                    MatriculaTecnico = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    DataAbertura = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DataConclusao = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Laudo = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    MotivoNaoExecucao = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ORDEM_SERVICO", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ORDEM_SERVICO_SOLICITACOES_SolicitacaoId",
                        column: x => x.SolicitacaoId,
                        principalTable: "SOLICITACOES",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ORDEM_SERVICO",
                columns: new[] { "Id", "DataAbertura", "DataConclusao", "Laudo", "MatriculaTecnico", "MotivoNaoExecucao", "SolicitacaoId" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 11, 1, 12, 2, 0, 0, DateTimeKind.Utc), null, null, "tec123", null, 10 },
                    { 2, new DateTime(2026, 11, 1, 12, 2, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 1, 12, 3, 0, 0, DateTimeKind.Utc), "Piso refixado (carga inicial).", "tec123", null, 11 }
                });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "DataInicioManutencao", "MatriculaTecnico" },
                values: new object[] { new DateTime(2026, 11, 1, 12, 2, 0, 0, DateTimeKind.Utc), "tec123" });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "DataInicioManutencao", "MatriculaTecnico" },
                values: new object[] { new DateTime(2026, 11, 1, 12, 2, 0, 0, DateTimeKind.Utc), "tec123" });

            migrationBuilder.CreateIndex(
                name: "IX_ORDEM_SERVICO_MatriculaTecnico",
                table: "ORDEM_SERVICO",
                column: "MatriculaTecnico");

            migrationBuilder.CreateIndex(
                name: "IX_ORDEM_SERVICO_SolicitacaoId",
                table: "ORDEM_SERVICO",
                column: "SolicitacaoId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ORDEM_SERVICO");

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "DataInicioManutencao", "MatriculaTecnico" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "DataInicioManutencao", "MatriculaTecnico" },
                values: new object[] { null, null });
        }
    }
}
