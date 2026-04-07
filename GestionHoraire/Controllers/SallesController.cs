using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GestionHoraire.Data;
using GestionHoraire.Models;

namespace GestionHoraire.Controllers
{
    public class SallesController : Controller
    {
        private readonly AppDbContext _context;

        public SallesController(AppDbContext context)
        {
            _context = context;
        }

        // 1. Liste des salles
        public async Task<IActionResult> Index()
        {
            var salles = await _context.Salles.OrderBy(s => s.Nom).ToListAsync();
            return View(salles);
        }

        // 2. Afficher formulaire Ajout (GET)
        public IActionResult Ajouter()
        {
            return View();
        }

        // 3. Traiter l'Ajout (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Ajouter([Bind("Nom,Capacite,Type")] Salle salle)
        {
            if (ModelState.IsValid)
            {
                _context.Add(salle);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Salle ajoutee avec succes.";
                return RedirectToAction(nameof(Index));
            }
            return View(salle);
        }

        // 4. Afficher formulaire Modification (GET)
        public async Task<IActionResult> Modifier(int? id)
        {
            if (id == null) return NotFound();
            var salle = await _context.Salles.FindAsync(id);
            if (salle == null) return NotFound();
            return View(salle);
        }

        // 5. Traiter la Modification (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Modifier(int id, [Bind("Id,Nom,Capacite,Type")] Salle salle)
        {
            if (id != salle.Id) return NotFound();
            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(salle);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Salle modifiee avec succes.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Salles.Any(e => e.Id == salle.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(salle);
        }

        // 6. Supprimer une salle
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Supprimer(int id)
        {
            var salle = await _context.Salles.FindAsync(id);
            if (salle != null)
            {
                _context.Salles.Remove(salle);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Salle supprimee avec succes.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}
