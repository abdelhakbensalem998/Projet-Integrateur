using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionHoraire.Migrations
{
    /// <inheritdoc />
    public partial class SyncModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Utilisateurs_Departements_DepartementId",
                table: "Utilisateurs");

            migrationBuilder.AlterColumn<int>(
                name: "DepartementId",
                table: "Utilisateurs",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Utilisateurs_Departements_DepartementId",
                table: "Utilisateurs",
                column: "DepartementId",
                principalTable: "Departements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Utilisateurs_Departements_DepartementId",
                table: "Utilisateurs");

            migrationBuilder.AlterColumn<int>(
                name: "DepartementId",
                table: "Utilisateurs",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddForeignKey(
                name: "FK_Utilisateurs_Departements_DepartementId",
                table: "Utilisateurs",
                column: "DepartementId",
                principalTable: "Departements",
                principalColumn: "Id");
        }
    }
}
