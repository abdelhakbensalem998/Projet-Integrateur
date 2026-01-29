using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GestionHoraire.Data;
using GestionHoraire.Models;

namespace GestionHoraire.Controllers
{
    public class ResponsableController : Controller
    {
        private readonly AppDbContext _context;

        public ResponsableController(AppDbContext context)
        {
            _context = context;
        }

        // Dashboard (Ticket 02)
        public IActionResult Index() => View();

        // Liste des Salles (Ticket 03)
        public async Task<IActionResult> Salles()
        {
            var salles = await _context.Salles.ToListAsync();
            return View(salles);
        }

        // Liste des Professeurs (Ticket 04)
        public async Task<IActionResult> Profs()
        {
            // 1. Simuler l'ID du département du responsable connecté (ex: ID = 1)
            // Plus tard, tu récupéreras cela via User.Claims ou Session
            int monDeptId = 1;

            // 2. Récupérer les profs du même département
            var listeProfs = await _context.Utilisateurs
                .Include(u => u.Departement) // Indispensable pour afficher le nom du département
                .Where(u => u.Role == "Professeur")
                .Where(u => u.DepartementId == monDeptId) // Filtre strict
                .ToListAsync();

            // 3. Envoyer à la vue "professeurs.cshtml" (Option B)
            return View("professeurs", listeProfs);
        }
        // GET: Responsable/Groupes
        public async Task<IActionResult> Groupes()
        {
            // 1. On identifie le département du responsable (Simulé ici avec l'ID 1)
            // Plus tard, ce sera : var user = await _context.Utilisateurs.FindAsync(GetUserId());
            int monDeptId = 1;

            // 2. On récupère UNIQUEMENT son département et les données liées
            var monDepartement = await _context.Departements
                .Include(d => d.Utilisateurs) // Pour compter les étudiants si nécessaire
                .Where(d => d.Id == monDeptId)
                .ToListAsync();

            return View(monDepartement);
        }
        public async Task<IActionResult> ConsulterPlanning(int deptId)
        {
            // On récupère les cours pour les afficher dans la grille
            var coursDuDept = await _context.Cours
                .Include(c => c.Utilisateur)
                .Include(c => c.Salle)
                .Where(c => c.DepartementId == deptId)
                .ToListAsync();

            return View(coursDuDept);
        }
    }
}