using GestionHoraire.Data;
using GestionHoraire.Models;
using GestionHoraire.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using System.IO;
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
            var profIds = tousCours.Where(c => c.UtilisateurId != null).Select(c => c.UtilisateurId).Distinct();
            int profsSurcharges = 0;
            foreach (var pId in profIds)
            {
                var heures = tousCours.Where(c => c.UtilisateurId == pId).Sum(c => (c.HeureFin - c.HeureDebut).TotalHours);
                if (heures > 18) profsSurcharges++;
            }
            ViewBag.NbSurcharges = profsSurcharges;

            return View(responsable);
        }

        // 5. GESTION DES DEMANDES
        public async Task<IActionResult> Demandes(bool voirArchives = false)
        {
            int? deptId = GetDeptId();
            var query = _context.Demandes
                .Include(d => d.Utilisateur)
                .Where(d => d.Utilisateur.DepartementId == deptId);

            if (!voirArchives)
            {
                query = query.Where(d => d.Statut != "Archivé");
            }
            else
            {
                query = query.Where(d => d.Statut == "Archivé");
            }

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

            // Security check
            var deptId = GetDeptId();
            var user = await _context.Utilisateurs.FindAsync(demande.UtilisateurId);
            if (user == null || user.DepartementId != deptId) return RedirectToAction("Index", "Login");

            demande.Statut = "Archivé";
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Demandes));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SupprimerDemande(int id)
        {
            var demande = await _context.Demandes.FindAsync(id);
            if (demande == null) return NotFound();

            // Security check
            var deptId = GetDeptId();
            var user = await _context.Utilisateurs.FindAsync(demande.UtilisateurId);
            if (user == null || user.DepartementId != deptId) return RedirectToAction("Index", "Login");

            _context.Demandes.Remove(demande);
            await _context.SaveChangesAsync();
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

        // =========================
        // LISTE DES PROFESSEURS
        // =========================
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

        // =========================
        // CREER PROF (GET)
        // =========================
        [HttpGet]
        public IActionResult CreerProf()
        {
            return View();
        }

        // =========================
        // CREER PROF (POST) + EMAIL
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreerProf(
            string nom,
            string email,
            string motDePasseProvisoire,
            [FromServices] EmailService emailService)
        {
            var deptId = GetDeptId();
            if (deptId == null) return RedirectToAction("Index", "Login");

            if (string.IsNullOrWhiteSpace(nom) ||
                string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(motDePasseProvisoire))
            {
                ViewBag.Error = "Tous les champs sont obligatoires.";
                return View();
            }

            // email unique
            if (_context.Utilisateurs.Any(u => u.Email == email))
            {
                ViewBag.Error = "Un compte avec cet email existe déjà.";
                return View();
            }

            // mot de passe provisoire doit respecter règles
            if (!MotDePasseValide(motDePasseProvisoire))
            {
                ViewBag.Error = "Le mot de passe provisoire doit contenir au moins 8 caractères, une majuscule, une minuscule, un chiffre et un caractère spécial.";
                return View();
            }

            // créer user prof
            Guid salt = Guid.NewGuid();
            byte[] hash = CalculerSHA256AvecSalt(motDePasseProvisoire, salt);

            var prof = new Utilisateur
            {
                Nom = nom,
                Email = email,
                Role = "Professeur",
                DepartementId = deptId.Value,
                MotDePasseSalt = salt,
                MotDePasseHash = hash,
                EstMotDePasseProvisoire = true,
                DateCreation = DateTime.Now
            };

            _context.Utilisateurs.Add(prof);
            await _context.SaveChangesAsync();

            // ✅ EMAIL : confirmation + mdp provisoire + instructions
            string body =
$@"Bonjour {nom},

Votre compte professeur a été créé dans l'application Gestion Horaire.

Email : {email}
Mot de passe provisoire : {motDePasseProvisoire}

Étapes à suivre :
1) Connectez-vous avec l'email et le mot de passe provisoire.
2) Changez votre mot de passe (obligatoire).
3) Configurez votre question de sécurité.

Merci.";

            try
            {
                emailService.Send(email, "Création de votre compte - Gestion Horaire", body);
                TempData["Success"] = "Professeur créé avec succès. Un email a été envoyé.";
            }
            catch
            {
                // si SMTP pas configuré ou erreur d'envoi, on ne bloque pas la création
                TempData["Success"] = "Professeur créé avec succès. (Email non envoyé : configuration SMTP manquante)";
            }

            return RedirectToAction(nameof(Profs));
        }

        // =========================
        // UTILITAIRES
        // =========================
        private static bool MotDePasseValide(string motDePasse)
        {
            if (string.IsNullOrWhiteSpace(motDePasse) || motDePasse.Length < 8)
                return false;

            bool hasUpper = motDePasse.Any(char.IsUpper);
            bool hasLower = motDePasse.Any(char.IsLower);
            bool hasDigit = motDePasse.Any(char.IsDigit);
            bool hasSpecial = motDePasse.Any(ch => !char.IsLetterOrDigit(ch));

            return hasUpper && hasLower && hasDigit && hasSpecial;
        }

        // 3. GESTION DES COURS
        public async Task<IActionResult> ListeCours()
        {
            var monDeptId = GetDeptId();
            var userId = GetUserId();
            var user = await _context.Utilisateurs.FindAsync(userId);
            var userRole = user?.Role;
            var query = _context.Cours.Include(c => c.Departement).AsQueryable();

            if (userRole == "Administrateur")
            {
            }
            else if (monDeptId != null)
            {
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
        public IActionResult VoirDispos(int id)
        {
            return RedirectToAction("Index", "Disponibilite", new { professeurId = id, returnUrl = "/Responsable/Profs" });
        }

        [HttpGet]
        public async Task<IActionResult> TelechargerDocument(int id)
        {
            var demande = await _context.Demandes.FindAsync(id);
            if (demande == null || string.IsNullOrEmpty(demande.FichierJoint)) return NotFound();

            // Verify the requester belongs to the manager's department
            var deptId = GetDeptId();
            var user = await _context.Utilisateurs.FindAsync(demande.UtilisateurId);
            if (user == null || user.DepartementId != deptId) return RedirectToAction("Index", "Login");

            var filePath = Path.Combine(_env.WebRootPath, "uploads", demande.FichierJoint);
            if (!System.IO.File.Exists(filePath)) return NotFound("Le fichier n'existe pas sur le serveur.");

            var fileBytes = System.IO.File.ReadAllBytes(filePath);
            
            // Extract the original filename (after the Guid_)
            var originalName = demande.FichierJoint.Contains("_") ?
                               demande.FichierJoint.Substring(demande.FichierJoint.IndexOf("_") + 1) :
                               demande.FichierJoint;

            return File(fileBytes, "application/octet-stream", originalName);
        }

        private static byte[] CalculerSHA256AvecSalt(string motDePasse, Guid saltGuid)
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