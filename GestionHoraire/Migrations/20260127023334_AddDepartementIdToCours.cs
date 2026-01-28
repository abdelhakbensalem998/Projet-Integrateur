using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionHoraire.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartementIdToCours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DepartementId",
                table: "Cours",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Cours_DepartementId",
                table: "Cours",
                column: "DepartementId");

            migrationBuilder.AddForeignKey(
                name: "FK_Cours_Departements_DepartementId",
                table: "Cours",
                column: "DepartementId",
                principalTable: "Departements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Cours_Departements_DepartementId",
                table: "Cours");

            migrationBuilder.DropIndex(
                name: "IX_Cours_DepartementId",
                table: "Cours");

            migrationBuilder.DropColumn(
                name: "DepartementId",
                table: "Cours");
        }
    }
}
