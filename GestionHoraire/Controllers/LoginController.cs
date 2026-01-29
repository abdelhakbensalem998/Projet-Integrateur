using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GestionHoraire.Data;
using Microsoft.AspNetCore.Http;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System;

namespace GestionHoraire.Controllers
{
    public class LoginController : Controller
    {
        private readonly AppDbContext _context;

        public LoginController(AppDbContext context)
        {
            _context = context;
        }

        // GET: /Login
        [HttpGet]
        public IActionResult Index()
        {
            // Si déjà connecté, rediriger selon rôle
            var userId = HttpContext.Session.GetInt32("UserId");
            var role = HttpContext.Session.GetString("UserRole");

            if (userId != null && !string.IsNullOrEmpty(role))
                return RedirectSelonRole(role);

            return View();
        }

        // POST: /Login
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

            if (user == null)
            {
                ViewBag.Error = "Email ou mot de passe incorrect";
                return View();
            }

            // ✅ SHA256(salt + password)
            if (!VerifierMotDePasseSHA256AvecSalt(motDePasse, user.MotDePasseSalt, user.MotDePasseHash))
            {
                ViewBag.Error = "Email ou mot de passe incorrect";
                return View();
            }

            // ✅ Session
            HttpContext.Session.SetInt32("UserId", user.Id);
            HttpContext.Session.SetString("UserRole", user.Role ?? "");
            HttpContext.Session.SetString("UserNom", user.Nom ?? "");
            HttpContext.Session.SetString("UserEmail", user.Email ?? "");
            HttpContext.Session.SetString("UserDepartement", user.Departement?.Nom ?? "");

            // Si dept existe, stocker son Id
            if (user.Departement != null)
                HttpContext.Session.SetInt32("DepartementId", user.Departement.Id);
            else if (user.DepartementId.HasValue)
                HttpContext.Session.SetInt32("DepartementId", user.DepartementId.Value);

            return RedirectSelonRole(user.Role ?? "");
        }

        [HttpGet]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Index");
        }

        // ===========================
        // 🔐 SHA256(salt + password)
        // ===========================
        private static bool VerifierMotDePasseSHA256AvecSalt(string motDePasse, Guid saltGuid, byte[] hashStocke)
        {
            if (hashStocke == null || hashStocke.Length == 0)
                return false;

            byte[] salt = saltGuid.ToByteArray();          // 16 bytes
            byte[] mdpBytes = Encoding.UTF8.GetBytes(motDePasse);

            byte[] input = new byte[salt.Length + mdpBytes.Length];
            Buffer.BlockCopy(salt, 0, input, 0, salt.Length);
            Buffer.BlockCopy(mdpBytes, 0, input, salt.Length, mdpBytes.Length);

            byte[] hashCalcule = SHA256.HashData(input);
            return CryptographicOperations.FixedTimeEquals(hashCalcule, hashStocke);
        }

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
    }
}