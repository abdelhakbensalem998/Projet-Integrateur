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
    /// <summary>
    /// Contrôleur responsable de la gestion et de la génération de l'emploi du temps (Planning).
    /// </summary>
    public class PlanningController : Controller
    {
        private readonly AppDbContext _context;

        public PlanningController(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Méthode de maintenance automatique pour s'assurer que la base de données est compatible
        /// et que les cours à plusieurs groupes sont bien séparés pour le planning.
        /// </summary>
        private async Task AutoReparerSchema()
        {
            try
            {
                // 1. On vérifie si la colonne GroupeIds existe en PostgreSQL, sinon on l'ajoute.
                await _context.Database.ExecuteSqlRawAsync(@"
                    DO $$
                    BEGIN
                        IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Cours' AND column_name = 'GroupeIds') THEN
                            ALTER TABLE ""Cours"" ADD COLUMN ""GroupeIds"" TEXT NULL;
                        END IF;
                    END $$;
                ");

                // 2. Traitement des cours multi-groupes : on les sépare en enregistrements distincts.
                // Exemple : Un cours pour "G1, G2" devient deux lignes indépendantes.
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
                            // On clone le cours original pour chaque groupe supplémentaire
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
                        mc.GroupeIds = gids[0].Trim(); // Le cours original garde seulement le 1er groupe
                    }
                    await _context.SaveChangesAsync();
                }
            }
            catch { }
        }

        /// <summary>
        /// Affiche l'emploi du temps pour un groupe sélectionné.
        /// </summary>
        /// <param name="groupeId">ID du groupe d'étudiants</param>
        public async Task<IActionResult> Index(int? groupeId)
        {
            await AutoReparerSchema();
            
            // Vérification de la session
            int? deptId = HttpContext.Session.GetInt32("DepartementId");
            if (deptId == null) return RedirectToAction("Login", "Account");

            string userRole = HttpContext.Session.GetString("UserRole");
            List<Models.Groupe> groupes;

            // Chargement des groupes selon le rôle (Admin voit tout, Responsable voit son dept)
            if (userRole == "Administrateur")
                groupes = await _context.Groupes.ToListAsync();
            else
                groupes = await _context.Groupes.Where(g => g.DepartementId == deptId).ToListAsync();

            // Envoi des infos à la vue pour les listes déroulantes
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

                // On filtre les cours : seulement ceux qui concernent le groupe choisi
                var filtered = allCours
                    .Where(c => !string.IsNullOrEmpty(c.GroupeIds) && 
                                c.GroupeIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                 .Select(s => s.Trim())
                                 .Contains(targetId))
                    .OrderBy(c => c.Jour)
                    .ThenBy(c => c.HeureDebut)
                    .ToList();

                // Données de statistiques pour le haut de la page
                ViewBag.TotalCoursDuGroupe = filtered.Count;
                ViewBag.CoursPlanifies = filtered.Count(c => c.HeureDebut != TimeSpan.Zero);
                
                return View(filtered);
            }

            return View(new List<Models.Cours>());
        }

        /// <summary>
        /// Algorithme de génération automatique du planning.
        /// Cherche à placer chaque cours dans un créneau libre sans conflit.
        /// </summary>
        [HttpPost]
        public async Task<JsonResult> GenererPlanningAleatoire()
        {
            try
            {
                int? deptId = HttpContext.Session.GetInt32("DepartementId");
                if (deptId == null) return Json(new { success = false, message = "Session expirée" });

                // Chargement de toutes les ressources nécessaires
                var allCours = await _context.Cours.Where(c => c.DepartementId == deptId).ToListAsync();
                var allDispos = await _context.Disponibilites.ToListAsync();
                var allSalles = await _context.Salles.ToListAsync();

                if (!allCours.Any()) return Json(new { success = false, message = "Aucun cours n'est assigné à votre département. Veuillez d'abord créer des cours dans l'onglet Affectations." });
                if (!allSalles.Any()) return Json(new { success = false, message = "Aucune salle n'est enregistrée. Veuillez ajouter des salles avant de générer le planning." });

                // Étape 1 : Réinitialisation du planning actuel
                foreach (var c in allCours) { c.Jour = 0; c.HeureDebut = TimeSpan.Zero; c.HeureFin = TimeSpan.Zero; }

                // Étape 2 : Tri par difficulté (heuristique)
                // On place d'abord les cours qui ont le plus d'intervenants
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
                // Étape 3 : Tentative de placement pour chaque cours
                foreach (var c in sortedCours)
                {
                    bool ok = false;
                    var shuffledJours = jours.OrderBy(x => random.Next()).ToList();
                    var shuffledSlots = slots.OrderBy(x => random.Next()).ToList();

                    // Récupération des IDs des profs et des groupes impliqués
                    var currentPids = string.IsNullOrEmpty(c.ProfesseurIds) 
                                      ? (c.UtilisateurId.HasValue ? new[] { c.UtilisateurId.Value.ToString() } : new string[0])
                                      : c.ProfesseurIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToArray();
                    
                    var currentGids = string.IsNullOrEmpty(c.GroupeIds) 
                                      ? new string[0] 
                                      : c.GroupeIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(g => g.Trim()).ToArray();

                    foreach (var j in shuffledJours)
                    {
                        // Stratégie d'adjacence : on essaie de grouper les cours des étudiants
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

                            // A. Vérification de la disponibilité des professeurs
                            bool allProfsAvailable = currentPids.All(pid => 
                                allDispos.Any(d => d.UtilisateurId.ToString() == pid && d.Jour == j && d.HeureDebut <= s && d.HeureFin >= sFin && d.Disponible == true) &&
                                !allDispos.Any(d => d.UtilisateurId.ToString() == pid && d.Jour == j && d.HeureDebut < sFin && d.HeureFin > s && d.Disponible == false)
                            );
                            if (!allProfsAvailable) continue;

                            // B. Vérification des conflits de professeurs (Déjà en cours)
                            bool teacherConflict = allCours.Any(o => o.Id != c.Id && o.Jour == j && o.HeureDebut == s && 
                                                   (
                                                        (o.UtilisateurId.HasValue && currentPids.Contains(o.UtilisateurId.Value.ToString())) ||
                                                        (!string.IsNullOrEmpty(o.ProfesseurIds) && o.ProfesseurIds.Split(',').Intersect(currentPids).Any())
                                                   ));
                            if (teacherConflict) continue;

                            // C. Gestion des Salles (Assignation automatique)
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
                                else continue; // Pas de salle libre pour ce créneau
                            }

                            // D. Vérification du conflit de groupe (Les étudiants ont déjà un cours)
                            bool groupConflict = allCours.Any(o => o.Id != c.Id && o.Jour == j && o.HeureDebut == s && 
                                                 !string.IsNullOrEmpty(o.GroupeIds) && 
                                                 o.GroupeIds.Split(',').Intersect(currentGids).Any());
                            if (groupConflict) continue;

                            // Validation finale : Placement
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

        /// <summary>
        /// Met à jour manuellement la position d'un cours (Drag & Drop).
        /// Refait toutes les validations de conflits avant d'accepter.
        /// </summary>
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

                // 1. Résolution de la salle et du professeur
                int? finalSalleId = salleId ?? cours.SalleId;
                int? finalProfId = profId ?? cours.UtilisateurId;

                var currentPids = string.IsNullOrEmpty(cours.ProfesseurIds) 
                                  ? (finalProfId.HasValue ? new[] { finalProfId.Value.ToString() } : new string[0])
                                  : cours.ProfesseurIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToArray();

                // 2. Vérification de la disponibilité du professeur
                if (currentPids.Length > 0)
                {
                    bool allProfsAvailable = currentPids.All(pid => 
                        allDispos.Any(d => d.UtilisateurId.ToString() == pid && d.Jour == nJour && d.HeureDebut <= nHeureDebut && d.HeureFin >= nHeureFin && d.Disponible == true) &&
                        !allDispos.Any(d => d.UtilisateurId.ToString() == pid && d.Jour == nJour && d.HeureDebut < nHeureFin && d.HeureFin > nHeureDebut && d.Disponible == false)
                    );
                    if (!allProfsAvailable) return Json(new { success = false, message = "Un des professeurs n'est pas disponible pour ce créneau." });
                }

                // 3. Vérification des conflits professeurs (Déjà occupé ailleurs)
                if (currentPids.Length > 0)
                {
                    bool conflict = all.Any(c => c.Id != id && c.Jour == nJour && c.HeureDebut == nHeureDebut && 
                        (
                            (c.UtilisateurId.HasValue && currentPids.Contains(c.UtilisateurId.Value.ToString())) ||
                            (!string.IsNullOrEmpty(c.ProfesseurIds) && c.ProfesseurIds.Split(',').Intersect(currentPids).Any())
                        ));
                    if (conflict) return Json(new { success = false, message = "L'intervenant sélectionné est déjà occupé sur un autre cours." });
                }

                // 4. Vérification de la salle
                if (finalSalleId != null && all.Any(c => c.Id != id && c.SalleId == finalSalleId && c.Jour == nJour && c.HeureDebut == nHeureDebut))
                    return Json(new { success = false, message = "La salle est déjà occupée." });

                // 5. Vérification du groupe (Les étudiants ont déjà cours)
                if (!string.IsNullOrEmpty(cours.GroupeIds))
                {
                    var gids = cours.GroupeIds.Split(',');
                    bool gConflict = all.Any(c => c.Id != id && c.Jour == nJour && c.HeureDebut == nHeureDebut && 
                                     !string.IsNullOrEmpty(c.GroupeIds) && c.GroupeIds.Split(',').Intersect(gids).Any());
                    if (gConflict) return Json(new { success = false, message = "Les étudiants du groupe ont déjà un autre cours prévu à cette heure." });
                }

                // Mise à jour effective
                cours.Jour = nJour;
                cours.HeureDebut = nHeureDebut;
                cours.HeureFin = nHeureFin;
                cours.SalleId = finalSalleId;
                
                if (profId.HasValue)
                {
                    cours.UtilisateurId = profId;
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