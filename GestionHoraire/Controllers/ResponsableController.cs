using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GestionHoraire.Data;
using GestionHoraire.Models;
using Microsoft.AspNetCore.Http;
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

        private int? GetMonDeptId() => HttpContext.Session.GetInt32("DepartementId");

        // 1. ACCUEIL (Tableau de bord sans les Salles)
        public async Task<IActionResult> Index()
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            int? deptId = GetMonDeptId();

            if (userId == null || deptId == null) return RedirectToAction("Index", "Login");

            var user = await _context.Utilisateurs
                .Include(u => u.Departement)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null) return RedirectToAction("Logout", "Login");

            // Compteurs pour les badges
            ViewBag.NbProfesseurs = await _context.Utilisateurs
                .CountAsync(u => u.Role == "Professeur" && u.DepartementId == deptId);

            ViewBag.NbGroupes = await _context.Groupes
                .CountAsync(g => g.DepartementId == deptId);

            return View(user);
        }

        // 2. GESTION DES PROFESSEURS
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
            prof.Role = "Professeur";
            prof.DepartementId = GetMonDeptId();
            prof.DateCreation = DateTime.Now;
            prof.Disponibilite = true;
            prof.EstMotDePasseProvisoire = true;

            if (!string.IsNullOrEmpty(password))
            {
                Guid salt = Guid.NewGuid();
                prof.MotDePasseSalt = salt;
                prof.MotDePasseHash = CalculerSHA256(password, salt);
            }

            ModelState.Remove("Departement");
            ModelState.Remove("MotDePasseHash");
            ModelState.Remove("MotDePasseSalt");
            ModelState.Remove("Role");

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
            if (prof == null || prof.DepartementId != GetMonDeptId())
            {
                return NotFound();
            }
            return View(prof);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditerProf(int id, [Bind("Id,Nom,Email")] Utilisateur prof)
        {
            if (id != prof.Id) return NotFound();

            var existingProf = await _context.Utilisateurs.FindAsync(id);
            if (existingProf == null || existingProf.DepartementId != GetMonDeptId())
            {
                return NotFound();
            }

            existingProf.Nom = prof.Nom;
            existingProf.Email = prof.Email;

            // Remove non-edited fields from validation
            ModelState.Remove("MotDePasseHash");
            ModelState.Remove("MotDePasseSalt");
            ModelState.Remove("Role");
            ModelState.Remove("Departement");

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(existingProf);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ProfExists(prof.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Profs));
            }
            return View(prof);
        }

        private bool ProfExists(int id) => _context.Utilisateurs.Any(e => e.Id == id);

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

        // 3. AFFECTATIONS ET COURS
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

        // 4. DISPONIBILITÉS
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