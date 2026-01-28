using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionHoraire.Migrations
{
    /// <inheritdoc />
    public partial class AjoutRoleDepartement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DepartementId",
                table: "Utilisateurs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Utilisateurs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Departements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nom = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departements", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Utilisateurs_DepartementId",
                table: "Utilisateurs",
                column: "DepartementId");

            migrationBuilder.AddForeignKey(
                name: "FK_Utilisateurs_Departements_DepartementId",
                table: "Utilisateurs",
                column: "DepartementId",
                principalTable: "Departements",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Utilisateurs_Departements_DepartementId",
                table: "Utilisateurs");

            migrationBuilder.DropTable(
                name: "Departements");

            migrationBuilder.DropIndex(
                name: "IX_Utilisateurs_DepartementId",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "DepartementId",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "Utilisateurs");
        }
    }
}
