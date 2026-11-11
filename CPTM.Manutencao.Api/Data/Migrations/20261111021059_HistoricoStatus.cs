using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CPTM.Manutencao.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class HistoricoStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HISTORICO_STATUS",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SolicitacaoId = table.Column<int>(type: "INTEGER", nullable: false),
                    StatusAnterior = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    StatusNovo = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Matricula = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Data = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Observacao = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HISTORICO_STATUS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HISTORICO_STATUS_SOLICITACOES_SolicitacaoId",
                        column: x => x.SolicitacaoId,
                        principalTable: "SOLICITACOES",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "HISTORICO_STATUS",
                columns: new[] { "Id", "Data", "Matricula", "Observacao", "SolicitacaoId", "StatusAnterior", "StatusNovo" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 11, 1, 12, 0, 0, 0, DateTimeKind.Utc), "comum123", null, 1, null, "SOLICITADA" },
                    { 2, new DateTime(2026, 11, 1, 12, 0, 0, 0, DateTimeKind.Utc), "comum123", null, 2, null, "SOLICITADA" },
                    { 3, new DateTime(2026, 11, 1, 12, 0, 0, 0, DateTimeKind.Utc), "comum123", null, 3, null, "SOLICITADA" },
                    { 4, new DateTime(2026, 11, 1, 12, 0, 0, 0, DateTimeKind.Utc), "outro456", null, 4, null, "SOLICITADA" },
                    { 5, new DateTime(2026, 11, 1, 12, 0, 0, 0, DateTimeKind.Utc), "comum123", null, 5, null, "SOLICITADA" },
                    { 6, new DateTime(2026, 11, 1, 12, 1, 0, 0, DateTimeKind.Utc), "SISTEMA", "Carga inicial", 5, "SOLICITADA", "CANCELADA" },
                    { 7, new DateTime(2026, 11, 1, 12, 0, 0, 0, DateTimeKind.Utc), "comum123", null, 6, null, "SOLICITADA" },
                    { 8, new DateTime(2026, 11, 1, 12, 0, 0, 0, DateTimeKind.Utc), "comum123", null, 7, null, "SOLICITADA" },
                    { 9, new DateTime(2026, 11, 1, 12, 0, 0, 0, DateTimeKind.Utc), "comum123", null, 8, null, "SOLICITADA" },
                    { 10, new DateTime(2026, 11, 1, 12, 1, 0, 0, DateTimeKind.Utc), "SISTEMA", "Carga inicial", 8, "SOLICITADA", "APROVADA" },
                    { 11, new DateTime(2026, 11, 1, 12, 0, 0, 0, DateTimeKind.Utc), "comum123", null, 9, null, "SOLICITADA" },
                    { 12, new DateTime(2026, 11, 1, 12, 1, 0, 0, DateTimeKind.Utc), "SISTEMA", "Carga inicial", 9, "SOLICITADA", "DEVOLVIDA" },
                    { 13, new DateTime(2026, 11, 1, 12, 0, 0, 0, DateTimeKind.Utc), "comum123", null, 10, null, "SOLICITADA" },
                    { 14, new DateTime(2026, 11, 1, 12, 1, 0, 0, DateTimeKind.Utc), "SISTEMA", "Carga inicial", 10, "SOLICITADA", "EM MANUTENCAO" },
                    { 15, new DateTime(2026, 11, 1, 12, 0, 0, 0, DateTimeKind.Utc), "comum123", null, 11, null, "SOLICITADA" },
                    { 16, new DateTime(2026, 11, 1, 12, 1, 0, 0, DateTimeKind.Utc), "SISTEMA", "Carga inicial", 11, "SOLICITADA", "CONCLUIDA" },
                    { 17, new DateTime(2026, 11, 1, 12, 0, 0, 0, DateTimeKind.Utc), "comum123", null, 12, null, "SOLICITADA" },
                    { 18, new DateTime(2026, 11, 1, 12, 1, 0, 0, DateTimeKind.Utc), "SISTEMA", "Carga inicial", 12, "SOLICITADA", "REENVIADA" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_HISTORICO_STATUS_SolicitacaoId_Data",
                table: "HISTORICO_STATUS",
                columns: new[] { "SolicitacaoId", "Data" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HISTORICO_STATUS");
        }
    }
}
