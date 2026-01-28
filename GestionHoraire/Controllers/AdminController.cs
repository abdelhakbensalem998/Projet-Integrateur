using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using GestionHoraire.Data;

namespace GestionHoraire.Controllers
{
    public class AdminController : Controller  // ← Controller (pas ControllerBase)
    {
        private readonly AppDbContext _context;

        public AdminController(AppDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            // ✅ ViewBag fonctionne MAINTENANT
            ViewBag.UserRole = HttpContext.Session.GetString("UserRole") ?? "Administrateur";
            ViewBag.UserNom = HttpContext.Session.GetString("UserNom") ?? "Admin";
            ViewBag.UserEmail = HttpContext.Session.GetString("UserEmail") ?? "";

            return View();
        }
    }
}

