using GestionHoraire.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GestionHoraire.Controllers
{
    public class PlanningController : Controller
    {
        private readonly AppDbContext _context;

        public PlanningController(AppDbContext context)
        {
            _context = context;
        }

        private Task EnsureCoursColumnAsync(string columnName, string sqlDefinition)
        {
            return _context.Database.ExecuteSqlRawAsync($@"
                IF COL_LENGTH('dbo.Cours', '{columnName}') IS NULL
                    EXEC('ALTER TABLE dbo.Cours ADD [{columnName}] {sqlDefinition}');
            ");
        }

        private async Task EnsurePlanningSchemaAsync()
        {
            await EnsureCoursColumnAsync("ProfesseurIds", "NVARCHAR(MAX) NULL");
            await EnsureCoursColumnAsync("GroupeIds", "NVARCHAR(MAX) NULL");
            await EnsureCoursColumnAsync("CodeMinisteriel", "NVARCHAR(MAX) NULL");
            await EnsureCoursColumnAsync("HeuresTheorie", "INT NOT NULL DEFAULT (0)");
            await EnsureCoursColumnAsync("HeuresLabo", "INT NOT NULL DEFAULT (0)");
            await EnsureCoursColumnAsync("HeuresTravailPersonnel", "INT NOT NULL DEFAULT (0)");

            await _context.Database.ExecuteSqlRawAsync(@"
                UPDATE dbo.Cours
                SET ProfesseurIds = CAST(UtilisateurId AS NVARCHAR(20))
                WHERE UtilisateurId IS NOT NULL
                  AND (ProfesseurIds IS NULL OR LTRIM(RTRIM(ProfesseurIds)) = '');
            ");

            await _context.Database.ExecuteSqlRawAsync(@"
                UPDATE dbo.Cours
                SET GroupeIds = CAST(GroupeId AS NVARCHAR(20))
                WHERE GroupeId IS NOT NULL
                  AND (GroupeIds IS NULL OR LTRIM(RTRIM(GroupeIds)) = '');
            ");
        }

        public async Task<IActionResult> Index(int? groupeId)
        {
            await EnsurePlanningSchemaAsync();

            int? deptId = HttpContext.Session.GetInt32("DepartementId");
            string userRole = HttpContext.Session.GetString("UserRole") ?? string.Empty;

            if (deptId == null && userRole != "Administrateur")
            {
                return RedirectToAction("Login", "Account");
            }

            List<Models.Groupe> groupes;
            IQueryable<Models.Cours> query = _context.Cours
                .Include(c => c.Utilisateur)
                .Include(c => c.Salle)
                .Include(c => c.Groupe);

            if (userRole == "Administrateur")
            {
                groupes = await _context.Groupes
                    .OrderBy(g => g.Nom)
                    .ToListAsync();
            }
            else
            {
                groupes = await _context.Groupes
                    .Where(g => g.DepartementId == deptId)
                    .OrderBy(g => g.Nom)
                    .ToListAsync();

                query = query.Where(c => c.DepartementId == deptId);
            }

            if (groupeId.HasValue)
            {
                query = query.Where(c => c.GroupeId == groupeId.Value);
            }

            var cours = groupeId.HasValue
                ? await query.OrderBy(c => c.Jour).ThenBy(c => c.HeureDebut).ToListAsync()
                : new List<Models.Cours>();

            ViewBag.Groupes = new SelectList(groupes, "Id", "Nom", groupeId);
            ViewBag.SelectedGroupeId = groupeId;
            ViewBag.SelectedGroupeName = groupes.FirstOrDefault(g => g.Id == groupeId)?.Nom;
            ViewBag.TotalCoursDuGroupe = cours.Count;
            ViewBag.CoursPlanifies = cours.Count(c => c.HeureDebut != TimeSpan.Zero);
            ViewBag.IsAdmin = userRole == "Administrateur";

            return View(cours);
        }

        [HttpPost]
        public async Task<JsonResult> GenererPlanningAleatoire()
        {
            await EnsurePlanningSchemaAsync();

            try
            {
                int? deptId = HttpContext.Session.GetInt32("DepartementId");
                if (deptId == null)
                {
                    return Json(new { success = false, message = "Session expiree" });
                }

                var cours = await _context.Cours
                    .Where(c => c.DepartementId == deptId)
                    .ToListAsync();

                var disposProfs = await _context.Disponibilites
                    .Where(d => d.Disponible)
                    .ToListAsync();

                if (!cours.Any())
                {
                    return Json(new { success = false, message = "Aucun cours n'est assigne a votre departement." });
                }

                var random = new Random();
                var joursPossibles = new[] { 1, 2, 3, 4, 5 };
                var heuresPossibles = new[] { 8, 10, 14, 16 };
                var coursMelanges = cours.OrderBy(_ => random.Next()).ToList();

                foreach (var c in coursMelanges)
                {
                    bool place = false;
                    int tentatives = 0;

                    while (!place && tentatives < 50)
                    {
                        var jourAlea = (DayOfWeek)joursPossibles[random.Next(joursPossibles.Length)];
                        var heureAlea = heuresPossibles[random.Next(heuresPossibles.Length)];
                        var heureDebutAlea = new TimeSpan(heureAlea, 0, 0);
                        var heureFinAlea = heureDebutAlea.Add(new TimeSpan(2, 0, 0));

                        bool profEstPresent = c.UtilisateurId.HasValue && disposProfs.Any(d =>
                            d.UtilisateurId == c.UtilisateurId.Value &&
                            d.Jour == jourAlea &&
                            d.HeureDebut <= heureDebutAlea &&
                            d.HeureFin >= heureFinAlea);

                        bool salleLibre = !cours.Any(other =>
                            other.Id != c.Id &&
                            other.SalleId == c.SalleId &&
                            other.Jour == jourAlea &&
                            other.HeureDebut == heureDebutAlea);

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

                return Json(new
                {
                    success = true,
                    message = "Planning genere avec succes en respectant les disponibilites !"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Erreur : " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetCoursInfo(int id)
        {
            await EnsurePlanningSchemaAsync();

            var cours = await _context.Cours
                .Include(c => c.Utilisateur)
                .Include(c => c.Salle)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (cours == null)
            {
                return Json(new { success = false });
            }

            var deptId = HttpContext.Session.GetInt32("DepartementId");
            string userRole = HttpContext.Session.GetString("UserRole") ?? string.Empty;

            var salles = await _context.Salles
                .OrderBy(s => s.Nom)
                .ToListAsync();

            IQueryable<Models.Utilisateur> profsQuery = _context.Utilisateurs
                .Where(u => u.Role == "Professeur");

            if (userRole != "Administrateur" && deptId.HasValue)
            {
                profsQuery = profsQuery.Where(u => u.DepartementId == deptId.Value);
            }

            var profs = await profsQuery
                .OrderBy(u => u.Nom)
                .ToListAsync();

            return Json(new
            {
                success = true,
                data = new
                {
                    id = cours.Id,
                    titre = cours.Titre,
                    jour = (int)cours.Jour,
                    heureDebut = cours.HeureDebut.ToString(@"hh\:mm"),
                    salleId = cours.SalleId,
                    profId = cours.UtilisateurId
                },
                salles = salles.Select(s => new { s.Id, s.Nom }),
                profs = profs.Select(p => new { p.Id, p.Nom })
            });
        }

        [HttpPost]
        public async Task<JsonResult> MettreAJourPosition(int id, int jour, string heureDebut, int? salleId, int? profId)
        {
            await EnsurePlanningSchemaAsync();

            try
            {
                var cours = await _context.Cours.FindAsync(id);
                if (cours == null)
                {
                    return Json(new { success = false, message = "Cours introuvable" });
                }

                var nouvelleHeureDebut = TimeSpan.Parse(heureDebut);
                var nouvelleHeureFin = nouvelleHeureDebut.Add(new TimeSpan(2, 0, 0));
                var nouveauJour = (DayOfWeek)jour;

                var allCours = await _context.Cours
                    .Where(c => c.DepartementId == cours.DepartementId)
                    .ToListAsync();

                var allDispos = await _context.Disponibilites
                    .Where(d => d.Disponible)
                    .ToListAsync();

                int? finalSalleId = salleId ?? cours.SalleId;
                int? finalProfId = profId ?? cours.UtilisateurId;

                if (finalProfId.HasValue)
                {
                    bool profDisponible = allDispos.Any(d =>
                        d.UtilisateurId == finalProfId.Value &&
                        d.Jour == nouveauJour &&
                        d.HeureDebut <= nouvelleHeureDebut &&
                        d.HeureFin >= nouvelleHeureFin);

                    if (!profDisponible)
                    {
                        return Json(new { success = false, message = "Le professeur n'est pas disponible sur ce creneau." });
                    }

                    bool conflitProf = allCours.Any(c =>
                        c.Id != id &&
                        c.UtilisateurId == finalProfId &&
                        c.Jour == nouveauJour &&
                        c.HeureDebut == nouvelleHeureDebut);

                    if (conflitProf)
                    {
                        return Json(new { success = false, message = "Le professeur est deja occupe." });
                    }
                }

                if (finalSalleId.HasValue && allCours.Any(c =>
                    c.Id != id &&
                    c.SalleId == finalSalleId &&
                    c.Jour == nouveauJour &&
                    c.HeureDebut == nouvelleHeureDebut))
                {
                    return Json(new { success = false, message = "La salle est deja occupee." });
                }

                if (cours.GroupeId.HasValue && allCours.Any(c =>
                    c.Id != id &&
                    c.GroupeId == cours.GroupeId &&
                    c.Jour == nouveauJour &&
                    c.HeureDebut == nouvelleHeureDebut))
                {
                    return Json(new { success = false, message = "Le groupe est deja en cours." });
                }

                cours.Jour = nouveauJour;
                cours.HeureDebut = nouvelleHeureDebut;
                cours.HeureFin = nouvelleHeureFin;
                cours.SalleId = finalSalleId;

                if (profId.HasValue)
                {
                    cours.UtilisateurId = profId.Value;
                    cours.ProfesseurIds = profId.Value.ToString();
                }

                if (cours.GroupeId.HasValue && string.IsNullOrWhiteSpace(cours.GroupeIds))
                {
                    cours.GroupeIds = cours.GroupeId.Value.ToString();
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public JsonResult TesterLeServeur()
        {
            var heure = DateTime.Now.ToString("HH:mm:ss");

            return Json(new
            {
                success = true,
                message = "Salut ! AJAX fonctionne. Le serveur a repondu a " + heure
            });
        }
    }
}
