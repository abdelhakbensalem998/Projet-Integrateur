using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using GestionHoraire.Data;
using GestionHoraire.Models;

namespace GestionHoraire.Controllers
{
    public class GroupesController : Controller
    {
        private readonly AppDbContext _context;

        public GroupesController(AppDbContext context)
        {
            _context = context;
        }

        // 1. LISTE DES GROUPES

        public async Task<IActionResult> Index()
        {
            // 1. Récupérer l'ID du département depuis la SESSION
            int? departementId = HttpContext.Session.GetInt32("DepartementId");

            // 2. Vérifier si l'ID existe
            if (departementId == null)
            {
                // Si pas de session, on peut rediriger vers le login ou afficher liste vide
                return RedirectToAction("Index", "Login");
            }

            // 3. Filtrer les groupes par ce DepartementId
            var groupes = await _context.Groupes
                                        .Include(g => g.Departement)
                                        .Where(g => g.DepartementId == departementId.Value)
                                        .OrderBy(g => g.Nom)
                                        .ToListAsync();

            return View("groupe", groupes);
        }

        // 3. AJOUTER - POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Ajouter(Groupe groupe)
        {
            int? departementId = HttpContext.Session.GetInt32("DepartementId");

            if (departementId != null)
            {
                groupe.DepartementId = departementId.Value; // On force son département
            }

            // On retire les erreurs de validation liées au département puisqu'on le gère en interne
            ModelState.Remove("Departement");
            ModelState.Remove("DepartementId");

            if (ModelState.IsValid)
            {
                _context.Add(groupe);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(groupe);
        }
        // AJOUTER - GET (C'est cette méthode qui ouvre la page !)
        [HttpGet]
        public IActionResult Ajouter()
        {
            ViewData["Title"] = "Ajouter un nouveau groupe";
            return View();
        }

        // 4. MODIFIER - GET
        // 4. MODIFIER - GET : Affiche le formulaire avec les données actuelles
        [HttpGet]
        public async Task<IActionResult> Modifier(int? id)
        {
            if (id == null) return NotFound();

            // On cherche le groupe dans la base
            var groupe = await _context.Groupes.FindAsync(id);

            if (groupe == null) return NotFound();

            // On vérifie que le groupe appartient bien au département du responsable (Sécurité)
            int? userDeptId = HttpContext.Session.GetInt32("DepartementId");
            if (groupe.DepartementId != userDeptId)
            {
                return Unauthorized(); // Le responsable ne peut pas modifier un groupe d'un autre département
            }

            return View(groupe);
        }


        // 5. MODIFIER - POST
        [HttpPost]
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

        // 6. SUPPRIMER - POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Supprimer(int id)
        {
            var groupe = await _context.Groupes.FindAsync(id);
            if (groupe != null)
            {
                _context.Groupes.Remove(groupe);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        // Vérification d'existence (Helper)
        private bool GroupeExists(int id)
        {
            return _context.Groupes.Any(e => e.Id == id);
        }
    }
}