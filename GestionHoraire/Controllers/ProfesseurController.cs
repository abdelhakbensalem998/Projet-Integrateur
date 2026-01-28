using GestionHoraire.Data;
using Microsoft.AspNetCore.Mvc;

namespace GestionHoraire.Controllers
{
    public class ProfesseurController : Controller  // ← Controller (POUR ViewBag)
    {
        private readonly AppDbContext _context;

        public ProfesseurController(AppDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            // ✅ Session + ViewBag = FONCTIONNE
            ViewBag.UserRole = HttpContext.Session.GetString("UserRole") ?? "Professeur";
            ViewBag.UserNom = HttpContext.Session.GetString("UserNom") ?? "Professeur";
            ViewBag.UserEmail = HttpContext.Session.GetString("UserEmail") ?? "";
            return View();
        }
    }
}

