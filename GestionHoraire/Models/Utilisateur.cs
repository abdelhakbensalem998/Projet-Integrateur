using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionHoraire.Models
{
    public class Utilisateur
    {
        public int Id { get; set; }
        public string Nom { get; set; }
        public string Email { get; set; }
        public DateTime DateCreation { get; set; }
        public string Role { get; set; }
        public int? DepartementId { get; set; }
        public Departement Departement { get; set; }
        public bool Disponibilite { get; set; }
        public byte[] MotDePasseHash { get; set; }   // VARBINARY en SQL
        public Guid MotDePasseSalt { get; set; }     // UNIQUEIDENTIFIER en SQL
    }

}

