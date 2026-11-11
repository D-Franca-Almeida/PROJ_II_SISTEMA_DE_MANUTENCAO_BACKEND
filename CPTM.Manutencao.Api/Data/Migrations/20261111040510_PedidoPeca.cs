using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPTM.Manutencao.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PedidoPeca : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PEDIDO_PECA",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrdemServicoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Item = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Quantidade = table.Column<int>(type: "INTEGER", nullable: false),
                    Justificativa = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    MatriculaSolicitante = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    DataPedido = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ObservacaoAlmoxarifado = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    MatriculaEncaminhamento = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    DataEncaminhamento = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MatriculaRecebimento = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    DataRecebimento = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MatriculaCancelamento = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    DataCancelamento = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MotivoCancelamento = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PEDIDO_PECA", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PEDIDO_PECA_ORDEM_SERVICO_OrdemServicoId",
                        column: x => x.OrdemServicoId,
                        principalTable: "ORDEM_SERVICO",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PEDIDO_PECA_OrdemServicoId",
                table: "PEDIDO_PECA",
                column: "OrdemServicoId");

            migrationBuilder.CreateIndex(
                name: "IX_PEDIDO_PECA_Status",
                table: "PEDIDO_PECA",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PEDIDO_PECA");
        }
    }
}
