using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionHoraire.Models
{
    public class Disponibilite
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("Utilisateur")]
        public int UtilisateurId { get; set; }
        public Utilisateur Utilisateur { get; set; }

        [Required]
        public DayOfWeek Jour { get; set; } // Lundi, Mardi, etc.

        [Required]
        public TimeSpan HeureDebut { get; set; }

        [Required]
        public TimeSpan HeureFin { get; set; }

        public bool Disponible { get; set; } = true;
    }
}
