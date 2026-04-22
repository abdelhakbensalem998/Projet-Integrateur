using System;
using System.ComponentModel.DataAnnotations;

namespace GestionHoraire.Models
{
    public class Demande
    {
        [Key]
        public int Id { get; set; }

        public int UtilisateurId { get; set; }
        public virtual Utilisateur? Utilisateur { get; set; }

        [Required]
        public string Type { get; set; } // "Vacances", "Document", "Matériel", etc.

        [Required]
        public string Description { get; set; }

        public DateTime DateCreation { get; set; } = DateTime.Now;

        public string Statut { get; set; } = "En attente"; // "En attente", "Approuvé", "Refusé"

        public bool EstUrgent { get; set; } = false;

        public string? FichierJoint { get; set; } // Nom du fichier d'origine

        public byte[]? ContenuFichier { get; set; } // Données binaires du fichier dans Neon

        public string? TypeMime { get; set; } // Type de fichier (application/pdf, etc.)

        public string? NoteResponsable { get; set; }
    }
}
