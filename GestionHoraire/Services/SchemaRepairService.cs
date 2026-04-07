using GestionHoraire.Data;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace GestionHoraire.Services
{
    public class SchemaRepairService
    {
        private readonly AppDbContext _context;

        public SchemaRepairService(AppDbContext context)
        {
            _context = context;
        }

        private Task EnsureDemandesColumnAsync(string columnName, string sqlDefinition)
        {
            var quotedColumnName = columnName.Replace("'", "''");
            var bracketedColumnName = columnName.Replace("]", "]]");
            var escapedDefinition = sqlDefinition.Replace("'", "''");

            return _context.Database.ExecuteSqlRawAsync($@"
                IF COL_LENGTH('dbo.Demandes', '{quotedColumnName}') IS NULL
                    EXEC('ALTER TABLE dbo.Demandes ADD [{bracketedColumnName}] {escapedDefinition}');
            ");
        }

        private Task EnsureCoursColumnAsync(string columnName, string sqlDefinition)
        {
            var quotedColumnName = columnName.Replace("'", "''");
            var bracketedColumnName = columnName.Replace("]", "]]");
            var escapedDefinition = sqlDefinition.Replace("'", "''");

            return _context.Database.ExecuteSqlRawAsync($@"
                IF COL_LENGTH('dbo.Cours', '{quotedColumnName}') IS NULL
                    EXEC('ALTER TABLE dbo.Cours ADD [{bracketedColumnName}] {escapedDefinition}');
            ");
        }

        public async Task EnsureCoursSchemaAsync()
        {
            await EnsureCoursColumnAsync("ProfesseurIds", "NVARCHAR(MAX) NULL");
            await EnsureCoursColumnAsync("GroupeIds", "NVARCHAR(MAX) NULL");
            await EnsureCoursColumnAsync("CodeMinisteriel", "NVARCHAR(MAX) NULL");
            await EnsureCoursColumnAsync("HeuresTheorie", "INT NOT NULL DEFAULT (0)");
            await EnsureCoursColumnAsync("HeuresLabo", "INT NOT NULL DEFAULT (0)");
            await EnsureCoursColumnAsync("HeuresTravailPersonnel", "INT NOT NULL DEFAULT (0)");

            await _context.Database.ExecuteSqlRawAsync(@"
                UPDATE dbo.Cours
                SET ProfesseurIds = CAST(UtilisateurId AS NVARCHAR(20))
                WHERE UtilisateurId IS NOT NULL
                  AND (ProfesseurIds IS NULL OR LTRIM(RTRIM(ProfesseurIds)) = '');
            ");

            await _context.Database.ExecuteSqlRawAsync(@"
                UPDATE dbo.Cours
                SET GroupeIds = CAST(GroupeId AS NVARCHAR(20))
                WHERE GroupeId IS NOT NULL
                  AND (GroupeIds IS NULL OR LTRIM(RTRIM(GroupeIds)) = '');
            ");
        }

        public async Task EnsureDemandesSchemaAsync()
        {
            await _context.Database.ExecuteSqlRawAsync(@"
                IF OBJECT_ID(N'dbo.Demandes', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[Demandes]
                    (
                        [Id] INT IDENTITY(1,1) NOT NULL,
                        [UtilisateurId] INT NOT NULL,
                        [Type] NVARCHAR(MAX) NOT NULL,
                        [Description] NVARCHAR(MAX) NOT NULL,
                        [DateCreation] DATETIME2 NOT NULL,
                        [Statut] NVARCHAR(MAX) NOT NULL,
                        [EstUrgent] BIT NOT NULL CONSTRAINT [DF_Demandes_EstUrgent] DEFAULT ((0)),
                        [FichierJoint] NVARCHAR(MAX) NULL,
                        [NoteResponsable] NVARCHAR(MAX) NULL,
                        CONSTRAINT [PK_Demandes] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_Demandes_Utilisateurs_UtilisateurId]
                            FOREIGN KEY ([UtilisateurId]) REFERENCES [dbo].[Utilisateurs] ([Id]) ON DELETE CASCADE
                    );

                    CREATE INDEX [IX_Demandes_UtilisateurId] ON [dbo].[Demandes] ([UtilisateurId]);
                END
            ");

            await EnsureDemandesColumnAsync("UtilisateurId", "INT NOT NULL DEFAULT ((0))");
            await EnsureDemandesColumnAsync("Type", "NVARCHAR(MAX) NOT NULL DEFAULT ('')");
            await EnsureDemandesColumnAsync("Description", "NVARCHAR(MAX) NOT NULL DEFAULT ('')");
            await EnsureDemandesColumnAsync("DateCreation", "DATETIME2 NOT NULL DEFAULT (SYSUTCDATETIME())");
            await EnsureDemandesColumnAsync("Statut", "NVARCHAR(MAX) NOT NULL DEFAULT ('En attente')");
            await EnsureDemandesColumnAsync("EstUrgent", "BIT NOT NULL DEFAULT ((0))");
            await EnsureDemandesColumnAsync("FichierJoint", "NVARCHAR(MAX) NULL");
            await EnsureDemandesColumnAsync("NoteResponsable", "NVARCHAR(MAX) NULL");

            await _context.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = 'IX_Demandes_UtilisateurId'
                      AND object_id = OBJECT_ID(N'dbo.Demandes')
                )
                BEGIN
                    CREATE INDEX [IX_Demandes_UtilisateurId] ON [dbo].[Demandes] ([UtilisateurId]);
                END
            ");
        }
    }
}
