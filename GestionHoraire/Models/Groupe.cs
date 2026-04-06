using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionHoraire.Models
{
    public class Groupe
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Le nom du groupe est obligatoire")]
        [Display(Name = "Nom du Groupe")]
        public string Nom { get; set; } // ex: L3 Informatique

        [Required(ErrorMessage = "Le niveau est obligatoire")]
        public string Niveau { get; set; } // ex: Licence 1, Master 2

        [Required(ErrorMessage = "L'effectif est obligatoire")]
        [Range(1, 1000)]
        [Display(Name = "Nombre d'étudiants")]
        public int Effectif { get; set; }

        [Required]
        [Display(Name = "Département")]
        public int DepartementId { get; set; }

        [ForeignKey("DepartementId")]
        public Departement? Departement { get; set; }
    }
}