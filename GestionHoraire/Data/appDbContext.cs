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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Conversion robuste : garantit que l'Enum DayOfWeek est stocké en NVARCHAR en base
            modelBuilder.Entity<Disponibilite>()
                .Property(d => d.Jour)
                .HasConversion(new EnumToStringConverter<System.DayOfWeek>());

            base.OnModelCreating(modelBuilder);
        }
    }
}