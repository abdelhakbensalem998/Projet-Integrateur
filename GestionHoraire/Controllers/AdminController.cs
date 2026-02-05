using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using GestionHoraire.Data;
using GestionHoraire.Models;

namespace GestionHoraire.Controllers
{
    public class AdminController : Controller
    {
        private readonly AppDbContext _context;

        public AdminController(AppDbContext context)
        {
            _context = context;
        }

        // Cette méthode doit s'appeler Index pour l'URL https://localhost:7092/Admin
        public IActionResult Index(string roleFilter)
        {
            // 1. Sécurité : on récupère les infos de session
            ViewBag.UserNom = HttpContext.Session.GetString("UserNom") ?? "Admin";
            ViewBag.UserRole = HttpContext.Session.GetString("UserRole") ?? "Administrateur";

            // 2. On charge les départements pour le menu déroulant
            ViewBag.Departements = _context.Departements.ToList();

            // 3. On prépare la liste des utilisateurs
            var query = _context.Utilisateurs.Include(u => u.Departement).AsQueryable();

            if (!string.IsNullOrEmpty(roleFilter))
            {
                query = query.Where(u => u.Role == roleFilter);
            }

            var listeDutilisateurs = query.ToList();

            // 4. CRUCIAL : On envoie la liste à la vue pour éviter le NullReferenceException
            return View(listeDutilisateurs);
        }
    }
}
