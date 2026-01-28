using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GestionHoraire.Data;
using GestionHoraire.Models;

namespace GestionHoraire.Controllers
{
    public class LoginController : Controller
    {
        private readonly AppDbContext _context;

        public LoginController(AppDbContext context)
        {
            _context = context;
        }

        public IActionResult Index() => View();

        [HttpPost]
        public IActionResult Index(string email, string motDePasse)
        {
            var user = _context.Utilisateurs
                .Include(u => u.Departement)
                .FirstOrDefault(u => u.Email == email);

            if (user != null)
            {
                // Session utilisateur (mot de passe = n'importe quoi pour test)
                HttpContext.Session.SetString("UserId", user.Id.ToString());
                HttpContext.Session.SetString("UserRole", user.Role ?? "Professeur");
                HttpContext.Session.SetString("UserNom", user.Nom);
                HttpContext.Session.SetString("UserEmail", user.Email);
                HttpContext.Session.SetString("UserDepartement", user.Departement?.Nom ?? "");
                // Dans POST Index(), après Session.SetString("UserNom"...):
                if (user.Departement != null)
                {
                    HttpContext.Session.SetInt32("DepartementId", user.Departement.Id);
                }


                // Redirection PAR RÔLE
                return user.Role switch
                {
                    "Administrateur" => RedirectToAction("Index", "Admin"),
                    "ResponsableDépartement" => RedirectToAction("Index", "Responsable"),
                    "Professeur" => RedirectToAction("Index", "Professeur"),
                    _ => RedirectToAction("Index", "Home")
                };
            }

            ViewBag.Error = "Email ou mot de passe incorrect";
            return View();
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Index");
        }
    }
}
