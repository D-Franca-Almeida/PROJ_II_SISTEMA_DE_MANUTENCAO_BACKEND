using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPTM.Manutencao.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FechamentoERetirada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DataRetirada",
                table: "SOLICITACOES",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdentificacaoRetirada",
                table: "SOLICITACOES",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ObservacaoDestino",
                table: "SOLICITACOES",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DataRetirada", "IdentificacaoRetirada", "ObservacaoDestino" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "DataRetirada", "IdentificacaoRetirada", "ObservacaoDestino" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "DataRetirada", "IdentificacaoRetirada", "ObservacaoDestino" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "DataRetirada", "IdentificacaoRetirada", "ObservacaoDestino" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "DataRetirada", "IdentificacaoRetirada", "ObservacaoDestino" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "DataRetirada", "IdentificacaoRetirada", "ObservacaoDestino" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "DataRetirada", "IdentificacaoRetirada", "ObservacaoDestino" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "DataRetirada", "IdentificacaoRetirada", "ObservacaoDestino" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "DataRetirada", "IdentificacaoRetirada", "ObservacaoDestino" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "DataRetirada", "IdentificacaoRetirada", "ObservacaoDestino" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "DataRetirada", "IdentificacaoRetirada", "ObservacaoDestino" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "DataRetirada", "IdentificacaoRetirada", "ObservacaoDestino" },
                values: new object[] { null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DataRetirada",
                table: "SOLICITACOES");

            migrationBuilder.DropColumn(
                name: "IdentificacaoRetirada",
                table: "SOLICITACOES");

            migrationBuilder.DropColumn(
                name: "ObservacaoDestino",
                table: "SOLICITACOES");
        }
    }
}
