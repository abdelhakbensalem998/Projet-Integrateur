using GestionHoraire.Data;
using GestionHoraire.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace GestionHoraire.Controllers
{
    public class LoginController : Controller
    {
        private readonly AppDbContext _context;

        public LoginController(AppDbContext context)
        {
            _context = context;
        }

        // =========================
        // LOGIN
        // =========================

        [HttpGet]
        public IActionResult Index()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            var role = HttpContext.Session.GetString("UserRole");

            if (userId != null && !string.IsNullOrEmpty(role))
                return RedirectSelonRole(role);

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Index(string email, string motDePasse)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(motDePasse))
            {
                ViewBag.Error = "Email ou mot de passe incorrect";
                return View();
            }

            var user = _context.Utilisateurs
                .Include(u => u.Departement)
                .FirstOrDefault(u => u.Email == email);

            if (user == null ||
                !VerifierMotDePasseSHA256AvecSalt(motDePasse, user.MotDePasseSalt, user.MotDePasseHash))
            {
                ViewBag.Error = "Email ou mot de passe incorrect";
                return View();
            }

            // ✅ Si mot de passe provisoire -> forcer changement AVANT d'ouvrir la session complète
            if (user.EstMotDePasseProvisoire)
            {
                TempData["Info"] = "Vous devez changer votre mot de passe provisoire.";
                return RedirectToAction("ChangeTempPassword", new { email = user.Email });
            }

            // Session
            HttpContext.Session.SetInt32("UserId", user.Id);
            HttpContext.Session.SetString("UserRole", user.Role ?? "");
            HttpContext.Session.SetString("UserNom", user.Nom ?? "");
            HttpContext.Session.SetString("UserEmail", user.Email ?? "");

            if (user.DepartementId.HasValue)
                HttpContext.Session.SetInt32("DepartementId", user.DepartementId.Value);

            return RedirectSelonRole(user.Role ?? "");
        }

        // =========================
        // LOGOUT
        // =========================

        [HttpGet]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Index");
        }

        // =========================
        // CHANGER MOT DE PASSE PROVISOIRE
        // (email + mdp provisoire + nouveau + confirmation)
        // =========================

        [HttpGet]
        public IActionResult ChangeTempPassword(string email)
        {
            ViewBag.Email = email; // préremplissage si redirection depuis login
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ChangeTempPassword(string email, string motDePasseProvisoire, string nouveauMotDePasse, string confirmerMotDePasse)
        {
            ViewBag.Email = email;

            if (string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(motDePasseProvisoire) ||
                string.IsNullOrWhiteSpace(nouveauMotDePasse) ||
                string.IsNullOrWhiteSpace(confirmerMotDePasse))
            {
                ViewBag.Error = "Tous les champs sont obligatoires.";
                return View();
            }

            if (nouveauMotDePasse != confirmerMotDePasse)
            {
                ViewBag.Error = "La confirmation ne correspond pas.";
                return View();
            }

            if (!MotDePasseValide(nouveauMotDePasse))
            {
                ViewBag.Error = "Le mot de passe doit contenir au moins 8 caractères, une majuscule, une minuscule, un chiffre et un caractère spécial.";
                return View();
            }

            var user = _context.Utilisateurs.FirstOrDefault(u => u.Email == email);

            if (user == null)
            {
                ViewBag.Error = "Informations invalides.";
                return View();
            }

            // ✅ obliger que ce soit vraiment un compte provisoire
            if (!user.EstMotDePasseProvisoire)
            {
                ViewBag.Error = "Ce compte n'a pas de mot de passe provisoire actif.";
                return View();
            }

            // vérifier mdp provisoire = mdp actuel
            if (!VerifierMotDePasseSHA256AvecSalt(motDePasseProvisoire, user.MotDePasseSalt, user.MotDePasseHash))
            {
                ViewBag.Error = "Informations invalides.";
                return View();
            }

            // interdire réutiliser l'ancien
            if (VerifierMotDePasseSHA256AvecSalt(nouveauMotDePasse, user.MotDePasseSalt, user.MotDePasseHash))
            {
                ViewBag.Error = "Le nouveau mot de passe doit être différent de l'ancien.";
                return View();
            }

            Guid salt = Guid.NewGuid();
            byte[] hash = CalculerSHA256AvecSalt(nouveauMotDePasse, salt);

            user.MotDePasseSalt = salt;
            user.MotDePasseHash = hash;
            user.EstMotDePasseProvisoire = false;

            _context.SaveChanges();

            TempData["Success"] = "Mot de passe changé avec succès. Vous pouvez vous connecter.";
            return RedirectToAction("Index");
        }

        // =========================
        // MOT DE PASSE OUBLIÉ (Étape 1 : email)
        // =========================

        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ForgotPassword(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                ViewBag.Error = "Veuillez entrer votre email.";
                return View();
            }

            var user = _context.Utilisateurs.FirstOrDefault(u => u.Email == email);

            // message neutre
            if (user == null)
            {
                ViewBag.Error = "Si ce compte existe, la procédure est disponible.";
                return View();
            }

            // aller à la question
            return RedirectToAction("ForgotPasswordReset", new { id = user.Id });
        }

        // =========================
        // MOT DE PASSE OUBLIÉ (Étape 2 : question + réponse + nouveau mdp)
        // =========================

        [HttpGet]
        public IActionResult ForgotPasswordReset(int id)
        {
            var user = _context.Utilisateurs.FirstOrDefault(u => u.Id == id);
            if (user == null)
                return RedirectToAction("ForgotPassword");

            if (string.IsNullOrWhiteSpace(user.QuestionSecurite) ||
                user.ReponseSecuriteSalt == null ||
                user.ReponseSecuriteHash == null)
            {
                TempData["Info"] = "Aucune question de sécurité n'est configurée pour ce compte.";
                return RedirectToAction("ForgotPassword");
            }

            ViewBag.UserId = user.Id;
            ViewBag.Question = user.QuestionSecurite;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ForgotPasswordReset(int userId, string reponseSecurite, string nouveauMotDePasse, string confirmerMotDePasse)
        {
            var user = _context.Utilisateurs.FirstOrDefault(u => u.Id == userId);

            if (user == null)
            {
                ViewBag.Error = "Informations invalides.";
                return View();
            }

            ViewBag.UserId = user.Id;
            ViewBag.Question = user.QuestionSecurite;

            if (string.IsNullOrWhiteSpace(reponseSecurite) ||
                string.IsNullOrWhiteSpace(nouveauMotDePasse) ||
                string.IsNullOrWhiteSpace(confirmerMotDePasse))
            {
                ViewBag.Error = "Tous les champs sont obligatoires.";
                return View();
            }

            if (nouveauMotDePasse != confirmerMotDePasse)
            {
                ViewBag.Error = "La confirmation ne correspond pas.";
                return View();
            }

            if (!MotDePasseValide(nouveauMotDePasse))
            {
                ViewBag.Error = "Le mot de passe doit contenir au moins 8 caractères, une majuscule, une minuscule, un chiffre et un caractère spécial.";
                return View();
            }

            // interdire réutiliser l'ancien
            if (VerifierMotDePasseSHA256AvecSalt(nouveauMotDePasse, user.MotDePasseSalt, user.MotDePasseHash))
            {
                ViewBag.Error = "Le nouveau mot de passe doit être différent de l'ancien.";
                return View();
            }

            // vérifier réponse sécurité (hash + salt)
            if (!VerifierReponseSecurite(user, reponseSecurite))
            {
                ViewBag.Error = "Informations invalides.";
                return View();
            }

            Guid salt = Guid.NewGuid();
            byte[] hash = CalculerSHA256AvecSalt(nouveauMotDePasse, salt);

            user.MotDePasseSalt = salt;
            user.MotDePasseHash = hash;
            user.EstMotDePasseProvisoire = false;

            _context.SaveChanges();

            TempData["Success"] = "Mot de passe réinitialisé. Vous pouvez vous connecter.";
            return RedirectToAction("Index");
        }

        // =========================
        // MÉTHODES UTILITAIRES
        // =========================

        private IActionResult RedirectSelonRole(string role)
        {
            role = (role ?? "").Trim();

            return role switch
            {
                "Administrateur" => RedirectToAction("Index", "Admin"),
                "ResponsableDépartement" => RedirectToAction("Index", "Responsable"),
                "Professeur" => RedirectToAction("Index", "Professeur"),
                _ => RedirectToAction("Index", "Home")
            };
        }

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

        private static bool VerifierMotDePasseSHA256AvecSalt(string motDePasse, Guid saltGuid, byte[] hashStocke)
        {
            byte[] hashCalcule = CalculerSHA256AvecSalt(motDePasse, saltGuid);
            return CryptographicOperations.FixedTimeEquals(hashCalcule, hashStocke);
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

        private static bool VerifierReponseSecurite(Utilisateur user, string reponse)
        {
            if (user.ReponseSecuriteSalt == null || user.ReponseSecuriteHash == null)
                return false;

            // normaliser
            string normalized = (reponse ?? "").Trim().ToLowerInvariant();

            byte[] hashCalcule = CalculerSHA256AvecSalt(normalized, user.ReponseSecuriteSalt.Value);
            return CryptographicOperations.FixedTimeEquals(hashCalcule, user.ReponseSecuriteHash);
        }
    }
}
