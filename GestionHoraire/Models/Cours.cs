using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionHoraire.Models
{
    public class Cours
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Titre { get; set; }

        // Salle
        public int? SalleId { get; set; }
        public Salle Salle { get; set; }

        // Professeur assigné
        public int? UtilisateurId { get; set; }
        public Utilisateur Utilisateur { get; set; }

        // Département (doit correspondre à Utilisateur.Departement)
        [Required]
        public int DepartementId { get; set; }
    }
}
