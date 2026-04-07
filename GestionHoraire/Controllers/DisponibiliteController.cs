using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using GestionHoraire.Data;
using GestionHoraire.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace GestionHoraire.Controllers
{
    public class DisponibiliteController : Controller
    {
        private readonly AppDbContext _context;

        public DisponibiliteController(AppDbContext context)
        {
            _context = context;
        }

        // GET: /Disponibilite/Index?professeurId=1&returnUrl=/Responsable/Profs
        public async Task<IActionResult> Index(int? professeurId, string? returnUrl)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            string userRole = HttpContext.Session.GetString("UserRole") ?? "";

            if (userRole == "Professeur")
            {
                professeurId = userId; // prof ne peut voir que ses propres dispos
            }
            else if (professeurId == null && userRole != "Professeur")
            {
                return BadRequest("ID professeur requis.");
            }

            if (!professeurId.HasValue)
                return NotFound();

            var disponibilites = await _context.Disponibilites
                .Include(d => d.Utilisateur)
                .Where(d => d.UtilisateurId == professeurId)
                .OrderBy(d => d.Jour).ThenBy(d => d.HeureDebut)
                .ToListAsync();

            ViewBag.ProfesseurId = professeurId.Value;
            ViewBag.ProfesseurNom = _context.Utilisateurs
                .Where(u => u.Id == professeurId)
                .Select(u => u.Nom)
                .FirstOrDefault() ?? "Utilisateur";
            ViewBag.UserRole = userRole;
            ViewBag.ReturnUrl = returnUrl;

            return View(disponibilites);
        }

        // GET: /Disponibilite/Create?professeurId=1
        public IActionResult Create(int? professeurId, int? jour, string heure, string? returnUrl)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            string userRole = HttpContext.Session.GetString("UserRole") ?? "";
 
            if (userRole == "Professeur") professeurId = userId;
            
            if (userRole != "ResponsableDépartement" && (professeurId == null || professeurId != userId))
                return RedirectToAction("Index", "Login");

            var model = new Disponibilite 
            { 
                UtilisateurId = professeurId.Value,
                Jour = (DayOfWeek)(jour ?? 1),
                Disponible = true
            };

            if (!string.IsNullOrEmpty(heure) && TimeSpan.TryParse(heure, out var ts))
            {
                model.HeureDebut = ts;
                model.HeureFin = ts.Add(new TimeSpan(2, 0, 0));
            }

            ViewBag.ProfesseurId = professeurId;
            ViewBag.ReturnUrl = returnUrl;
            return View(model);
        }

        // POST: /Disponibilite/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Disponibilite model, string? returnUrl)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.ProfesseurId = model.UtilisateurId;
                ViewBag.ReturnUrl = returnUrl;
                return View(model);
            }

            int? userId = HttpContext.Session.GetInt32("UserId");
            string userRole = HttpContext.Session.GetString("UserRole") ?? "";

            if (userRole != "ResponsableDépartement" && model.UtilisateurId != userId)
                return RedirectToAction("Index", "Login");

            // Vérifier doublon créneau
            var existe = await _context.Disponibilites
                .AnyAsync(d => d.UtilisateurId == model.UtilisateurId
                            && d.Jour == model.Jour
                            && d.HeureDebut == model.HeureDebut
                            && d.HeureFin == model.HeureFin);

            if (existe)
            {
                ModelState.AddModelError("", "Ce créneau existe déjà.");
                ViewBag.ProfesseurId = model.UtilisateurId;
                ViewBag.ReturnUrl = returnUrl;
                return View(model);
            }

            _context.Disponibilites.Add(model);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Créneau ajouté ! ✅";

            return RedirectToAction(nameof(Index), new { professeurId = model.UtilisateurId, returnUrl = returnUrl });
        }

        // GET: /Disponibilite/Edit/5
        public IActionResult Edit(int id, string? returnUrl)
        {
            var dispo = _context.Disponibilites.Find(id);
            if (dispo == null) return NotFound();

            int? userId = HttpContext.Session.GetInt32("UserId");
            string userRole = HttpContext.Session.GetString("UserRole") ?? "";

            if (userRole == "Professeur" && dispo.UtilisateurId != userId)
                return RedirectToAction("Index", "Login");

            ViewBag.ProfesseurId = dispo.UtilisateurId;
            ViewBag.ReturnUrl = returnUrl;
            return View("create", dispo);
        }

        // POST: /Disponibilite/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Disponibilite model, string? returnUrl)
        {
            if (id != model.Id) return NotFound();

            if (!ModelState.IsValid)
            {
                ViewBag.ProfesseurId = model.UtilisateurId;
                ViewBag.ReturnUrl = returnUrl;
                return View(model);
            }

            var dispo = await _context.Disponibilites.FindAsync(id);
            if (dispo == null) return NotFound();

            int? userId = HttpContext.Session.GetInt32("UserId");
            string userRole = HttpContext.Session.GetString("UserRole") ?? "";

            if (userRole == "Professeur" && dispo.UtilisateurId != userId)
                return RedirectToAction("Index", "Login");

            dispo.Jour = model.Jour;
            dispo.HeureDebut = model.HeureDebut;
            dispo.HeureFin = model.HeureFin;
            dispo.Disponible = model.Disponible;

            await _context.SaveChangesAsync();
            TempData["Success"] = "Disponibilité mise à jour ! ✅";

            return RedirectToAction(nameof(Index), new { professeurId = dispo.UtilisateurId, returnUrl = returnUrl });
        }

        // GET: /Disponibilite/Delete/5
        public async Task<IActionResult> Delete(int id, string? returnUrl)
        {
            var dispo = await _context.Disponibilites
                .Include(d => d.Utilisateur)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (dispo == null) return NotFound();

            int? userId = HttpContext.Session.GetInt32("UserId");
            string userRole = HttpContext.Session.GetString("UserRole") ?? "";

            if (userRole != "ResponsableDépartement" && dispo.UtilisateurId != userId)
                return RedirectToAction("Index", "Login");

            ViewBag.ReturnUrl = returnUrl;
            return View(dispo);
        }

        // POST: /Disponibilite/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id, string? returnUrl)
        {
            var dispo = await _context.Disponibilites.FindAsync(id);
            if (dispo == null) return NotFound();

            int? userId = HttpContext.Session.GetInt32("UserId");
            string userRole = HttpContext.Session.GetString("UserRole") ?? "";

            if (userRole != "ResponsableDépartement" && dispo.UtilisateurId != userId)
                return RedirectToAction("Index", "Login");

            int profId = dispo.UtilisateurId;
            _context.Disponibilites.Remove(dispo);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Créneau supprimé ! ✅";

            return RedirectToAction(nameof(Index), new { professeurId = profId, returnUrl = returnUrl });
        }

        // POST: Générer dispos par défaut (L-V 8h-18h)
        [HttpPost]
        public async Task<IActionResult> GenererDefaut(int professeurId, string? returnUrl)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            string userRole = HttpContext.Session.GetString("UserRole") ?? "";

            if (userRole != "ResponsableDépartement" && professeurId != userId)
                return RedirectToAction("Index", "Login");

            var jours = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                              DayOfWeek.Thursday, DayOfWeek.Friday };

            foreach (var jour in jours)
            {
                var heures = new[] {
                    (new TimeSpan(8, 0, 0), new TimeSpan(10, 0, 0)),
                    (new TimeSpan(10, 0, 0), new TimeSpan(12, 0, 0)),
                    (new TimeSpan(12, 0, 0), new TimeSpan(14, 0, 0)),
                    (new TimeSpan(14, 0, 0), new TimeSpan(16, 0, 0)),
                    (new TimeSpan(16, 0, 0), new TimeSpan(18, 0, 0))
                };

                foreach (var (debut, fin) in heures)
                {
                    if (!await _context.Disponibilites
                        .AnyAsync(d => d.UtilisateurId == professeurId && d.Jour == jour
                                     && d.HeureDebut == debut && d.HeureFin == fin))
                    {
                        _context.Disponibilites.Add(new Disponibilite
                        {
                            UtilisateurId = professeurId,
                            Jour = jour,
                            HeureDebut = debut,
                            HeureFin = fin,
                            Disponible = true
                        });
                    }
                }
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = "Disponibilités par défaut générées ! ✅";
            return RedirectToAction(nameof(Index), new { professeurId, returnUrl = returnUrl });
        }

    }
}


