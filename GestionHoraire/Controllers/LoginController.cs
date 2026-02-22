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

            //  Si mot de passe provisoire -> forcer changement AVANT d'ouvrir la session complète
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
        // + configurer question de sécurité
        // =========================

        [HttpGet]
        public IActionResult ChangeTempPassword(string email)
        {
            ViewBag.Email = email;
            ViewBag.Questions = GetQuestionsSecurite();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ChangeTempPassword(
            string email,
            string motDePasseProvisoire,
            string nouveauMotDePasse,
            string confirmerMotDePasse,
            string questionSecurite,
            string reponseSecurite)
        {
            ViewBag.Email = email;
            ViewBag.Questions = GetQuestionsSecurite();

            if (string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(motDePasseProvisoire) ||
                string.IsNullOrWhiteSpace(nouveauMotDePasse) ||
                string.IsNullOrWhiteSpace(confirmerMotDePasse) ||
                string.IsNullOrWhiteSpace(questionSecurite) ||
                string.IsNullOrWhiteSpace(reponseSecurite))
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

            if (!user.EstMotDePasseProvisoire)
            {
                ViewBag.Error = "Ce compte n'a pas de mot de passe provisoire actif.";
                return View();
            }

            if (!VerifierMotDePasseSHA256AvecSalt(motDePasseProvisoire, user.MotDePasseSalt, user.MotDePasseHash))
            {
                ViewBag.Error = "Informations invalides.";
                return View();
            }

            if (VerifierMotDePasseSHA256AvecSalt(nouveauMotDePasse, user.MotDePasseSalt, user.MotDePasseHash))
            {
                ViewBag.Error = "Le nouveau mot de passe doit être différent de l'ancien.";
                return View();
            }

            // 1) Sauver nouveau mot de passe
            Guid salt = Guid.NewGuid();
            byte[] hash = CalculerSHA256AvecSalt(nouveauMotDePasse, salt);

            user.MotDePasseSalt = salt;
            user.MotDePasseHash = hash;
            user.EstMotDePasseProvisoire = false;

            // 2) Sauver question + réponse sécurité (hash + salt)
            string normalized = (reponseSecurite ?? "").Trim().ToLowerInvariant();
            Guid repSalt = Guid.NewGuid();
            byte[] repHash = CalculerSHA256AvecSalt(normalized, repSalt);

            user.QuestionSecurite = questionSecurite;
            user.ReponseSecuriteSalt = repSalt;
            user.ReponseSecuriteHash = repHash;

            _context.SaveChanges();

            TempData["Success"] = "Mot de passe changé et question de sécurité enregistrée. Vous pouvez vous connecter.";
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

            
            if (user == null)
            {
                ViewBag.Error = "Aucun compte trouvé avec cet email.";
                return View();
            }

            return RedirectToAction("ForgotPasswordReset", new { id = user.Id });
        }

        // =========================
        // MOT DE PASSE OUBLIÉ (Étape 2/3)
        // Step 2: question/réponse
        // Step 3: nouveau mot de passe (affiché seulement si réponse OK)
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

            // Step 2 = afficher question + réponse uniquement
            ViewBag.Step = 2;
            ViewBag.UserId = user.Id;
            ViewBag.Question = user.QuestionSecurite;

            // Nettoyer une ancienne validation
            HttpContext.Session.Remove("PwdResetVerifiedUserId");

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ForgotPasswordReset(
            int userId,
            string step,
            string reponseSecurite,
            string nouveauMotDePasse,
            string confirmerMotDePasse)
        {
            var user = _context.Utilisateurs.FirstOrDefault(u => u.Id == userId);

            if (user == null)
            {
                ViewBag.Error = "Informations invalides.";
                ViewBag.Step = 2;
                return View();
            }

            ViewBag.UserId = user.Id;
            ViewBag.Question = user.QuestionSecurite;

            // -------- STEP 2 : Vérifier réponse --------
            if (step == "2")
            {
                if (string.IsNullOrWhiteSpace(reponseSecurite))
                {
                    ViewBag.Error = "Veuillez répondre à la question.";
                    ViewBag.Step = 2;
                    return View();
                }

                if (!VerifierReponseSecurite(user, reponseSecurite))
                {
                    ViewBag.Error = "Réponse incorrecte.";
                    ViewBag.Step = 2; //  ne pas afficher les champs mot de passe
                    return View();
                }

                //  Réponse correcte -> autoriser l'étape 3
                HttpContext.Session.SetInt32("PwdResetVerifiedUserId", user.Id);

                ViewBag.Step = 3; //  afficher champs nouveau mdp + confirmation
                return View();
            }

            // -------- STEP 3 : Changer mot de passe --------
            if (step == "3")
            {
                var verifiedId = HttpContext.Session.GetInt32("PwdResetVerifiedUserId");
                if (verifiedId == null || verifiedId.Value != user.Id)
                {
                    ViewBag.Error = "Veuillez répondre à la question de sécurité avant de changer le mot de passe.";
                    ViewBag.Step = 2;
                    return View();
                }

                if (string.IsNullOrWhiteSpace(nouveauMotDePasse) ||
                    string.IsNullOrWhiteSpace(confirmerMotDePasse))
                {
                    ViewBag.Error = "Tous les champs sont obligatoires.";
                    ViewBag.Step = 3;
                    return View();
                }

                if (nouveauMotDePasse != confirmerMotDePasse)
                {
                    ViewBag.Error = "La confirmation ne correspond pas.";
                    ViewBag.Step = 3;
                    return View();
                }

                if (!MotDePasseValide(nouveauMotDePasse))
                {
                    ViewBag.Error = "Le mot de passe doit contenir au moins 8 caractères, une majuscule, une minuscule, un chiffre et un caractère spécial.";
                    ViewBag.Step = 3;
                    return View();
                }

                if (VerifierMotDePasseSHA256AvecSalt(nouveauMotDePasse, user.MotDePasseSalt, user.MotDePasseHash))
                {
                    ViewBag.Error = "Le nouveau mot de passe doit être différent de l'ancien.";
                    ViewBag.Step = 3;
                    return View();
                }

                Guid salt = Guid.NewGuid();
                byte[] hash = CalculerSHA256AvecSalt(nouveauMotDePasse, salt);

                user.MotDePasseSalt = salt;
                user.MotDePasseHash = hash;
                user.EstMotDePasseProvisoire = false;

                _context.SaveChanges();

                // nettoyer
                HttpContext.Session.Remove("PwdResetVerifiedUserId");

                TempData["Success"] = "Mot de passe réinitialisé. Vous pouvez vous connecter.";
                return RedirectToAction("Index");
            }

            // fallback
            ViewBag.Step = 2;
            return View();
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

        private static string[] GetQuestionsSecurite() => new[]
        {
            "Quel est le nom de ta première école ?",
            "Quel est le prénom de ta mère ?",
            "Quel est le nom de ton premier animal ?",
            "Dans quelle ville es-tu née ?",
            "Quel est ton plat préféré ?"
        };

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

            string normalized = (reponse ?? "").Trim().ToLowerInvariant();

            byte[] hashCalcule = CalculerSHA256AvecSalt(normalized, user.ReponseSecuriteSalt.Value);
            return CryptographicOperations.FixedTimeEquals(hashCalcule, user.ReponseSecuriteHash);
        }
    }
}