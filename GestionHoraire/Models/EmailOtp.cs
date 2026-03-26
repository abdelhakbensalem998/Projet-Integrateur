using System;

namespace GestionHoraire.Models
{
    public class EmailOtp
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Purpose { get; set; } = "RESET"; // RESET / 2FA

        public byte[] CodeHash { get; set; } = Array.Empty<byte>();
        public Guid CodeSalt { get; set; }

        public DateTime ExpiresAt { get; set; }
        public DateTime? UsedAt { get; set; }

        public int Attempts { get; set; }
        public DateTime LastSentAt { get; set; }
        public DateTime CreatedAt { get; set; }

        public Utilisateur? User { get; set; }
    }
}