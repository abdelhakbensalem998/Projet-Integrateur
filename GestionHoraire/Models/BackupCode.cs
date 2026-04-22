using System;

namespace GestionHoraire.Models
{
    public class BackupCode
    {
        public int Id { get; set; }
        public int UserId { get; set; }

        public byte[] CodeHash { get; set; } = Array.Empty<byte>();
        public Guid CodeSalt { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UsedAt { get; set; }
        public DateTime? RevokedAt { get; set; }

        public Utilisateur? User { get; set; }
    }
}
