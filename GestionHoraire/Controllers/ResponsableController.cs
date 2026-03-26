using GestionHoraire.Data;
using GestionHoraire.Models;
using GestionHoraire.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace GestionHoraire.Controllers
{
    public class ResponsableController : Controller
    {
        private readonly AppDbContext _context;

        public ResponsableController(AppDbContext context)
        {
            _context = context;
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

            return View(responsable);
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