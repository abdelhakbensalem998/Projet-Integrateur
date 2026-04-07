using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionHoraire.Models
{
    public class Salle
    {
        public int Id { get; set; }

        [Required]
        public string Nom { get; set; }

        public int Capacite { get; set; }

        public string Type { get; set; }

        // The current database used by the app does not expose these columns yet.
        [NotMapped]
        public string? Logiciels { get; set; }

        [NotMapped]
        public string? Materiel { get; set; }
    }
}
