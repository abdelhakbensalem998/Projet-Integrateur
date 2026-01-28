using System.ComponentModel.DataAnnotations;

namespace GestionHoraire.Models
{
    public class Salle
    {
        public int Id { get; set; }

        [Required]
        public string Nom { get; set; }

        public int Capacite { get; set; }
    }
}
