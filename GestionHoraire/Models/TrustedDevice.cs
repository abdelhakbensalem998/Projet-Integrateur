using System;

namespace GestionHoraire.Models
{
    public class TrustedDevice
    {
        public int Id { get; set; }
        public int UserId { get; set; }

        public string? DeviceName { get; set; }

        public byte[] TokenHash { get; set; } = Array.Empty<byte>();
        public Guid TokenSalt { get; set; }

        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }

        public Utilisateur? User { get; set; }
    }
}