using GestionHoraire.Models;
using Microsoft.EntityFrameworkCore;

namespace GestionHoraire.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options) { }

        public DbSet<Utilisateur> Utilisateurs { get; set; }
        public DbSet<Cours> Cours { get; set; }
        public DbSet<Salle> Salles { get; set; }
        public DbSet<Departement> Departements { get; set; }
        public DbSet<Disponibilite> Disponibilites { get; set; }

        public DbSet<Classe> Classes { get; set; }
        public DbSet<CoursClasse> CoursClasses { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<CoursClasse>()
                .HasKey(cc => new { cc.CoursId, cc.ClasseId });

            modelBuilder.Entity<CoursClasse>()
                .HasOne(cc => cc.Cours)
                .WithMany()
                .HasForeignKey(cc => cc.CoursId);

            modelBuilder.Entity<CoursClasse>()
                .HasOne(cc => cc.Classe)
                .WithMany(c => c.CoursClasses)
                .HasForeignKey(cc => cc.ClasseId);
        }
    }
}
