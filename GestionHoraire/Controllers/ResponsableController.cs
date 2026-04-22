using GestionHoraire.Data;
using GestionHoraire.Models;
using GestionHoraire.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace GestionHoraire.Controllers
{
    public class ResponsableController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;

        public ResponsableController(AppDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        private int? GetDeptId() => HttpContext.Session.GetInt32("DepartementId");
        private int? GetUserId() => HttpContext.Session.GetInt32("UserId");

        // =========================
        // DASHBOARD RESPONSABLE
        // =========================
        public async Task<IActionResult> Index()
        {
            var userId = GetUserId();
            var deptId = GetDeptId();
            if (userId == null || deptId == null) return RedirectToAction("Index", "Login");

            var responsable = await _context.Utilisateurs
                .Include(u => u.Departement)
                .FirstOrDefaultAsync(u => u.Id == userId.Value);

            if (responsable == null) return RedirectToAction("Logout", "Login");

            ViewBag.NbProfesseurs = await _context.Utilisateurs
                .CountAsync(u => u.Role == "Professeur" && u.DepartementId == deptId.Value);

            ViewBag.NbGroupes = await _context.Groupes
                .CountAsync(g => g.DepartementId == deptId.Value);

            ViewBag.NbSalles = await _context.Salles.CountAsync();

            ViewBag.NbDemandesEnAttente = await _context.Demandes
                .CountAsync(d => d.Statut == "En attente" && d.Utilisateur.DepartementId == deptId);

            ViewBag.NbDemandesUrgentes = await _context.Demandes
                .CountAsync(d => d.Statut == "En attente" && d.EstUrgent && d.Utilisateur.DepartementId == deptId);

            // Stats pour les rapports
            var tousCours = await _context.Cours.Where(c => c.DepartementId == deptId).ToListAsync();
            var totalCours = tousCours.Count;
            var coursAssignes = tousCours.Count(c => c.UtilisateurId != null || !string.IsNullOrEmpty(c.ProfesseurIds));
            
            ViewBag.TauxAssignation = totalCours > 0 ? (int)((double)coursAssignes / totalCours * 100) : 0;
            ViewBag.NbCoursTotal = totalCours;

            // Calcul des conflits
            int conflits = 0;
            for (int i = 0; i < tousCours.Count; i++)
            {
                for (int j = i + 1; j < tousCours.Count; j++)
                {
                    var c1 = tousCours[i];
                    var c2 = tousCours[j];
                    if (c1.Jour == c2.Jour && c1.HeureDebut < c2.HeureFin && c2.HeureDebut < c1.HeureFin)
                    {
                        if ((c1.UtilisateurId != null && c1.UtilisateurId == c2.UtilisateurId) ||
                            (c1.SalleId != null && c1.SalleId == c2.SalleId))
                        {
                            conflits++;
                        }
                    }
                }
            }
            ViewBag.NbConflits = conflits;

            // Surcharge
            var profIds = tousCours.Where(c => c.UtilisateurId != null).Select(c => (int)c.UtilisateurId!).Distinct();
            int profsSurcharges = 0;
            foreach (var pId in profIds)
            {
                var heures = tousCours.Where(c => c.UtilisateurId == pId).Sum(c => (c.HeureFin - c.HeureDebut).TotalHours);
                if (heures > 18) profsSurcharges++;
            }
            ViewBag.NbSurcharges = profsSurcharges;

            return View(responsable);
        }

        // GESTION DES DEMANDES
        public async Task<IActionResult> Demandes(bool voirArchives = false)
        {
            int? deptId = GetDeptId();
            var query = _context.Demandes
                .Include(d => d.Utilisateur)
                .Where(d => d.Utilisateur.DepartementId == deptId);

            if (!voirArchives) query = query.Where(d => d.Statut != "Archivé");
            else query = query.Where(d => d.Statut == "Archivé");

            var demandes = await query.OrderByDescending(d => d.DateCreation).ToListAsync();
            ViewBag.VoirArchives = voirArchives;
            return View(demandes);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ArchiveDemande(int id)
        {
            var demande = await _context.Demandes.FindAsync(id);
            if (demande == null) return NotFound();
            var deptId = GetDeptId();
            var user = await _context.Utilisateurs.FindAsync(demande.UtilisateurId);
            if (user == null || user.DepartementId != deptId) return RedirectToAction("Index", "Login");
            demande.Statut = "Archivé";
            await _context.SaveChangesAsync();
            TempData["Success"] = "Demande archivée.";
            return RedirectToAction(nameof(Demandes));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SupprimerDemande(int id)
        {
            var demande = await _context.Demandes.FindAsync(id);
            if (demande == null) return NotFound();
            var deptId = GetDeptId();
            var user = await _context.Utilisateurs.FindAsync(demande.UtilisateurId);
            if (user == null || user.DepartementId != deptId) return RedirectToAction("Index", "Login");
            _context.Demandes.Remove(demande);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Demande supprimée.";
            return RedirectToAction(nameof(Demandes));
        }

        [HttpPost]
        public async Task<IActionResult> TraiterDemande(int id, string action, string note)
        {
            var demande = await _context.Demandes.FindAsync(id);
            if (demande == null) return NotFound();
            if (action == "Approuver") demande.Statut = "Approuvé";
            else if (action == "Refuser") demande.Statut = "Refusé";
            demande.NoteResponsable = note;
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Demandes));
        }

        public async Task<IActionResult> Profs()
        {
            var deptId = GetDeptId();
            if (deptId == null) return RedirectToAction("Index", "Login");
            var profs = await _context.Utilisateurs
                .Where(u => u.Role == "Professeur" && u.DepartementId == deptId.Value)
                .OrderBy(u => u.Nom)
                .ToListAsync();
            return View(profs);
        }

        [HttpGet]
        public IActionResult CreerProf() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreerProf(string nom, string email, string motDePasseProvisoire, [FromServices] EmailService emailService)
        {
            var deptId = GetDeptId();
            if (deptId == null) return RedirectToAction("Index", "Login");
            if (string.IsNullOrWhiteSpace(nom) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(motDePasseProvisoire))
            {
                ViewBag.Error = "Tous les champs sont obligatoires.";
                return View();
            }
            if (_context.Utilisateurs.Any(u => u.Email == email))
            {
                ViewBag.Error = "Email déjà utilisé.";
                return View();
            }
            if (!MotDePasseValide(motDePasseProvisoire))
            {
                ViewBag.Error = "Le mot de passe doit être complexe (8+ car, maj, min, chiffre, spécial).";
                return View();
            }
            Guid salt = Guid.NewGuid();
            var prof = new Utilisateur { Nom = nom, Email = email, Role = "Professeur", DepartementId = deptId.Value, MotDePasseSalt = salt, MotDePasseHash = CalculerSHA256AvecSalt(motDePasseProvisoire, salt), EstMotDePasseProvisoire = true };
            _context.Utilisateurs.Add(prof);
            await _context.SaveChangesAsync();
            try { emailService.Send(email, "Création compte", $"Mdp provisoire : {motDePasseProvisoire}"); } catch { }
            TempData["Success"] = "Professeur créé.";
            return RedirectToAction(nameof(Profs));
        }

        [HttpGet]
        public async Task<IActionResult> EditerProf(int id)
        {
            var deptId = GetDeptId();
            var prof = await _context.Utilisateurs.FindAsync(id);
            if (prof == null || prof.Role != "Professeur" || prof.DepartementId != deptId) return NotFound();
            return View(prof);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditerProf(int id, string nom, string email)
        {
            var deptId = GetDeptId();
            var prof = await _context.Utilisateurs.FindAsync(id);
            if (prof == null || prof.Role != "Professeur" || prof.DepartementId != deptId) return NotFound();
            prof.Nom = nom; prof.Email = email;
            await _context.SaveChangesAsync();
            TempData["Success"] = "Mis à jour.";
            return RedirectToAction(nameof(Profs));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SupprimerProf(int id)
        {
            var deptId = GetDeptId();
            var prof = await _context.Utilisateurs.FindAsync(id);
            if (prof == null || prof.Role != "Professeur" || prof.DepartementId != deptId) return NotFound();
            if (await _context.Demandes.AnyAsync(d => d.UtilisateurId == id))
            {
                TempData["Error"] = "Impossible de supprimer (demandes liées).";
                return RedirectToAction(nameof(Profs));
            }
            _context.Utilisateurs.Remove(prof);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Supprimé.";
            return RedirectToAction(nameof(Profs));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReinitialiserMotDePasse(int id, [FromServices] EmailService emailService)
        {
            var deptId = GetDeptId();
            var prof = await _context.Utilisateurs.FindAsync(id);
            if (prof == null || prof.Role != "Professeur" || prof.DepartementId != deptId) return NotFound();
            string mdp = "TempPass!" + new Random().Next(1000, 9999);
            Guid salt = Guid.NewGuid();
            prof.MotDePasseSalt = salt; prof.MotDePasseHash = CalculerSHA256AvecSalt(mdp, salt); prof.EstMotDePasseProvisoire = true;
            await _context.SaveChangesAsync();
            try { emailService.Send(prof.Email, "Reset MDP", $"Nouveau mdp : {mdp}"); } catch { }
            TempData["Success"] = "Réinitialisé.";
            return RedirectToAction(nameof(Profs));
        }

        public async Task<IActionResult> ListeCours()
        {
            var deptId = GetDeptId();
            var query = _context.Cours.Include(c => c.Departement).AsQueryable();
            if (deptId != null) query = query.Where(c => c.DepartementId == deptId);
            return View(await query.ToListAsync());
        }

        public IActionResult VoirDispos(int id) => RedirectToAction("Index", "Disponibilite", new { professeurId = id, returnUrl = "/Responsable/Profs" });

        [HttpGet]
        public async Task<IActionResult> TelechargerDocument(int id)
        {
            var d = await _context.Demandes.FindAsync(id);
            if (d?.ContenuFichier == null) return NotFound();
            var deptId = GetDeptId();
            var user = await _context.Utilisateurs.FindAsync(d.UtilisateurId);
            if (user == null || user.DepartementId != deptId) return Unauthorized();
            return File(d.ContenuFichier, d.TypeMime ?? "application/octet-stream", d.FichierJoint ?? "document");
        }

        private static bool MotDePasseValide(string mdp)
        {
            if (string.IsNullOrWhiteSpace(mdp) || mdp.Length < 8) return false;
            return mdp.Any(char.IsUpper) && mdp.Any(char.IsLower) && mdp.Any(char.IsDigit) && mdp.Any(ch => !char.IsLetterOrDigit(ch));
        }

        private static byte[] CalculerSHA256AvecSalt(string mdp, Guid saltGuid)
        {
            byte[] salt = saltGuid.ToByteArray();
            byte[] bytes = Encoding.UTF8.GetBytes(mdp);
            byte[] input = new byte[salt.Length + bytes.Length];
            Buffer.BlockCopy(salt, 0, input, 0, salt.Length);
            Buffer.BlockCopy(bytes, 0, input, salt.Length, bytes.Length);
            return SHA256.HashData(input);
        }
    }
}