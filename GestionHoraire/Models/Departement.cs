using System.ComponentModel.DataAnnotations;

namespace GestionHoraire.Models
{
    public class Departement
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [Display(Name = "Nom du département")]
        public string Nom { get; set; }

        // Relation inverse
        public ICollection<Utilisateur> Utilisateurs { get; set; } = new List<Utilisateur>();
    }
}

