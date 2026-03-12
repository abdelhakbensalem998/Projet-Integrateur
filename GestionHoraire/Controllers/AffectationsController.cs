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
    public class AffectationsController : Controller
    {
        private readonly AppDbContext _context;

        public AffectationsController(AppDbContext context)
        {
            _context = context;
        }

        private int? GetMonDeptId() => HttpContext.Session.GetInt32("DepartementId");

        // 1. AFFECTATIONS ET COURS
        public async Task<IActionResult> Index()
        {
            int? monDeptId = GetMonDeptId();
            string userRole = HttpContext.Session.GetString("UserRole");

            IQueryable<Cours> query = _context.Cours.Include(c => c.Utilisateur);

            if (userRole == "Administrateur")
            {
                // L'admin voit toutes les affectations de tous les départements
            }
            else if (monDeptId != null)
            {
                // Le responsable ne voit que celles de son département
                query = query.Where(c => c.DepartementId == monDeptId);
            }
            else
            {
                return RedirectToAction("Index", "Login");
            }

            var cours = await query.ToListAsync();
            return View(cours);
        }

        // 2. CREER UN NOUVEAU COURS
        [HttpGet]
        public IActionResult CreerCours()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreerCours([Bind("Titre,Jour,HeureDebut,HeureFin")] Cours cours)
        {
            var deptId = GetMonDeptId();
            if (deptId == null) return Unauthorized();
            
            cours.DepartementId = deptId.Value;
            
            ModelState.Remove("Utilisateur");
            ModelState.Remove("Departement");
            ModelState.Remove("Salle");
            ModelState.Remove("Groupe");

            if (ModelState.IsValid)
            {
                _context.Add(cours);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(cours);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SupprimerCours(int id)
        {
            var cours = await _context.Cours.FindAsync(id);
            if (cours != null)
            {
                int? deptId = GetMonDeptId();
                string userRole = HttpContext.Session.GetString("UserRole");
                
                // Allow delete if Admin or if it belongs to Responsable's department
                if (userRole == "Administrateur" || cours.DepartementId == deptId)
                {
                    _context.Cours.Remove(cours);
                    await _context.SaveChangesAsync();
                }
            }
            return RedirectToAction(nameof(Index));
        }

        // 3. EDITER AFFECTATION
        [HttpGet]
        public async Task<IActionResult> EditerAffectation(int id)
        {
            var cours = await _context.Cours.FindAsync(id);
            if (cours == null) return NotFound();

            int? deptId = GetMonDeptId();
            string userRole = HttpContext.Session.GetString("UserRole");
            
            // Check permissions
            if (userRole != "Administrateur" && cours.DepartementId != deptId)
                return Unauthorized();

            var profsQuery = _context.Utilisateurs.Where(u => u.Role == "Professeur");
            if (userRole != "Administrateur")
                profsQuery = profsQuery.Where(u => u.DepartementId == deptId);

            var profs = await profsQuery.ToListAsync();

            ViewBag.Professeurs = new SelectList(profs, "Id", "Nom", cours.UtilisateurId);
            return View(cours);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditerAffectation(int id, int? utilisateurId)
        {
            var cours = await _context.Cours.FindAsync(id);
            if (cours == null) return NotFound();

            int? deptId = GetMonDeptId();
            string userRole = HttpContext.Session.GetString("UserRole");
            
            // Check permissions
            if (userRole != "Administrateur" && cours.DepartementId != deptId)
                return Unauthorized();

            cours.UtilisateurId = utilisateurId;
            _context.Update(cours);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // 3. DISPONIBILITÉS
        public async Task<IActionResult> VoirDispos(int id)
        {
            var prof = await _context.Utilisateurs.FindAsync(id);
            if (prof == null) return NotFound();

            var dispos = await _context.Disponibilites
                .Where(d => d.UtilisateurId == id)
                .OrderBy(d => d.Jour).ThenBy(d => d.HeureDebut)
                .ToListAsync();

            ViewBag.ProfNom = prof.Nom;
            ViewBag.ProfId = prof.Id;
            return View(dispos);
        }

        // Ajouter / Supprimer Dispo
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AjouterDispo(int profId, DayOfWeek jour, TimeSpan debut, TimeSpan fin)
        {
            var dispo = new Disponibilite
            {
                UtilisateurId = profId,
                Jour = jour,
                HeureDebut = debut,
                HeureFin = fin
            };

            _context.Disponibilites.Add(dispo);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(VoirDispos), new { id = profId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SupprimerDispo(int id, int profId)
        {
            var dispo = await _context.Disponibilites.FindAsync(id);
            if (dispo != null)
            {
                _context.Disponibilites.Remove(dispo);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(VoirDispos), new { id = profId });
        }
    }
}
