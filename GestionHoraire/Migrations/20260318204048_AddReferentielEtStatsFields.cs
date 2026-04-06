using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionHoraire.Migrations
{
    /// <inheritdoc />
    public partial class AddReferentielEtStatsFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /*
            migrationBuilder.DropForeignKey(
                name: "FK_Cours_Groupes_GroupeId",
                table: "Cours");

            migrationBuilder.DropIndex(
                name: "IX_Cours_GroupeId",
                table: "Cours");

            migrationBuilder.DropColumn(
                name: "GroupeId",
                table: "Cours");
            */

            migrationBuilder.AddColumn<string>(
                name: "Logiciels",
                table: "Salles",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Materiel",
                table: "Salles",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodeMinisteriel",
                table: "Cours",
                type: "nvarchar(max)",
                nullable: true);

            /*
            migrationBuilder.AddColumn<string>(
                name: "GroupeIds",
                table: "Cours",
                type: "nvarchar(max)",
                nullable: true);
            */

            migrationBuilder.AddColumn<int>(
                name: "HeuresLabo",
                table: "Cours",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HeuresTheorie",
                table: "Cours",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HeuresTravailPersonnel",
                table: "Cours",
                type: "int",
                nullable: false,
                defaultValue: 0);

            /*
            migrationBuilder.AddColumn<string>(
                name: "ProfesseurIds",
                table: "Cours",
                type: "nvarchar(max)",
                nullable: true);
            */
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Logiciels",
                table: "Salles");

            migrationBuilder.DropColumn(
                name: "Materiel",
                table: "Salles");

            migrationBuilder.DropColumn(
                name: "CodeMinisteriel",
                table: "Cours");

            migrationBuilder.DropColumn(
                name: "GroupeIds",
                table: "Cours");

            migrationBuilder.DropColumn(
                name: "HeuresLabo",
                table: "Cours");

            migrationBuilder.DropColumn(
                name: "HeuresTheorie",
                table: "Cours");

            migrationBuilder.DropColumn(
                name: "HeuresTravailPersonnel",
                table: "Cours");

            migrationBuilder.DropColumn(
                name: "ProfesseurIds",
                table: "Cours");

            migrationBuilder.AddColumn<int>(
                name: "GroupeId",
                table: "Cours",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cours_GroupeId",
                table: "Cours",
                column: "GroupeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Cours_Groupes_GroupeId",
                table: "Cours",
                column: "GroupeId",
                principalTable: "Groupes",
                principalColumn: "Id");
        }
    }
}
