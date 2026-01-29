using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionHoraire.Migrations
{
    /// <inheritdoc />
    public partial class SynManualChanges : Migration
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
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<DateTime>(
                name: "DateCreation",
                table: "Utilisateurs",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "Disponibilite",
                table: "Utilisateurs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "MotDePasseHash",
                table: "Utilisateurs",
                type: "varbinary(max)",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<Guid>(
                name: "MotDePasseSalt",
                table: "Utilisateurs",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<string>(
                name: "Jour",
                table: "Disponibilites",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<TimeSpan>(
                name: "HeureDebut",
                table: "Cours",
                type: "time",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.AddColumn<TimeSpan>(
                name: "HeureFin",
                table: "Cours",
                type: "time",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.AddColumn<int>(
                name: "Jour",
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
                name: "FK_Cours_Departements_DepartementId",
                table: "Cours");

            migrationBuilder.DropForeignKey(
                name: "FK_Utilisateurs_Departements_DepartementId",
                table: "Utilisateurs");

            migrationBuilder.DropIndex(
                name: "IX_Cours_DepartementId",
                table: "Cours");

            migrationBuilder.DropColumn(
                name: "DateCreation",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "Disponibilite",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "MotDePasseHash",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "MotDePasseSalt",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "HeureDebut",
                table: "Cours");

            migrationBuilder.DropColumn(
                name: "HeureFin",
                table: "Cours");

            migrationBuilder.DropColumn(
                name: "Jour",
                table: "Cours");

            migrationBuilder.AlterColumn<int>(
                name: "DepartementId",
                table: "Utilisateurs",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Jour",
                table: "Disponibilites",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddForeignKey(
                name: "FK_Utilisateurs_Departements_DepartementId",
                table: "Utilisateurs",
                column: "DepartementId",
                principalTable: "Departements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
