using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CPTM.Manutencao.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CatalogoEstacaoEquipamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ESTACAO",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Linha = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ESTACAO", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EQUIPAMENTO",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EstacaoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Descricao = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Patrimonio = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EQUIPAMENTO", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EQUIPAMENTO_ESTACAO_EstacaoId",
                        column: x => x.EstacaoId,
                        principalTable: "ESTACAO",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ESTACAO",
                columns: new[] { "Id", "Linha", "Nome" },
                values: new object[,]
                {
                    { 1, "7-Rubi", "Luz" },
                    { 2, "10-Turquesa", "Brás" },
                    { 3, "11-Coral", "Corinthians-Itaquera" },
                    { 4, "12-Safira", "Calmon Viana" },
                    { 5, "13-Jade", "Aeroporto-Guarulhos" }
                });

            migrationBuilder.InsertData(
                table: "EQUIPAMENTO",
                columns: new[] { "Id", "Descricao", "EstacaoId", "Patrimonio" },
                values: new object[,]
                {
                    { 1, "Escada rolante da plataforma 2", 1, "PAT-LUZ-0001" },
                    { 2, "Catraca de acesso principal", 1, "PAT-LUZ-0002" },
                    { 3, "Terminal de bilheteria 01", 1, "PAT-LUZ-0003" },
                    { 4, "Catraca de acesso norte", 2, "PAT-BRS-0001" },
                    { 5, "Escada rolante da plataforma 1", 2, "PAT-BRS-0002" },
                    { 6, "Rádio de comunicação do controle da estação", 2, "PAT-BRS-0003" },
                    { 7, "Terminal de bilheteria 01", 3, "PAT-ITQ-0001" },
                    { 8, "Catraca de acesso sul", 3, "PAT-ITQ-0002" },
                    { 9, "Escada rolante da passarela", 3, "PAT-ITQ-0003" },
                    { 10, "Terminal de bilheteria 01", 4, "PAT-CVN-0001" },
                    { 11, "Catraca de acesso principal", 4, "PAT-CVN-0002" },
                    { 12, "Rádio de comunicação da plataforma", 4, "PAT-CVN-0003" },
                    { 13, "Terminal de bilheteria 01", 5, "PAT-AGU-0001" },
                    { 14, "Escada rolante do saguão", 5, "PAT-AGU-0002" },
                    { 15, "Catraca de acesso ao terminal", 5, "PAT-AGU-0003" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_EQUIPAMENTO_EstacaoId",
                table: "EQUIPAMENTO",
                column: "EstacaoId");

            migrationBuilder.CreateIndex(
                name: "IX_EQUIPAMENTO_Patrimonio",
                table: "EQUIPAMENTO",
                column: "Patrimonio",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EQUIPAMENTO");

            migrationBuilder.DropTable(
                name: "ESTACAO");
        }
    }
}
