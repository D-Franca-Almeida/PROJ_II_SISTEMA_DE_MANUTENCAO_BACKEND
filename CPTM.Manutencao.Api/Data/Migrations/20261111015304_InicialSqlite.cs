using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CPTM.Manutencao.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InicialSqlite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SOLICITACOES",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Descricao = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    MotivoDevolucao = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    MatriculaSolicitante = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    MatriculaTecnico = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    DataInicioManutencao = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SOLICITACOES", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "SOLICITACOES",
                columns: new[] { "Id", "DataInicioManutencao", "Descricao", "MatriculaSolicitante", "MatriculaTecnico", "MotivoDevolucao", "Status" },
                values: new object[,]
                {
                    { 1, null, "Lâmpada queimada na plataforma 2", "comum123", null, null, "SOLICITADA" },
                    { 2, null, "Escada rolante com ruído", "comum123", null, null, "SOLICITADA" },
                    { 3, null, "Catraca travada", "comum123", null, null, "SOLICITADA" },
                    { 4, null, "Vazamento no banheiro", "outro456", null, null, "SOLICITADA" },
                    { 5, null, "Porta do vagão desalinhada", "comum123", null, null, "CANCELADA" },
                    { 6, null, "Aparelho de ar-condicionado com defeito", "comum123", null, null, "SOLICITADA" },
                    { 7, null, "Grade de proteção solta", "comum123", null, null, "SOLICITADA" },
                    { 8, null, "Ventilador do túnel sem funcionar", "comum123", null, null, "APROVADA" },
                    { 9, null, "Sinalização apagada (devolvida)", "comum123", null, null, "DEVOLVIDA" },
                    { 10, null, "Bebedouro sem pressão", "comum123", null, null, "EM MANUTENCAO" },
                    { 11, null, "Piso solto na estação", "comum123", null, null, "CONCLUIDA" },
                    { 12, null, "Escada rolante com ruído (corrigida)", "comum123", null, null, "REENVIADA" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_SOLICITACOES_MatriculaSolicitante",
                table: "SOLICITACOES",
                column: "MatriculaSolicitante");

            migrationBuilder.CreateIndex(
                name: "IX_SOLICITACOES_Status",
                table: "SOLICITACOES",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SOLICITACOES");
        }
    }
}
