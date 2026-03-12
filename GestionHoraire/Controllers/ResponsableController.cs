using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GestionHoraire.Data;
using GestionHoraire.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Security.Cryptography;
using System.Text;

namespace GestionHoraire.Controllers
{
    public class ResponsableController : Controller
    {
        private readonly AppDbContext _context;

        public ResponsableController(AppDbContext context)
        {
            _context = context;
        }

        // Helper pour récupérer le département en session
        private int? GetMonDeptId() => HttpContext.Session.GetInt32("DepartementId");

        // 1. ACCUEIL
        public async Task<IActionResult> Index()
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Index", "Login");

            var user = await _context.Utilisateurs
                .Include(u => u.Departement)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null) return RedirectToAction("Logout", "Login");

            return View(user);
        }

        // 2. GESTION DES PROFESSEURS AVEC HASH
        public async Task<IActionResult> Profs()
        {
            int? deptId = GetMonDeptId();
            var profs = await _context.Utilisateurs
                .Where(u => u.Role == "Professeur" && u.DepartementId == deptId)
                .ToListAsync();
            return View(profs);
        }

        [HttpGet]
        public IActionResult CreerProf() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreerProf(Utilisateur prof, string password)
        {
            int? monDeptId = GetMonDeptId();
            prof.Role = "Professeur";
            prof.DepartementId = monDeptId;
            prof.DateCreation = DateTime.Now;
            prof.Disponibilite = true;

            // Forcer le changement au premier login
            prof.EstMotDePasseProvisoire = true;

            if (!string.IsNullOrEmpty(password))
            {
                // Hachage SHA256 identique au LoginController
                Guid salt = Guid.NewGuid();
                prof.MotDePasseSalt = salt;
                prof.MotDePasseHash = CalculerSHA256(password, salt);
            }

            // Nettoyage validation
            ModelState.Remove("Departement");
            ModelState.Remove("MotDePasseHash");
            ModelState.Remove("MotDePasseSalt");
            ModelState.Remove("Disponibilites");
            ModelState.Remove("Role");
            ModelState.Remove("DateCreation");

            if (ModelState.IsValid)
            {
                _context.Utilisateurs.Add(prof);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Profs));
            }
            return View(prof);
        }

        [HttpGet]
        public async Task<IActionResult> EditerProf(int id)
        {
            var prof = await _context.Utilisateurs.FindAsync(id);
            if (prof == null || prof.DepartementId != GetMonDeptId()) return NotFound();
            return View(prof);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditerProf(int id, Utilisateur model)
        {
            var profEnDb = await _context.Utilisateurs.FindAsync(id);
            if (profEnDb == null) return NotFound();

            profEnDb.Nom = model.Nom;
            profEnDb.Email = model.Email;

            ModelState.Remove("MotDePasseHash");
            ModelState.Remove("MotDePasseSalt");
            ModelState.Remove("Departement");
            ModelState.Remove("Role");
            ModelState.Remove("Disponibilites");

            if (ModelState.IsValid)
            {
                _context.Update(profEnDb);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Profs));
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SupprimerProf(int id)
        {
            var prof = await _context.Utilisateurs.FindAsync(id);
            if (prof != null && prof.DepartementId == GetMonDeptId())
            {
                _context.Utilisateurs.Remove(prof);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Profs));
        }

        // 3. GESTION DES COURS
        public async Task<IActionResult> Affectations()
        {
            int? monDeptId = GetMonDeptId();
            string userRole = HttpContext.Session.GetString("UserRole");

            IQueryable<Cours> query = _context.Cours.Include(c => c.Utilisateur);

            if (userRole == "Administrateur")
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
            return View(cours);
        }

        [HttpGet]
        public IActionResult CreerCours() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreerCours(Cours cours)
        {
            int? monDeptId = GetMonDeptId();
            if (monDeptId == null) return RedirectToAction("Index", "Login");

            cours.DepartementId = monDeptId.Value;
            cours.Jour = DayOfWeek.Monday;
            cours.HeureDebut = new TimeSpan(8, 0, 0);
            cours.HeureFin = new TimeSpan(10, 0, 0);

            ModelState.Remove("Departement");
            ModelState.Remove("Utilisateur");
            ModelState.Remove("Salle");

            if (ModelState.IsValid)
            {
                _context.Add(cours);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Affectations));
            }
            return View(cours);
        }

        // 4. GESTION DES DISPONIBILITÉS
        public async Task<IActionResult> VoirDispos(int id)
        {
            var prof = await _context.Utilisateurs.FindAsync(id);
            if (prof == null) return NotFound();

            var dispos = await _context.Disponibilites
                .Where(d => d.UtilisateurId == id)
                .OrderBy(d => d.Jour).ThenBy(d => d.HeureDebut)
                .ToListAsync();

            ViewBag.ProfNom = prof.Nom;
            ViewBag.ProfId = prof.Id;
            return View(dispos);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AjouterDispo(int profId, int jour, TimeSpan debut, TimeSpan fin)
        {
            var dispo = new Disponibilite
            {
                UtilisateurId = profId,
                Jour = (DayOfWeek)jour,
                HeureDebut = debut,
                HeureFin = fin,
                Disponible = true
            };

            ModelState.Remove("Utilisateur");
            if (ModelState.IsValid)
            {
                _context.Disponibilites.Add(dispo);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(VoirDispos), new { id = profId });
        }

        
        // OUTILS DE SÉCURITÉ (DOIT CORRESPONDRE AU LOGIN) sha256
        private static byte[] CalculerSHA256(string motDePasse, Guid saltGuid)
        {
            byte[] salt = saltGuid.ToByteArray();
            byte[] mdpBytes = Encoding.UTF8.GetBytes(motDePasse);
            byte[] input = new byte[salt.Length + mdpBytes.Length];
            Buffer.BlockCopy(salt, 0, input, 0, salt.Length);
            Buffer.BlockCopy(mdpBytes, 0, input, salt.Length, mdpBytes.Length);
            return SHA256.HashData(input);
        }
    }
}