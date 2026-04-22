using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using GestionHoraire.Data;
using GestionHoraire.Models;
using Microsoft.AspNetCore.Http;
using System.Linq;
using System.Threading.Tasks;

namespace GestionHoraire.Controllers
{
    [Route("Groupes")]
    [Route("Groupe")]
    public class GroupesController : Controller
    {
        private readonly AppDbContext _context;

        public GroupesController(AppDbContext context)
        {
            _context = context;
        }

        // ==========================================
        // 1. LISTE DES GROUPES (INDEX)
        // ==========================================
        [Route("")]
        [Route("Index")]
        public async Task<IActionResult> Index(int? idDept)
        {
            // Récupération des infos de session
            string userRole = HttpContext.Session.GetString("UserRole") ?? "";
            int? sessionDeptId = HttpContext.Session.GetInt32("DepartementId");

            // Requête de base avec jointure sur le département
            var query = _context.Groupes.Include(g => g.Departement).AsQueryable();

            if (string.Equals(userRole, "Administrateur", StringComparison.OrdinalIgnoreCase))
            {
                // Pour l'Admin : on prépare la liste de tous les départements pour le filtre
                var listeDepts = await _context.Departements.OrderBy(d => d.Nom).ToListAsync();
                ViewBag.Departements = new SelectList(listeDepts, "Id", "Nom", idDept);

                // Application du filtre si sélectionné
                if (idDept.HasValue && idDept > 0)
                {
                    query = query.Where(g => g.DepartementId == idDept.Value);
                }
            }
            else
            {
                // Pour le Responsable : filtrage strict sur son département de session
                if (sessionDeptId == null) return RedirectToAction("Index", "Login");
                query = query.Where(g => g.DepartementId == sessionDeptId.Value);
            }

            var groupes = await query
                .OrderBy(g => g.Departement.Nom)
                .ThenBy(g => g.Nom)
                .ToListAsync();

            return View("groupe", groupes);
        }

        // ==========================================
        // 2. AJOUTER UN GROUPE
        // ==========================================
        [HttpGet("Ajouter")]
        public async Task<IActionResult> Ajouter()
        {
            // On passe la liste des départements pour que l'admin puisse choisir
            ViewBag.Departements = new SelectList(await _context.Departements.ToListAsync(), "Id", "Nom");
            return View();
        }

        [HttpPost("Ajouter")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Ajouter(Groupe groupe)
        {
            string userRole = HttpContext.Session.GetString("UserRole") ?? "";

            // Sécurité : si ce n'est pas un admin, on force le département du responsable
            if (userRole != "Administrateur")
            {
                groupe.DepartementId = HttpContext.Session.GetInt32("DepartementId") ?? 0;
            }

            if (ModelState.IsValid)
            {
                _context.Add(groupe);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Le groupe a été ajouté avec succès.";
                return RedirectToAction(nameof(Index));
            }

            ViewBag.Departements = new SelectList(await _context.Departements.ToListAsync(), "Id", "Nom", groupe.DepartementId);
            return View(groupe);
        }

        // ==========================================
        // 3. MODIFIER UN GROUPE
        // ==========================================
        [HttpGet("Modifier/{id}")]
        public async Task<IActionResult> Modifier(int? id)
        {
            if (id == null) return NotFound();

            var groupe = await _context.Groupes.FindAsync(id);
            if (groupe == null) return NotFound();

            // Vérification de sécurité pour les responsables
            string userRole = HttpContext.Session.GetString("UserRole") ?? "";
            int? sessionDeptId = HttpContext.Session.GetInt32("DepartementId");

            if (userRole != "Administrateur" && groupe.DepartementId != sessionDeptId)
            {
                return RedirectToAction("Index", "Login");
            }

            ViewBag.Departements = new SelectList(await _context.Departements.ToListAsync(), "Id", "Nom", groupe.DepartementId);
            return View(groupe);
        }

        [HttpPost("Modifier/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Modifier(int id, Groupe groupe)
        {
            if (id != groupe.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(groupe);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Le groupe a été modifié avec succès.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!GroupeExists(groupe.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            ViewBag.Departements = new SelectList(await _context.Departements.ToListAsync(), "Id", "Nom", groupe.DepartementId);
            return View(groupe);
        }

        // ==========================================
        // 4. SUPPRIMER UN GROUPE
        // ==========================================
        [HttpPost("Supprimer/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Supprimer(int id)
        {
            var groupe = await _context.Groupes.FindAsync(id);
            if (groupe == null) return NotFound();

            // Sécurité additionnelle
            string userRole = HttpContext.Session.GetString("UserRole") ?? "";
            int? sessionDeptId = HttpContext.Session.GetInt32("DepartementId");

            if (userRole != "Administrateur" && groupe.DepartementId != sessionDeptId)
            {
                return RedirectToAction("Index", "Login");
            }

            _context.Groupes.Remove(groupe);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Le groupe a été supprimé.";
            return RedirectToAction(nameof(Index));
        }

        private bool GroupeExists(int id)
        {
            return _context.Groupes.Any(e => e.Id == id);
        }
    }
}