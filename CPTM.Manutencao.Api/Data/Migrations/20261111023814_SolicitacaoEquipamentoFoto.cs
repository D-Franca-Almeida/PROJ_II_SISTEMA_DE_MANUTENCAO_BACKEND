using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPTM.Manutencao.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SolicitacaoEquipamentoFoto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DataAbertura",
                table: "SOLICITACOES",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "EquipamentoId",
                table: "SOLICITACOES",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FotoUrl",
                table: "SOLICITACOES",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DataAbertura", "EquipamentoId", "FotoUrl" },
                values: new object[] { new DateTime(2026, 11, 1, 12, 0, 0, 0, DateTimeKind.Utc), 1, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "DataAbertura", "EquipamentoId", "FotoUrl" },
                values: new object[] { new DateTime(2026, 11, 1, 12, 0, 0, 0, DateTimeKind.Utc), 5, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "DataAbertura", "EquipamentoId", "FotoUrl" },
                values: new object[] { new DateTime(2026, 11, 1, 12, 0, 0, 0, DateTimeKind.Utc), 2, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "DataAbertura", "EquipamentoId", "FotoUrl" },
                values: new object[] { new DateTime(2026, 11, 1, 12, 0, 0, 0, DateTimeKind.Utc), 4, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "DataAbertura", "EquipamentoId", "FotoUrl" },
                values: new object[] { new DateTime(2026, 11, 1, 12, 0, 0, 0, DateTimeKind.Utc), 8, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "DataAbertura", "EquipamentoId", "FotoUrl" },
                values: new object[] { new DateTime(2026, 11, 1, 12, 0, 0, 0, DateTimeKind.Utc), 10, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "DataAbertura", "EquipamentoId", "FotoUrl" },
                values: new object[] { new DateTime(2026, 11, 1, 12, 0, 0, 0, DateTimeKind.Utc), 11, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "DataAbertura", "EquipamentoId", "FotoUrl" },
                values: new object[] { new DateTime(2026, 11, 1, 12, 0, 0, 0, DateTimeKind.Utc), 12, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "DataAbertura", "EquipamentoId", "FotoUrl" },
                values: new object[] { new DateTime(2026, 11, 1, 12, 0, 0, 0, DateTimeKind.Utc), 13, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "DataAbertura", "EquipamentoId", "FotoUrl" },
                values: new object[] { new DateTime(2026, 11, 1, 12, 0, 0, 0, DateTimeKind.Utc), 14, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "DataAbertura", "EquipamentoId", "FotoUrl" },
                values: new object[] { new DateTime(2026, 11, 1, 12, 0, 0, 0, DateTimeKind.Utc), 9, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "DataAbertura", "EquipamentoId", "FotoUrl" },
                values: new object[] { new DateTime(2026, 11, 1, 12, 0, 0, 0, DateTimeKind.Utc), 5, null });

            if (migrationBuilder.IsSqlite())
                migrationBuilder.Sql(
                    "UPDATE SOLICITACOES SET DataAbertura = COALESCE(" +
                    "(SELECT MIN(h.Data) FROM HISTORICO_STATUS h WHERE h.SolicitacaoId = SOLICITACOES.Id), " +
                    "strftime('%Y-%m-%d %H:%M:%S', 'now')) " +
                    "WHERE DataAbertura < '1900-01-01';");

            migrationBuilder.CreateIndex(
                name: "IX_SOLICITACOES_EquipamentoId",
                table: "SOLICITACOES",
                column: "EquipamentoId");

            migrationBuilder.AddForeignKey(
                name: "FK_SOLICITACOES_EQUIPAMENTO_EquipamentoId",
                table: "SOLICITACOES",
                column: "EquipamentoId",
                principalTable: "EQUIPAMENTO",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SOLICITACOES_EQUIPAMENTO_EquipamentoId",
                table: "SOLICITACOES");

            migrationBuilder.DropIndex(
                name: "IX_SOLICITACOES_EquipamentoId",
                table: "SOLICITACOES");

            migrationBuilder.DropColumn(
                name: "DataAbertura",
                table: "SOLICITACOES");

            migrationBuilder.DropColumn(
                name: "EquipamentoId",
                table: "SOLICITACOES");

            migrationBuilder.DropColumn(
                name: "FotoUrl",
                table: "SOLICITACOES");
        }
    }
}
