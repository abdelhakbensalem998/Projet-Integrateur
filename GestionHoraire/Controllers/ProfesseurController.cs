using GestionHoraire.Data;
using GestionHoraire.Models;
using GestionHoraire.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;

namespace GestionHoraire.Controllers
{
    public class ProfesseurController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly SchemaRepairService _schemaRepairService;

        public ProfesseurController(AppDbContext context, IWebHostEnvironment env, SchemaRepairService schemaRepairService)
        {
            _context = context;
            _env = env;
            _schemaRepairService = schemaRepairService;
        }

        private int? GetCurrentUserId() => HttpContext.Session.GetInt32("UserId");

        // 1. DASHBOARD CENTRAL (GRID INTERACTIF)
        public async Task<IActionResult> Index()
        {
            await _schemaRepairService.EnsureCoursSchemaAsync();
            await _schemaRepairService.EnsureDemandesSchemaAsync();

            int? userId = GetCurrentUserId();
            if (userId == null) return RedirectToAction("Index", "Login");

            var prof = await _context.Utilisateurs
                .Include(u => u.Departement)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (prof == null) return RedirectToAction("Logout", "Login");

            // LOGIQUE DE RÉCUPÉRATION COURS (Multi-Professeurs support)
            var targetId = userId.Value.ToString();
            var tousDeptsCours = await _context.Cours
                .Include(c => c.Salle)
                .Where(c => c.DepartementId == prof.DepartementId)
                .ToListAsync();

            var mesCours = tousDeptsCours
                .Where(c => 
                    c.UtilisateurId == userId || 
                    (!string.IsNullOrEmpty(c.ProfesseurIds) && 
                     c.ProfesseurIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(p => p.Trim())
                                    .Contains(targetId)))
                .OrderBy(c => c.Jour)
                .ThenBy(c => c.HeureDebut)
                .ToList();

            // RÉCUPÉRATION DISPONIBILITÉS
            var mesDispos = await _context.Disponibilites
                .Where(d => d.UtilisateurId == userId)
                .ToListAsync();

            // RÉCUPÉRATION DERNIÈRES DEMANDES (HISTORIQUE)
            var mesDemandes = await _context.Demandes
                .Where(d => d.UtilisateurId == userId)
                .OrderByDescending(d => d.DateCreation)
                .Take(5)
                .ToListAsync();

            ViewBag.UserNom = prof.Nom;
            ViewBag.UserRole = prof.Role;
            ViewBag.MesDisponibilites = mesDispos;
            ViewBag.MesDemandes = mesDemandes;

            return View(mesCours);
        }

        // 2. CENTRE DE DEMANDES (SIGNALEMENT D'ABSENCE / TECHNIQUE)
        [HttpGet]
        public IActionResult EnvoyerDemande() => View(new Demande());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EnvoyerDemande(Demande demande)
        {
            await _schemaRepairService.EnsureDemandesSchemaAsync();

            int? userId = GetCurrentUserId();
            if (userId == null) return RedirectToAction("Index", "Login");

            // On sature les données automatiques
            demande.UtilisateurId = userId.Value;
            demande.DateCreation = DateTime.Now;
            demande.Statut = "En attente";

            if (demande.Type == "Absence") demande.EstUrgent = true;

            _context.Demandes.Add(demande);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Votre demande a été transmise au responsable ! 🚀";
            return RedirectToAction(nameof(Index));
        }

        // 3. DÉPÔT DE PLAN DE COURS
        [HttpGet]
        public IActionResult DeposerPlanCours() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeposerPlanCours(string description, IFormFile fichier)
        {
            await _schemaRepairService.EnsureDemandesSchemaAsync();

            int? userId = GetCurrentUserId();
            if (userId == null) return RedirectToAction("Index", "Login");

            if (fichier == null || fichier.Length == 0)
            {
                ModelState.AddModelError("", "Le document est obligatoire.");
                return View();
            }

            // Define upload path using IWebHostEnvironment
            var uploadPath = Path.Combine(_env.WebRootPath, "uploads");
            if (!Directory.Exists(uploadPath)) Directory.CreateDirectory(uploadPath);

            // Unique filename to avoid collisions
            var uniqueFileName = Guid.NewGuid().ToString() + "_" + fichier.FileName;
            var filePath = Path.Combine(uploadPath, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await fichier.CopyToAsync(stream);
            }

            var demande = new Demande
            {
                UtilisateurId = userId.Value,
                Type = "Plan de cours",
                Description = description ?? "Dépôt plan de cours",
                DateCreation = DateTime.Now,
                Statut = "En attente",
                FichierJoint = uniqueFileName // Store the unique name
            };

            _context.Demandes.Add(demande);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Plan de cours déposé avec succès !";
            return RedirectToAction(nameof(Index));
        }

        // 4. HISTORIQUE COMPLET DES DEMANDES
        public async Task<IActionResult> MesDemandes()
        {
            await _schemaRepairService.EnsureDemandesSchemaAsync();

            int? userId = GetCurrentUserId();
            if (userId == null) return RedirectToAction("Index", "Login");

            var history = await _context.Demandes
                .Where(d => d.UtilisateurId == userId)
                .OrderByDescending(d => d.DateCreation)
                .ToListAsync();

            return View(history);
        }
    }
}
