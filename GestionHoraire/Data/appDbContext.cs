using GestionHoraire.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GestionHoraire.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // ===== Tables principales =====
        public DbSet<Utilisateur> Utilisateurs { get; set; }
        public DbSet<Cours> Cours { get; set; }
        public DbSet<Salle> Salles { get; set; }
        public DbSet<Departement> Departements { get; set; }
        public DbSet<Disponibilite> Disponibilites { get; set; }
        public DbSet<Groupe> Groupes { get; set; }
        public DbSet<Demande> Demandes { get; set; }

        // ===== Sécurité / OTP / Logs =====
        public DbSet<EmailOtp> EmailOtps { get; set; }
        public DbSet<SecurityLog> SecurityLogs { get; set; }
        public DbSet<TrustedDevice> TrustedDevices { get; set; }
        public DbSet<BackupCode> BackupCodes { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Disponibilite>()
                .Property(d => d.Jour)
                .HasConversion<int>();

            // PostgreSQL gère nativement le type Guid comme 'uuid'
            // Les configurations spécifiques à SQL Server (uniqueidentifier) ne sont pas nécessaires
            
            modelBuilder.Entity<Utilisateur>()
                .Property(u => u.TwoFactorProvider)
                .HasMaxLength(32);

            modelBuilder.Entity<Utilisateur>()
                .Property(u => u.AuthenticatorSecretKey)
                .HasMaxLength(128);

            modelBuilder.Entity<BackupCode>()
                .HasOne(b => b.User)
                .WithMany(u => u.BackupCodes)
                .HasForeignKey(b => b.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
