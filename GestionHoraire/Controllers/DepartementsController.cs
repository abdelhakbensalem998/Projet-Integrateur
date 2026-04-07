using System;
using System.Threading.Tasks;
using GestionHoraire.Data;
using GestionHoraire.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionHoraire.Controllers
{
    public class DepartementsController : Controller
    {
        private readonly AppDbContext _context;

        public DepartementsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: /Departements/
        public async Task<IActionResult> Index()
        {
            var departements = await _context.Departements.ToListAsync();
            return View(departements);
        }

        // GET: /Departements/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: /Departements/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,Nom")] Departement departement)
        {
            departement.Nom = departement.Nom?.Trim();
            await ValidateUniqueNameAsync(departement);

            if (ModelState.IsValid)
            {
                _context.Add(departement);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Département ajouté avec succès.";
                return RedirectToAction(nameof(Index));
            }

            return View(departement);
        }

        // GET: /Departements/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var departement = await _context.Departements.FindAsync(id);
            if (departement == null)
            {
                return NotFound();
            }

            return View(departement);
        }

        // POST: /Departements/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Nom")] Departement departement)
        {
            if (id != departement.Id)
            {
                return NotFound();
            }

            departement.Nom = departement.Nom?.Trim();
            await ValidateUniqueNameAsync(departement, departement.Id);

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(departement);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Département modifié avec succès.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!DepartementExists(departement.Id))
                    {
                        return NotFound();
                    }

                    throw;
                }

                return RedirectToAction(nameof(Index));
            }

            return View(departement);
        }

        // GET: /Departements/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var departement = await _context.Departements
                .FirstOrDefaultAsync(m => m.Id == id);
            if (departement == null)
            {
                return NotFound();
            }

            return View(departement);
        }

        // POST: /Departements/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var departement = await _context.Departements.FindAsync(id);
            if (departement != null)
            {
                _context.Departements.Remove(departement);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Département supprimé avec succès.";
            }

            return RedirectToAction(nameof(Index));
        }

        private bool DepartementExists(int id)
        {
            return _context.Departements.Any(e => e.Id == id);
        }

        private async Task ValidateUniqueNameAsync(Departement departement, int? excludedId = null)
        {
            if (string.IsNullOrWhiteSpace(departement.Nom))
            {
                ModelState.AddModelError(nameof(Departement.Nom), "Le nom du département est obligatoire.");
                return;
            }

            var nomNormalise = departement.Nom.Trim().ToUpper();

            var duplicateExists = await _context.Departements.AnyAsync(d =>
                d.Id != excludedId &&
                d.Nom != null &&
                d.Nom.Trim().ToUpper() == nomNormalise);

            if (duplicateExists)
            {
                ModelState.AddModelError(nameof(Departement.Nom), "Un département avec ce nom existe déjà.");
            }
        }
    }
}
