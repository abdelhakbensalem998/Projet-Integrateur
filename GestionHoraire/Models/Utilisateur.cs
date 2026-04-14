using System;
using System.Collections.Generic;

namespace GestionHoraire.Models
{
    public class Utilisateur
    {
        // ===== Identité =====
        public int Id { get; set; }
        public string? Nom { get; set; }
        public string? Email { get; set; }
        public string? Role { get; set; } // "Administrateur", "ResponsableDépartement", "Professeur"

        // ===== Sécurité Mot de Passe =====
        public Guid MotDePasseSalt { get; set; }
        public byte[] MotDePasseHash { get; set; } = Array.Empty<byte>();
        public bool EstMotDePasseProvisoire { get; set; }

        // ===== Question de sécurité =====
        public string? QuestionSecurite { get; set; }
        public Guid? ReponseSecuriteSalt { get; set; }
        public byte[]? ReponseSecuriteHash { get; set; }

        // ===== Lockout (anti brute-force) =====
        public int FailedLoginAttempts { get; set; }
        public DateTime? LockoutUntil { get; set; }

        // ===== 2FA =====
        public bool TwoFactorEnabled { get; set; }
        public string? TwoFactorProvider { get; set; }
        public string? AuthenticatorSecretKey { get; set; }
        public DateTime? AuthenticatorEnabledAt { get; set; }

        // ===== État et Relations =====
        public bool Disponibilite { get; set; }
        public int? DepartementId { get; set; }
        public virtual Departement? Departement { get; set; }

        public virtual ICollection<BackupCode> BackupCodes { get; set; } = new List<BackupCode>();
        public virtual ICollection<Cours> Cours { get; set; } = new List<Cours>();
        public virtual ICollection<Disponibilite> Disponibilites { get; set; } = new List<Disponibilite>();

        public DateTime DateCreation { get; set; } = DateTime.Now;
    }
}
