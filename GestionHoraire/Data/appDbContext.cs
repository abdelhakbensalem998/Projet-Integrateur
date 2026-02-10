using Microsoft.EntityFrameworkCore;
using GestionHoraire.Models;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GestionHoraire.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<Utilisateur> Utilisateurs { get; set; }
        public DbSet<Cours> Cours { get; set; }
        public DbSet<Salle> Salles { get; set; }
        public DbSet<Departement> Departements { get; set; }
        public DbSet<Disponibilite> Disponibilites { get; set; }
        public DbSet<Groupe> Groupes { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // On force la conversion vers INT pour matcher ta DB
            modelBuilder.Entity<Disponibilite>()
                .Property(d => d.Jour)
                .HasConversion<int>();

            // Configuration pour le mot de passe si nécessaire
            modelBuilder.Entity<Utilisateur>()
                .Property(u => u.MotDePasseSalt)
                .HasColumnType("uniqueidentifier");
        }

    }
}