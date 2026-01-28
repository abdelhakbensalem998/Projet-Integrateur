using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GestionHoraire.Data;
using GestionHoraire.Models;

namespace GestionHoraire.Controllers
{
    public class DisponibiliteController : Controller
    {
        private readonly AppDbContext _context;
        public DisponibiliteController(AppDbContext context)
        {
            _context = context;
        }

        // GET: Liste des disponibilités pour un professeur
        public IActionResult Index(int? professeurId)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            string userRole = HttpContext.Session.GetString("UserRole");

            if (userRole == "Professeur")
            {
                professeurId = userId; // prof ne peut voir que ses propres disponibilités
            }
            else if (professeurId == null)
            {
                return BadRequest("Id professeur requis pour le responsable.");
            }

            var disponibilites = _context.Disponibilites
                .Include(d => d.Utilisateur)
                .Where(d => d.UtilisateurId == professeurId)
                .OrderBy(d => d.Jour).ThenBy(d => d.HeureDebut)
                .ToList();

            return View(disponibilites);
        }

        // GET: Modifier une disponibilité
        public IActionResult Edit(int id)
        {
            var dispo = _context.Disponibilites.Find(id);
            if (dispo == null) return NotFound();

            int? userId = HttpContext.Session.GetInt32("UserId");
            string userRole = HttpContext.Session.GetString("UserRole");

            if (userRole == "Professeur" && dispo.UtilisateurId != userId)
                return Forbid(); // prof ne peut modifier que ses propres dispo

            return View(dispo);
        }

        // POST: Modifier disponibilité
        [HttpPost]
        public IActionResult Edit(Disponibilite model)
        {
            if (!ModelState.IsValid) return View(model);

            var dispo = _context.Disponibilites.Find(model.Id);
            if (dispo == null) return NotFound();

            int? userId = HttpContext.Session.GetInt32("UserId");
            string userRole = HttpContext.Session.GetString("UserRole");

            if (userRole == "Professeur" && dispo.UtilisateurId != userId)
                return Forbid();

            dispo.Jour = model.Jour;
            dispo.HeureDebut = model.HeureDebut;
            dispo.HeureFin = model.HeureFin;
            dispo.Disponible = model.Disponible;

            _context.SaveChanges();
            TempData["Success"] = "Disponibilité mise à jour avec succès !";

            return RedirectToAction("Index", new { professeurId = dispo.UtilisateurId });
        }
    }
}

