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

            IQueryable<Cours> query = _context.Cours
                .Include(c => c.Utilisateur)
                .Include(c => c.Salle); // Added .Include(c => c.Salle) as per the provided Code Edit example

            if (string.Equals(userRole, "Administrateur", StringComparison.OrdinalIgnoreCase))
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
            ViewBag.AllGroupes = await _context.Groupes.ToListAsync();
            ViewBag.AllUsers = await _context.Utilisateurs.ToListAsync();
            return View(cours);
        }

        // 2. CREER UN NOUVEAU COURS
        [HttpGet]
        public async Task<IActionResult> CreerCours()
        {
            int? deptId = GetMonDeptId();
            var groupesQuery = _context.Groupes.AsQueryable();
            if (deptId != null) 
                groupesQuery = groupesQuery.Where(g => g.DepartementId == deptId);
            
            var profsQuery = _context.Utilisateurs.Where(u => u.Role == "Professeur");
            if (deptId != null) profsQuery = profsQuery.Where(u => u.DepartementId == deptId);

            ViewBag.Groupes = new SelectList(await groupesQuery.ToListAsync(), "Id", "Nom");
            ViewBag.Professeurs = await profsQuery.ToListAsync();
            ViewBag.Salles = new SelectList(await _context.Salles.ToListAsync(), "Id", "Nom");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreerCours([Bind("Titre")] Cours cours, int[] groupeIds, int[] profIds, int? salleId)
        {
            var deptId = GetMonDeptId();
            if (deptId == null) return RedirectToAction("Index", "Login");
            
            var profIdsStr = (profIds != null && profIds.Length > 0) ? string.Join(",", profIds) : null;
            var primaryProfId = (profIds != null && profIds.Length > 0) ? (int?)profIds[0] : null;
            if (groupeIds == null || groupeIds.Length == 0)
            {
                // Fallback (should not happen with required attribute)
                cours.DepartementId = deptId.Value;
                cours.SalleId = salleId;
                _context.Add(cours);
            }
            else
            {
                foreach (var gid in groupeIds)
                {
                    string targetGid = gid.ToString();
                    // Check if already exists for this group and title
                    var exists = await _context.Cours.AnyAsync(c => c.DepartementId == deptId.Value && c.Titre == cours.Titre && c.GroupeIds == targetGid);
                    if (exists) continue;

                    var newCours = new Cours
                    {
                        Titre = cours.Titre,
                        DepartementId = deptId.Value,
                        SalleId = salleId,
                        GroupeIds = targetGid,
                        ProfesseurIds = profIdsStr,
                        UtilisateurId = primaryProfId,
                        Jour = DayOfWeek.Monday,
                        HeureDebut = TimeSpan.Zero,
                        HeureFin = TimeSpan.Zero
                    };
                    _context.Add(newCours);
                }
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
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
                
                if (string.Equals(userRole, "Administrateur", StringComparison.OrdinalIgnoreCase) || cours.DepartementId == deptId)
                {
                    // Group deletion: find all related courses (same title, same professor(s))
                    // Normalize for comparison
                    string nTitre = (cours.Titre ?? "").Trim().ToLower();
                    string nPhIds = (cours.ProfesseurIds ?? "").Trim().TrimEnd(',');
                    
                    var allCourses = await _context.Cours.Where(c => c.DepartementId == cours.DepartementId).ToListAsync();
                    var related = allCourses
                        .Where(c => (c.Titre ?? "").Trim().ToLower() == nTitre && 
                                    c.UtilisateurId == cours.UtilisateurId &&
                                    (c.ProfesseurIds ?? "").Trim().TrimEnd(',') == nPhIds)
                        .ToList();
                    
                    _context.Cours.RemoveRange(related);
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
            
            if (!string.Equals(userRole, "Administrateur", StringComparison.OrdinalIgnoreCase) && cours.DepartementId != deptId)
                return RedirectToAction("Index", "Login");

            var profsQuery = _context.Utilisateurs.Where(u => u.Role == "Professeur");
            var groupesQuery = _context.Groupes.AsQueryable();

            if (!string.Equals(userRole, "Administrateur", StringComparison.OrdinalIgnoreCase))
            {
                profsQuery = profsQuery.Where(u => u.DepartementId == deptId);
                groupesQuery = groupesQuery.Where(g => g.DepartementId == deptId);
            }

            // Current IDs for selection
            ViewBag.CurrentProfIds = cours.ProfesseurIds?.Split(',') ?? new string[0];
            ViewBag.CurrentGroupeIds = cours.GroupeIds?.Split(',') ?? new string[0];

            ViewBag.ProfesseursList = await profsQuery.ToListAsync();
            ViewBag.GroupesList = await groupesQuery.ToListAsync();
            ViewBag.Salles = new SelectList(await _context.Salles.ToListAsync(), "Id", "Nom", cours.SalleId);
            
            return View(cours);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditerAffectation(int id, int[] profIds, int[] groupeIds, int? salleId)
        {
            var cours = await _context.Cours.FindAsync(id);
            if (cours == null) return NotFound();

            int? deptId = GetMonDeptId();
            string userRole = HttpContext.Session.GetString("UserRole");
            
            if (!string.Equals(userRole, "Administrateur", StringComparison.OrdinalIgnoreCase) && cours.DepartementId != deptId)
                return RedirectToAction("Index", "Login");

            // Support multi-profs
            cours.ProfesseurIds = (profIds != null && profIds.Length > 0) 
                                 ? string.Join(",", profIds) 
                                 : null;
            
            // If at least one prof, set the first as UtilisateurId for backward compatibility
            cours.UtilisateurId = (profIds != null && profIds.Length > 0) ? profIds[0] : null;

            cours.SalleId = salleId;
            
            // To maintain independence, if multiple groups are selected during edit, 
            // we keep the current one as the first selected, and create clones for others.
            if (groupeIds != null && groupeIds.Length > 0)
            {
                cours.GroupeIds = groupeIds[0].ToString();
                for (int i = 1; i < groupeIds.Length; i++)
                {
                    string targetGid = groupeIds[i].ToString();
                    // Skip if a course with this title and this group already exists in the department
                    bool alreadyExists = await _context.Cours.AnyAsync(c => c.DepartementId == cours.DepartementId && c.Titre == cours.Titre && c.GroupeIds == targetGid);
                    if (alreadyExists) continue;

                    var clone = new Cours
                    {
                        Titre = cours.Titre,
                        DepartementId = cours.DepartementId,
                        SalleId = cours.SalleId,
                        UtilisateurId = cours.UtilisateurId,
                        ProfesseurIds = cours.ProfesseurIds,
                        GroupeIds = targetGid,
                        Jour = DayOfWeek.Monday,
                        HeureDebut = TimeSpan.Zero,
                        HeureFin = TimeSpan.Zero
                    };
                    _context.Add(clone);
                }
            }

            _context.Update(cours);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // 3. DISPONIBILITÉS
        public IActionResult VoirDispos(int id)
        {
            return RedirectToAction("Index", "Disponibilite", new { professeurId = id, returnUrl = "/Affectations/Index" });
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
