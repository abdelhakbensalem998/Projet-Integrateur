using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using GestionHoraire.Data;
using GestionHoraire.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace GestionHoraire.Controllers
{
    public class AdminController : Controller
    {
        private readonly AppDbContext _context;

        public AdminController(AppDbContext context)
        {
            _context = context;
        }

        // ==========================================
        // 1. DASHBOARD (AVEC COMPTEURS DYNAMIQUES)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            // Informations de session pour l'affichage
            ViewBag.UserNom = HttpContext.Session.GetString("UserNom") ?? "Admin";
            ViewBag.UserRole = HttpContext.Session.GetString("UserRole") ?? "Administrateur";

            // Calcul des statistiques pour les badges .badge-count
            ViewBag.TotalUsers = await _context.Utilisateurs.CountAsync();
            ViewBag.TotalSalles = await _context.Salles.CountAsync();
            ViewBag.TotalGroupes = await _context.Groupes.CountAsync();
            ViewBag.TotalDepts = await _context.Departements.CountAsync();

            return View();
        }

        [HttpGet]
        public IActionResult Index() => RedirectToAction(nameof(Dashboard));

        // ==========================================
        // 2. GESTION DES UTILISATEURS (LISTE)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Utilisateurs(string? roleFilter = null, string? searchTerm = null)
        {
            ViewBag.UserNom = HttpContext.Session.GetString("UserNom") ?? "Admin";
            ViewBag.UserRole = HttpContext.Session.GetString("UserRole") ?? "Administrateur";

            var query = _context.Utilisateurs
                .Include(u => u.Departement)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                searchTerm = searchTerm.Trim().ToLower();
                query = query.Where(u =>
                    (!string.IsNullOrEmpty(u.Nom) && u.Nom.ToLower().Contains(searchTerm)) ||
                    (!string.IsNullOrEmpty(u.Email) && u.Email.ToLower().Contains(searchTerm)));
            }

            if (!string.IsNullOrEmpty(roleFilter))
            {
                query = query.Where(u => u.Role == roleFilter);
            }

            var users = await query.OrderBy(u => u.Nom).ToListAsync();
            return View(users);
        }

        // ==========================================
        // 3. CRÉATION D'UTILISATEUR
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> CreateUser()
        {
            ViewBag.Departements = await _context.Departements.ToListAsync();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateUser(Utilisateur user)
        {
            if (ModelState.IsValid)
            {
                user.DateCreation = DateTime.Now;
                _context.Utilisateurs.Add(user);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Utilisateur créé avec succès !";
                return RedirectToAction(nameof(Utilisateurs));
            }

            ViewBag.Departements = await _context.Departements.ToListAsync();
            return View(user);
        }

        // ==========================================
        // 4. MODIFICATION D'UTILISATEUR
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> EditUser(int id)
        {
            var user = await _context.Utilisateurs.FindAsync(id);
            if (user == null) return NotFound();

            ViewBag.Departements = await _context.Departements.ToListAsync();
            return View(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditUser(int id, Utilisateur user)
        {
            if (id != user.Id) return BadRequest();

            if (ModelState.IsValid)
            {
                try
                {
                    var existing = await _context.Utilisateurs.FindAsync(id);
                    if (existing == null) return NotFound();

                    existing.Nom = user.Nom;
                    existing.Email = user.Email;
                    existing.Role = user.Role;
                    existing.DepartementId = user.DepartementId;

                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Informations mises à jour.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!UserExists(id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Utilisateurs));
            }

            ViewBag.Departements = await _context.Departements.ToListAsync();
            return View(user);
        }

        // ==========================================
        // 5. SUPPRESSION D'UTILISATEUR
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var user = await _context.Utilisateurs.FindAsync(id);
            if (user != null)
            {
                _context.Utilisateurs.Remove(user);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Utilisateur supprimé.";
            }
            return RedirectToAction(nameof(Utilisateurs));
        }

        private bool UserExists(int id)
        {
            return _context.Utilisateurs.Any(e => e.Id == id);
        }
    }
}
