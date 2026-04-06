using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace GestionHoraire.Models
{
    public class Classe
    {
        public int Id { get; set; }

        [Required]
        public string Nom { get; set; }  // ex: INFO1-A

        public string Programme { get; set; } // ex: Informatique
        public int Etape { get; set; }        // 1,2,3...
        public int Effectif { get; set; }     // nb d'étudiants

        public ICollection<CoursClasse> CoursClasses { get; set; }
    }
}
