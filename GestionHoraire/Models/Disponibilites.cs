using System;
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
        public virtual Utilisateur? Utilisateur { get; set; }

        [Required]
        // Utilisation de Column(TypeName = "nvarchar") si SQL stocke du texte
        // Ou laissez tel quel si SQL stocke des entiers (0, 1, 2...)
        public DayOfWeek Jour { get; set; }

        [Required]
        public TimeSpan HeureDebut { get; set; }

        [Required]
        public TimeSpan HeureFin { get; set; }

        public bool Disponible { get; set; } = true;
    }
}
