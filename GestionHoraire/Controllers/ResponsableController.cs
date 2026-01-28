using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using GestionHoraire.Data;
using GestionHoraire.Models;
using System.Linq;

namespace GestionHoraire.Controllers
{
    public class ResponsableController : Controller
    {
        private readonly AppDbContext _context;

        public ResponsableController(AppDbContext context)
        {
            _context = context;
        }

        // Page principale
        public IActionResult Index()
        {
            // Récupérer l'utilisateur connecté depuis la session
            var userEmail = HttpContext.Session.GetString("UserEmail") ?? "";
            var user = _context.Utilisateurs
                               .Include(u => u.Departement)
                               .Include(u => u.Disponibilites) // inclure disponibilités si table créée
                               .FirstOrDefault(u => u.Email == userEmail);

            if (user == null || user.DepartementId == 0)
            {
                TempData["Error"] = "Utilisateur ou département introuvable.";
                return RedirectToAction("Index", "Login");
            }

            var deptId = user.DepartementId;

            // Infos utilisateur pour affichage
            ViewBag.UserNom = user.Nom;
            ViewBag.UserEmail = user.Email;
            ViewBag.UserRole = user.Role;
            ViewBag.DepartementNom = user.Departement?.Nom ?? "";

            // Tous les cours du département
            ViewBag.CoursList = _context.Cours
                .Include(c => c.Salle)
                .Include(c => c.Utilisateur)
                .Where(c => c.DepartementId == deptId)
                .OrderBy(c => c.Id)
                .AsNoTracking()
                .ToList();

            // Tous les professeurs du département avec disponibilités
            ViewBag.Professeurs = _context.Utilisateurs
                .Where(u => u.Role == "Professeur" && u.DepartementId == deptId)
                .ToList();

            // Toutes les salles
            ViewBag.Salles = _context.Salles.ToList();

            return View();
        }

        // Mettre à jour la salle d'un cours
        [HttpPost]
        public IActionResult UpdateSalle(int CoursId, int? SalleId)
        {
            var cours = _context.Cours.Find(CoursId);
            if (cours == null)
            {
                TempData["Error"] = "❌ Cours introuvable.";
                return RedirectToAction("Index");
            }

            if (SalleId != null && !_context.Salles.Any(s => s.Id == SalleId))
            {
                TempData["Error"] = "❌ Salle introuvable.";
                return RedirectToAction("Index");
            }

            cours.SalleId = SalleId;
            _context.SaveChanges();
            TempData["Success"] = "✅ Salle modifiée avec succès !";

            return RedirectToAction("Index");
        }

        // Assigner un professeur à un cours
        [HttpPost]
        public IActionResult UpdateProfesseur(int CoursId, int? UtilisateurId)
        {
            var cours = _context.Cours.Find(CoursId);
            if (cours == null)
            {
                TempData["Error"] = "❌ Cours introuvable.";
                return RedirectToAction("Index");
            }

            if (UtilisateurId != null && !_context.Utilisateurs.Any(u => u.Id == UtilisateurId && u.Role == "Professeur"))
            {
                TempData["Error"] = "❌ Professeur introuvable.";
                return RedirectToAction("Index");
            }

            cours.UtilisateurId = UtilisateurId;
            _context.SaveChanges();
            TempData["Success"] = "✅ Professeur assigné avec succès !";

            return RedirectToAction("Index");
        }

        // Modifier une disponibilité (si table Disponibilites créée)
        [HttpPost]
        public IActionResult UpdateDisponibilite(int DisponibiliteId, bool Disponible)
        {
            var dispo = _context.Disponibilites
                        .Include(d => d.Utilisateur)
                        .FirstOrDefault(d => d.Id == DisponibiliteId);

            if (dispo == null)
            {
                TempData["Error"] = "❌ Disponibilité introuvable.";
                return RedirectToAction("Index");
            }

            // Vérification rôle : prof ne peut modifier que sa propre dispo
            var userId = HttpContext.Session.GetInt32("UserId");
            var userRole = HttpContext.Session.GetString("UserRole");

            if (userRole == "Professeur" && dispo.UtilisateurId != userId)
            {
                TempData["Error"] = "❌ Vous ne pouvez pas modifier cette disponibilité.";
                return RedirectToAction("Index");
            }

            dispo.Disponible = Disponible;
            _context.SaveChanges();

            TempData["Success"] = "✅ Disponibilité mise à jour !";
            return RedirectToAction("Index");
        }
    }
}
