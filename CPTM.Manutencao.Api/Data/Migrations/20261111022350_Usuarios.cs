using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CPTM.Manutencao.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Usuarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "USUARIO",
                columns: table => new
                {
                    Matricula = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SenhaHash = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Perfil = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USUARIO", x => x.Matricula);
                });

            migrationBuilder.InsertData(
                table: "USUARIO",
                columns: new[] { "Matricula", "Nome", "Perfil", "SenhaHash" },
                values: new object[,]
                {
                    { "admin123", "Administrador CPTM", "Administrativo", "AQAAAAIAAYagAAAAEPG97joPfZDaH+3JpNWPl6yhYANv0qBd26FjONrdxmHnee+lA28Pnrmd00X1NSQpmQ==" },
                    { "comum123", "Usuário Comum", "Comum", "AQAAAAIAAYagAAAAEPG97joPfZDaH+3JpNWPl6yhYANv0qBd26FjONrdxmHnee+lA28Pnrmd00X1NSQpmQ==" },
                    { "tec123", "Técnico CPTM", "Tecnico", "AQAAAAIAAYagAAAAEPG97joPfZDaH+3JpNWPl6yhYANv0qBd26FjONrdxmHnee+lA28Pnrmd00X1NSQpmQ==" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "USUARIO");
        }
    }
}
