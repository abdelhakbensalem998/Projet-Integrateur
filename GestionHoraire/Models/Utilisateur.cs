using System;
using System.Collections.Generic;

namespace GestionHoraire.Models
{
    public class Utilisateur
    {
        public int Id { get; set; }

        public string? Nom { get; set; }
        public string? Email { get; set; }
        public string? Role { get; set; }

        // Mot de passe sécurisé (SHA256 + Salt)
        public Guid MotDePasseSalt { get; set; }
        public byte[] MotDePasseHash { get; set; } = Array.Empty<byte>();

        // Liens
        public int? DepartementId { get; set; }
        public Departement? Departement { get; set; }

        // (Si tu as des relations, garde-les. Sinon tu peux supprimer)
        public ICollection<Cours> Cours { get; set; } = new List<Cours>();
        public ICollection<Disponibilite> Disponibilites { get; set; } = new List<Disponibilite>();

        // ✅ Nouveaux champs (SQL ajouté)
        public string? QuestionSecurite { get; set; }
        public Guid? ReponseSecuriteSalt { get; set; }
        public byte[]? ReponseSecuriteHash { get; set; }
        public bool EstMotDePasseProvisoire { get; set; }
    }
}
