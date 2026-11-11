using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPTM.Manutencao.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RecebimentoSsm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DataRecebimento",
                table: "SOLICITACOES",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NumeroSsm",
                table: "SOLICITACOES",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DataRecebimento", "NumeroSsm" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "DataRecebimento", "NumeroSsm" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "DataRecebimento", "NumeroSsm" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "DataRecebimento", "NumeroSsm" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "DataRecebimento", "NumeroSsm" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "DataRecebimento", "NumeroSsm" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "DataRecebimento", "NumeroSsm" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "DataRecebimento", "NumeroSsm" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "DataRecebimento", "NumeroSsm" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "DataRecebimento", "NumeroSsm" },
                values: new object[] { new DateTime(2026, 11, 1, 12, 1, 0, 0, DateTimeKind.Utc), "SSM-2026-000001" });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "DataRecebimento", "NumeroSsm" },
                values: new object[] { new DateTime(2026, 11, 1, 12, 1, 0, 0, DateTimeKind.Utc), "SSM-2026-000002" });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "DataRecebimento", "NumeroSsm" },
                values: new object[] { null, null });

            migrationBuilder.CreateIndex(
                name: "IX_SOLICITACOES_NumeroSsm",
                table: "SOLICITACOES",
                column: "NumeroSsm",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SOLICITACOES_NumeroSsm",
                table: "SOLICITACOES");

            migrationBuilder.DropColumn(
                name: "DataRecebimento",
                table: "SOLICITACOES");

            migrationBuilder.DropColumn(
                name: "NumeroSsm",
                table: "SOLICITACOES");
        }
    }
}
