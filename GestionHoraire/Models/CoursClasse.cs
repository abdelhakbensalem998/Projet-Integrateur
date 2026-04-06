namespace GestionHoraire.Models
{
    public class CoursClasse
    {
        public int CoursId { get; set; }
        public Cours Cours { get; set; }

        public int ClasseId { get; set; }
        public Classe Classe { get; set; }
    }
}
