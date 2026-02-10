using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GestionHoraire.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace GestionHoraire.Controllers
{
    public class PlanningController : Controller
    {
        private readonly AppDbContext _context;

        public PlanningController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(int? groupeId)
        {
            int? deptId = HttpContext.Session.GetInt32("DepartementId");
            if (deptId == null) return RedirectToAction("Login", "Account");

            var groupesDuDept = await _context.Groupes.Where(g => g.DepartementId == deptId).ToListAsync();
            ViewBag.Groupes = new SelectList(groupesDuDept, "Id", "Nom", groupeId);

            var query = _context.Cours
                .Include(c => c.Utilisateur)
                .Include(c => c.Salle)
                .Include(c => c.Groupe)
                .Where(c => c.DepartementId == deptId);

            if (groupeId.HasValue)
            {
                query = query.Where(c => c.GroupeId == groupeId);
            }

            var cours = await query.ToListAsync();
            return View(cours);
        }

        // Action AJAX pour générer le planning
        [HttpPost]
        public async Task<JsonResult> GenererPlanningAleatoire()
        {
            try
            {
                int? deptId = HttpContext.Session.GetInt32("DepartementId");
                if (deptId == null) return Json(new { success = false, message = "Session expirée" });

                var cours = await _context.Cours.Where(c => c.DepartementId == deptId).ToListAsync();
                var disposProfs = await _context.Disponibilites.Where(d => d.Disponible == true).ToListAsync();

                var random = new Random();
                var joursPossibles = new[] { 1, 2, 3, 4, 5 }; // Lun-Ven
                var heuresPossibles = new[] { 8, 10, 14, 16 };

                // On mélange les cours pour plus d'équité
                var coursMelanges = cours.OrderBy(x => random.Next()).ToList();

                foreach (var c in coursMelanges)
                {
                    bool place = false;
                    int tentatives = 0;

                    while (!place && tentatives < 50)
                    {
                        var jourAlea = (DayOfWeek)joursPossibles[random.Next(joursPossibles.Length)];
                        var hAlea = heuresPossibles[random.Next(heuresPossibles.Length)];
                        var heureDebutAlea = new TimeSpan(hAlea, 0, 0);
                        var heureFinAlea = heureDebutAlea.Add(new TimeSpan(2, 0, 0));

                        // 1. Vérifier si le prof a déclaré être PRÉSENT sur ce créneau
                        bool profEstPresent = disposProfs.Any(d =>
                            d.UtilisateurId == c.UtilisateurId &&
                            d.Jour == jourAlea &&
                            d.HeureDebut <= heureDebutAlea &&
                            d.HeureFin >= heureFinAlea);

                        // 2. Vérifier si la salle est libre dans notre liste locale
                        bool salleLibre = !cours.Any(other =>
                            other.Id != c.Id &&
                            other.SalleId == c.SalleId &&
                            other.Jour == jourAlea &&
                            other.HeureDebut == heureDebutAlea);

                        // 3. Vérifier si le groupe est libre
                        bool groupeLibre = !cours.Any(other =>
                            other.Id != c.Id &&
                            other.GroupeId == c.GroupeId &&
                            other.Jour == jourAlea &&
                            other.HeureDebut == heureDebutAlea);

                        if (profEstPresent && salleLibre && groupeLibre)
                        {
                            c.Jour = jourAlea;
                            c.HeureDebut = heureDebutAlea;
                            c.HeureFin = heureFinAlea;
                            place = true;
                        }
                        tentatives++;
                    }
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Planning généré avec succès en respectant les disponibilités !" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Erreur : " + ex.Message });
            }
        }
        [HttpPost]
        public JsonResult TesterLeServeur()
        {
            // On récupère l'heure pour prouver que le serveur répond en temps réel
            var heure = DateTime.Now.ToString("HH:mm:ss");

            return Json(new
            {
                success = true,
                message = "Salut ! AJAX fonctionne. Le serveur a répondu à " + heure
            });
        }
    }
}