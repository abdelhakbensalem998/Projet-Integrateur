using System;

namespace GestionHoraire.Models
{
    public class SecurityLog
    {
        public int Id { get; set; }
        public int? UserId { get; set; }

        public string Action { get; set; } = "";
        public string? Details { get; set; }
        public string? IpAddress { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}