using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPTM.Manutencao.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AutorizacaoRetirada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AutorizadoDocumento",
                table: "SOLICITACOES",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AutorizadoNome",
                table: "SOLICITACOES",
                type: "TEXT",
                maxLength: 150,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "AutorizadoDocumento", "AutorizadoNome" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "AutorizadoDocumento", "AutorizadoNome" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "AutorizadoDocumento", "AutorizadoNome" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "AutorizadoDocumento", "AutorizadoNome" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "AutorizadoDocumento", "AutorizadoNome" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "AutorizadoDocumento", "AutorizadoNome" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "AutorizadoDocumento", "AutorizadoNome" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "AutorizadoDocumento", "AutorizadoNome" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "AutorizadoDocumento", "AutorizadoNome" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "AutorizadoDocumento", "AutorizadoNome" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "AutorizadoDocumento", "AutorizadoNome" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SOLICITACOES",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "AutorizadoDocumento", "AutorizadoNome" },
                values: new object[] { null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutorizadoDocumento",
                table: "SOLICITACOES");

            migrationBuilder.DropColumn(
                name: "AutorizadoNome",
                table: "SOLICITACOES");
        }
    }
}
