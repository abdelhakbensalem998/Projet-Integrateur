using System;
using System.Collections.Generic;

namespace GestionHoraire.Models
{
    public class Utilisateur
    {
        public int Id { get; set; }

        public string? Nom { get; set; }
        public string? Email { get; set; }
        public string? Role { get; set; } // "Admin", "Responsable", "Professeur"

        // --- Sécurité Mot de Passe ---
        public Guid MotDePasseSalt { get; set; }
        public byte[] MotDePasseHash { get; set; } = Array.Empty<byte>();
        public bool EstMotDePasseProvisoire { get; set; }

        // --- Récupération de compte ---
        public string? QuestionSecurite { get; set; }
        public Guid? ReponseSecuriteSalt { get; set; }
        public byte[]? ReponseSecuriteHash { get; set; }

        // --- État et Relations ---
        public bool Disponibilite { get; set; } // Statut global (ex: actif/inactif)
        public int? DepartementId { get; set; }
        public virtual Departement? Departement { get; set; }

        // Collections pour les relations One-to-Many
        public virtual ICollection<Cours> Cours { get; set; } = new List<Cours>();
        public virtual ICollection<Disponibilite> Disponibilites { get; set; } = new List<Disponibilite>();
        public DateTime DateCreation { get; set; } = DateTime.Now; // Initialise par défaut à maintenant
    }
}