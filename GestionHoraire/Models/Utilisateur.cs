using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionHoraire.Models
{
    public class Utilisateur
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Nom { get; set; }

        [Required]
        public string Email { get; set; }

        [Required]
        public string Role { get; set; } // "Professeur" ou "Responsable"

        // Dépt lié
        public int DepartementId { get; set; }
        public Departement Departement { get; set; }

        // Disponibilité pour planning
        public ICollection<Disponibilite> Disponibilites { get; set; } // lien vers disponibilités
    }
}

