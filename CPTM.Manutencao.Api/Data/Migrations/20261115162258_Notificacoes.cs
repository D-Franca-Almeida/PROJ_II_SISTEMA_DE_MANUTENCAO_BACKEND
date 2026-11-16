using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPTM.Manutencao.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Notificacoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NOTIFICACAO",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MatriculaDestino = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    PerfilDestino = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    SolicitacaoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Mensagem = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Lida = table.Column<bool>(type: "INTEGER", nullable: false),
                    Data = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DataLeitura = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NOTIFICACAO", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NOTIFICACAO_SOLICITACOES_SolicitacaoId",
                        column: x => x.SolicitacaoId,
                        principalTable: "SOLICITACOES",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICACAO_MatriculaDestino_Lida",
                table: "NOTIFICACAO",
                columns: new[] { "MatriculaDestino", "Lida" });

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICACAO_PerfilDestino_Lida",
                table: "NOTIFICACAO",
                columns: new[] { "PerfilDestino", "Lida" });

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICACAO_SolicitacaoId",
                table: "NOTIFICACAO",
                column: "SolicitacaoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NOTIFICACAO");
        }
    }
}
