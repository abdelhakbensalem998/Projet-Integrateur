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
        public virtual Departement? Departement { get; set; }

        public int? UtilisateurId { get; set; } 
        public virtual Utilisateur? Utilisateur { get; set; }

        public string? ProfesseurIds { get; set; } // Multiple professors support (ex: "1,2,5")

        public int? SalleId { get; set; }
        public virtual Salle? Salle { get; set; }

        public int? GroupeId { get; set; }
        public virtual Groupe? Groupe { get; set; }

        // Multi-groupe support via string IDs (ex: "1,2,5")
        public string? GroupeIds { get; set; }

        public string? CodeMinisteriel { get; set; }
        public int HeuresTheorie { get; set; } = 0;
        public int HeuresLabo { get; set; } = 0;
        public int HeuresTravailPersonnel { get; set; } = 0;
    }
}