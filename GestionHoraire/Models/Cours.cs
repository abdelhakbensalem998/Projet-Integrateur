using System;
using System.ComponentModel.DataAnnotations;

namespace GestionHoraire.Models
{
    public class Cours
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Titre { get; set; }

        [Required]
        public DayOfWeek Jour { get; set; } // Lundi, Mardi, etc.

        [Required]
        public TimeSpan HeureDebut { get; set; }

        [Required]
        public TimeSpan HeureFin { get; set; }

        // ✅ AJOUT : correspond à dbo.Cours.Type (NVARCHAR)
        [MaxLength(50)]
        public string? Type { get; set; }

        public int DepartementId { get; set; }
        public Departement Departement { get; set; }

        public int? UtilisateurId { get; set; }
        public Utilisateur Utilisateur { get; set; }

        public int? SalleId { get; set; }
        public Salle Salle { get; set; }

        public int? GroupeId { get; set; }
        public Groupe Groupe { get; set; }
    }
}