using Microsoft.EntityFrameworkCore;
using GestionHoraire.Models;

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
    }
}
