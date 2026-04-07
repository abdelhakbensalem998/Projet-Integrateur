using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GestionHoraire.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
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

        private async Task AutoReparerSchema()
        {
            try
            {
                // Ensure GroupeIds exists
                await _context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Cours') AND name = 'GroupeIds')
                        ALTER TABLE Cours ADD GroupeIds NVARCHAR(MAX) NULL;
                ");

                // SEED: Split multi-group courses into independent records
                // We'll do this in C# for safer string handling
                var multiGroupCours = await _context.Cours
                    .Where(c => c.GroupeIds != null && c.GroupeIds.Contains(","))
                    .ToListAsync();

                if (multiGroupCours.Any())
                {
                    foreach (var mc in multiGroupCours)
                    {
                        var gids = mc.GroupeIds.Split(',', StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 1; i < gids.Length; i++)
                        {
                            var clone = new Models.Cours
                            {
                                Titre = mc.Titre,
                                DepartementId = mc.DepartementId,
                                SalleId = mc.SalleId,
                                UtilisateurId = mc.UtilisateurId,
                                ProfesseurIds = mc.ProfesseurIds,
                                GroupeIds = gids[i].Trim(),
                                Jour = mc.Jour,
                                HeureDebut = mc.HeureDebut,
                                HeureFin = mc.HeureFin
                            };
                            _context.Cours.Add(clone);
                        }
                        mc.GroupeIds = gids[0].Trim();
                    }
                    await _context.SaveChangesAsync();
                }
            }
            catch { }
        }

        public async Task<IActionResult> Index(int? groupeId)
        {
            await AutoReparerSchema();
            
            int? deptId = HttpContext.Session.GetInt32("DepartementId");
            if (deptId == null) return RedirectToAction("Login", "Account");

            string userRole = HttpContext.Session.GetString("UserRole");
            List<Models.Groupe> groupes;

            if (userRole == "Administrateur")
                groupes = await _context.Groupes.ToListAsync();
            else
                groupes = await _context.Groupes.Where(g => g.DepartementId == deptId).ToListAsync();

            ViewBag.Groupes = new SelectList(groupes, "Id", "Nom", groupeId);
            ViewBag.SelectedGroupeId = groupeId;
            ViewBag.SelectedGroupeName = groupes.FirstOrDefault(g => g.Id == groupeId)?.Nom;

            if (groupeId.HasValue)
            {
                var targetId = groupeId.Value.ToString();
                var allCours = await _context.Cours
                    .Include(c => c.Utilisateur)
                    .Include(c => c.Salle)
                    .Where(c => c.DepartementId == deptId)
                    .ToListAsync();

                // Strict filter: only show courses where GroupeIds contains this group's ID
                var filtered = allCours
                    .Where(c => !string.IsNullOrEmpty(c.GroupeIds) && 
                                c.GroupeIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                 .Select(s => s.Trim())
                                 .Contains(targetId))
                    .OrderBy(c => c.Jour)
                    .ThenBy(c => c.HeureDebut)
                    .ToList();

                // Diagnostic counts for the view
                ViewBag.TotalCoursDuGroupe = filtered.Count;
                ViewBag.CoursPlanifies = filtered.Count(c => c.HeureDebut != TimeSpan.Zero);
                
                return View(filtered);
            }

            return View(new List<Models.Cours>());
        }

        [HttpPost]
        public async Task<JsonResult> GenererPlanningAleatoire()
        {
            try
            {
                int? deptId = HttpContext.Session.GetInt32("DepartementId");
                if (deptId == null) return Json(new { success = false, message = "Session expirée" });

                var allCours = await _context.Cours.Where(c => c.DepartementId == deptId).ToListAsync();
                var allDispos = await _context.Disponibilites.ToListAsync();
                var allSalles = await _context.Salles.ToListAsync();

                if (!allCours.Any()) return Json(new { success = false, message = "Aucun cours n'est assigné à votre département. Veuillez d'abord créer des cours dans l'onglet Affectations." });
                if (!allSalles.Any()) return Json(new { success = false, message = "Aucune salle n'est enregistrée. Veuillez ajouter des salles avant de générer le planning." });

                // Reset
                foreach (var c in allCours) { c.Jour = 0; c.HeureDebut = TimeSpan.Zero; c.HeureFin = TimeSpan.Zero; }

                // 1. Priority Sorting (Difficulty Score)
                var sortedCours = allCours.Select(c => new {
                    Cours = c,
                    ProfCount = string.IsNullOrEmpty(c.ProfesseurIds) ? (c.UtilisateurId.HasValue ? 1 : 0) : c.ProfesseurIds.Split(',').Length,
                    TitleOverlapCount = allCours.Count(x => x.Titre == c.Titre)
                })
                .OrderByDescending(x => x.ProfCount)
                .ThenByDescending(x => x.TitleOverlapCount)
                .Select(x => x.Cours)
                .ToList();

                var random = new Random();
                var jours = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
                var slots = new[] { new TimeSpan(8, 0, 0), new TimeSpan(10, 0, 0), new TimeSpan(14, 0, 0), new TimeSpan(16, 0, 0) };

                int places = 0;
                foreach (var c in sortedCours)
                {
                    bool ok = false;
                    var shuffledJours = jours.OrderBy(x => random.Next()).ToList();
                    var shuffledSlots = slots.OrderBy(x => random.Next()).ToList();

                    // For multi-prof logic
                    var currentPids = string.IsNullOrEmpty(c.ProfesseurIds) 
                                      ? (c.UtilisateurId.HasValue ? new[] { c.UtilisateurId.Value.ToString() } : new string[0])
                                      : c.ProfesseurIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToArray();
                    
                    var currentGids = string.IsNullOrEmpty(c.GroupeIds) 
                                      ? new string[0] 
                                      : c.GroupeIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(g => g.Trim()).ToArray();

                    foreach (var j in shuffledJours)
                    {
                        var groupSlots = shuffledSlots.Select(s => new {
                            Slot = s,
                            IsAdjacent = allCours.Any(o => o.Id != c.Id && 
                                                           !string.IsNullOrEmpty(o.GroupeIds) && 
                                                           o.GroupeIds.Split(',').Intersect(currentGids).Any() && 
                                                           o.Jour == j && 
                                                           (o.HeureDebut == s.Add(new TimeSpan(2, 0, 0)) || o.HeureDebut == s.Add(new TimeSpan(-2, 0, 0))))
                        })
                        .OrderByDescending(x => x.IsAdjacent)
                        .Select(x => x.Slot)
                        .ToList();

                        foreach (var s in groupSlots)
                        {
                            var sFin = s.Add(new TimeSpan(2, 0, 0));

                            // 1. Teacher availability check (Empty = Absent)
                            // Every professor assigned to this course MUST have a 'Disponible=true' record covering the slot
                            bool allProfsAvailable = currentPids.All(pid => 
                                allDispos.Any(d => d.UtilisateurId.ToString() == pid && d.Jour == j && d.HeureDebut <= s && d.HeureFin >= sFin && d.Disponible == true) &&
                                !allDispos.Any(d => d.UtilisateurId.ToString() == pid && d.Jour == j && d.HeureDebut < sFin && d.HeureFin > s && d.Disponible == false)
                            );
                            if (!allProfsAvailable) continue;

                            // 2. Teacher overlap
                            bool teacherConflict = allCours.Any(o => o.Id != c.Id && o.Jour == j && o.HeureDebut == s && 
                                                   (
                                                        (o.UtilisateurId.HasValue && currentPids.Contains(o.UtilisateurId.Value.ToString())) ||
                                                        (!string.IsNullOrEmpty(o.ProfesseurIds) && o.ProfesseurIds.Split(',').Intersect(currentPids).Any())
                                                   ));
                            if (teacherConflict) continue;

                            // 3. Room Management (Automatic Assignment)
                            int? targetSalleId = null;
                            if (c.SalleId != null && !allCours.Any(o => o.Id != c.Id && o.SalleId == c.SalleId && o.Jour == j && o.HeureDebut == s))
                            {
                                targetSalleId = c.SalleId;
                            }
                            else
                            {
                                var availableSalles = allSalles
                                    .Where(r => !allCours.Any(o => o.Id != c.Id && o.SalleId == r.Id && o.Jour == j && o.HeureDebut == s))
                                    .ToList();

                                if (availableSalles.Any()) targetSalleId = availableSalles[random.Next(availableSalles.Count)].Id;
                                else continue; // No rooms left for this slot
                            }

                            // 4. Group overlap
                            bool groupConflict = allCours.Any(o => o.Id != c.Id && o.Jour == j && o.HeureDebut == s && 
                                                 !string.IsNullOrEmpty(o.GroupeIds) && 
                                                 o.GroupeIds.Split(',').Intersect(currentGids).Any());
                            if (groupConflict) continue;

                            // 5. Title capacity overlap (Simultaneous sessions only if multiple professors)
                            var simultaneousOfSameTitle = allCours.Where(o => o.Id != c.Id && o.Titre == c.Titre && o.Jour == j && o.HeureDebut == s).ToList();
                            if (simultaneousOfSameTitle.Count >= currentPids.Length) continue;

                            // Placement
                            c.Jour = j;
                            c.HeureDebut = s;
                            c.HeureFin = sFin;
                            c.SalleId = targetSalleId;
                            ok = true;
                            places++;
                            break;
                        }
                        if (ok) break;
                    }
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = $"{places}/{allCours.Count} cours placés." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Erreur : " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetCoursInfo(int id)
        {
            var cours = await _context.Cours
                .Include(c => c.Utilisateur)
                .Include(c => c.Salle)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (cours == null) return Json(new { success = false });

            var deptId = HttpContext.Session.GetInt32("DepartementId");
            var salles = await _context.Salles.ToListAsync();
            var profs = await _context.Utilisateurs.Where(u => u.Role == "Professeur" && u.DepartementId == deptId).ToListAsync();

            return Json(new { 
                success = true, 
                data = new {
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
            try
            {
                var cours = await _context.Cours.FindAsync(id);
                if (cours == null) return Json(new { success = false, message = "Cours introuvable" });

                var nHeureDebut = TimeSpan.Parse(heureDebut);
                var nHeureFin = nHeureDebut.Add(new TimeSpan(2, 0, 0));
                var nJour = (DayOfWeek)jour;

                var all = await _context.Cours.Where(c => c.DepartementId == cours.DepartementId).ToListAsync();
                var allDispos = await _context.Disponibilites.ToListAsync();

                // 1. Resolve Professors and Room
                int? finalSalleId = salleId ?? cours.SalleId;
                int? finalProfId = profId ?? cours.UtilisateurId;

                var currentPids = string.IsNullOrEmpty(cours.ProfesseurIds) 
                                  ? (finalProfId.HasValue ? new[] { finalProfId.Value.ToString() } : new string[0])
                                  : cours.ProfesseurIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToArray();

                // 2. Teacher availability check (Empty = Absent)
                if (currentPids.Length > 0)
                {
                    bool allProfsAvailable = currentPids.All(pid => 
                        allDispos.Any(d => d.UtilisateurId.ToString() == pid && d.Jour == nJour && d.HeureDebut <= nHeureDebut && d.HeureFin >= nHeureFin && d.Disponible == true) &&
                        !allDispos.Any(d => d.UtilisateurId.ToString() == pid && d.Jour == nJour && d.HeureDebut < nHeureFin && d.HeureFin > nHeureDebut && d.Disponible == false)
                    );
                    if (!allProfsAvailable) return Json(new { success = false, message = "Un des professeurs n'est pas disponible (ou n'a pas renseigné ce créneau)." });
                }

                // 3. Teacher overlap
                if (currentPids.Length > 0)
                {
                    bool conflict = all.Any(c => c.Id != id && c.Jour == nJour && c.HeureDebut == nHeureDebut && 
                        (
                            (c.UtilisateurId.HasValue && currentPids.Contains(c.UtilisateurId.Value.ToString())) ||
                            (!string.IsNullOrEmpty(c.ProfesseurIds) && c.ProfesseurIds.Split(',').Intersect(currentPids).Any())
                        ));
                    if (conflict) return Json(new { success = false, message = "Un des professeurs est déjà occupé." });
                }

                // 3. Room overlap
                if (finalSalleId != null && all.Any(c => c.Id != id && c.SalleId == finalSalleId && c.Jour == nJour && c.HeureDebut == nHeureDebut))
                    return Json(new { success = false, message = "La salle est déjà occupée." });

                // 4. Capacity check
                var simultaneousOfSameTitle = all.Where(c => c.Id != id && c.Titre == cours.Titre && c.Jour == nJour && c.HeureDebut == nHeureDebut).ToList();
                if (simultaneousOfSameTitle.Count >= currentPids.Length)
                    return Json(new { success = false, message = $"Ce cours ne peut pas avoir plus de {currentPids.Length} sessions simultanées." });

                // 5. Group overlap
                if (!string.IsNullOrEmpty(cours.GroupeIds))
                {
                    var gids = cours.GroupeIds.Split(',');
                    bool gConflict = all.Any(c => c.Id != id && c.Jour == nJour && c.HeureDebut == nHeureDebut && 
                                     !string.IsNullOrEmpty(c.GroupeIds) && c.GroupeIds.Split(',').Intersect(gids).Any());
                    if (gConflict) return Json(new { success = false, message = "Un des groupes est déjà en cours." });
                }

                // 6. Update
                cours.Jour = nJour;
                cours.HeureDebut = nHeureDebut;
                cours.HeureFin = nHeureFin;
                cours.SalleId = finalSalleId;
                
                // Only update UtilisateurId/ProfesseurIds if profId was explicitly provided (modal)
                if (profId.HasValue)
                {
                    cours.UtilisateurId = profId;
                    // If it's a simple change, keep ProfesseurIds in sync
                    if (string.IsNullOrEmpty(cours.ProfesseurIds) || !cours.ProfesseurIds.Contains(",")) 
                        cours.ProfesseurIds = profId.Value.ToString();
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }
    }
}