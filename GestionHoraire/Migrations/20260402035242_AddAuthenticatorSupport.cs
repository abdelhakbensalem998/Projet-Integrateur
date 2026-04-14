using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionHoraire.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthenticatorSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AuthenticatorEnabledAt",
                table: "Utilisateurs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthenticatorSecretKey",
                table: "Utilisateurs",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailedLoginAttempts",
                table: "Utilisateurs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockoutUntil",
                table: "Utilisateurs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TwoFactorEnabled",
                table: "Utilisateurs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TwoFactorProvider",
                table: "Utilisateurs",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Cours",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BackupCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CodeHash = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CodeSalt = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackupCodes_Utilisateurs_UserId",
                        column: x => x.UserId,
                        principalTable: "Utilisateurs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmailOtps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CodeHash = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CodeSalt = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastSentAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailOtps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailOtps_Utilisateurs_UserId",
                        column: x => x.UserId,
                        principalTable: "Utilisateurs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SecurityLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrustedDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    DeviceName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TokenHash = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    TokenSalt = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustedDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrustedDevices_Utilisateurs_UserId",
                        column: x => x.UserId,
                        principalTable: "Utilisateurs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackupCodes_UserId",
                table: "BackupCodes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailOtps_UserId",
                table: "EmailOtps",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustedDevices_UserId",
                table: "TrustedDevices",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupCodes");

            migrationBuilder.DropTable(
                name: "EmailOtps");

            migrationBuilder.DropTable(
                name: "SecurityLogs");

            migrationBuilder.DropTable(
                name: "TrustedDevices");

            migrationBuilder.DropColumn(
                name: "AuthenticatorEnabledAt",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "AuthenticatorSecretKey",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "FailedLoginAttempts",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "LockoutUntil",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "TwoFactorEnabled",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "TwoFactorProvider",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Cours");
        }
    }
}
