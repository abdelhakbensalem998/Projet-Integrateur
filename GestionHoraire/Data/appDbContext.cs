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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Disponibilite>()
                .Property(d => d.Jour)
                .HasConversion<int>();

            modelBuilder.Entity<Utilisateur>()
                .Property(u => u.MotDePasseSalt)
                .HasColumnType("uniqueidentifier");
        }
    }
}