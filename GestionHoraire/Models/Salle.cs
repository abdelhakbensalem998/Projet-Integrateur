using System.ComponentModel.DataAnnotations;

namespace GestionHoraire.Models
{
    public class Salle
    {
        public int Id { get; set; }

        [Required]
        public string Nom { get; set; }

        public int Capacite { get; set; }

        public string Type { get; set; }

        public string? Logiciels { get; set; }
        public string? Materiel { get; set; }
    }
}
