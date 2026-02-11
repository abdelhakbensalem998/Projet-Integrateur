using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using GestionHoraire.Data;
using GestionHoraire.Models;
using System;
using System.Linq;

namespace GestionHoraire.Controllers
{
    public class AdminController : Controller
    {
        private readonly AppDbContext _context;

        public AdminController(AppDbContext context)
        {
            _context = context;
        }

        // Page par défaut -> Dashboard
        [HttpGet]
        public IActionResult Index()
        {
            return RedirectToAction(nameof(Dashboard));
        }

        // =========================
        // DASHBOARD (1 seule carte)
        // =========================
        [HttpGet]
        public IActionResult Dashboard()
        {
            ViewBag.UserNom = HttpContext.Session.GetString("UserNom") ?? "Admin";
            ViewBag.UserRole = HttpContext.Session.GetString("UserRole") ?? "Administrateur";
            return View();
        }

        // =========================
        // LISTE UTILISATEURS + FILTRE
        // =========================
        [HttpGet]
        public IActionResult Utilisateurs(string roleFilter)
        {
            ViewBag.UserNom = HttpContext.Session.GetString("UserNom") ?? "Admin";
            ViewBag.UserRole = HttpContext.Session.GetString("UserRole") ?? "Administrateur";

            var query = _context.Utilisateurs
                .Include(u => u.Departement)
                .AsQueryable();

            if (!string.IsNullOrEmpty(roleFilter))
            {
                query = query.Where(u => u.Role == roleFilter);
            }

            return View(query.ToList());
        }

        // =========================
        // CREATE (GET)
        // =========================
        [HttpGet]
        public IActionResult CreateUser()
        {
            ViewBag.UserNom = HttpContext.Session.GetString("UserNom") ?? "Admin";
            ViewBag.UserRole = HttpContext.Session.GetString("UserRole") ?? "Administrateur";
            ViewBag.Departements = _context.Departements.ToList();
            return View();
        }

        // =========================
        // CREATE (POST)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CreateUser(Utilisateur user)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.UserNom = HttpContext.Session.GetString("UserNom") ?? "Admin";
                ViewBag.UserRole = HttpContext.Session.GetString("UserRole") ?? "Administrateur";
                ViewBag.Departements = _context.Departements.ToList();
                return View(user);
            }

            user.DateCreation = DateTime.Now;
            _context.Utilisateurs.Add(user);
            _context.SaveChanges();

            return RedirectToAction(nameof(Utilisateurs));
        }

        // =========================
        // EDIT (GET)
        // =========================
        [HttpGet]
        public IActionResult EditUser(int id)
        {
            ViewBag.UserNom = HttpContext.Session.GetString("UserNom") ?? "Admin";
            ViewBag.UserRole = HttpContext.Session.GetString("UserRole") ?? "Administrateur";
            ViewBag.Departements = _context.Departements.ToList();

            var user = _context.Utilisateurs.Find(id);
            if (user == null)
                return NotFound();

            return View(user);
        }

        // =========================
        // EDIT (POST)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EditUser(int id, Utilisateur user)
        {
            if (id != user.Id)
                return BadRequest();

            if (!ModelState.IsValid)
            {
                ViewBag.UserNom = HttpContext.Session.GetString("UserNom") ?? "Admin";
                ViewBag.UserRole = HttpContext.Session.GetString("UserRole") ?? "Administrateur";
                ViewBag.Departements = _context.Departements.ToList();
                return View(user);
            }

            var existing = _context.Utilisateurs.Find(id);
            if (existing == null)
                return NotFound();

            existing.Nom = user.Nom;
            existing.Email = user.Email;
            existing.Role = user.Role;
            existing.DepartementId = user.DepartementId;

            _context.SaveChanges();

            return RedirectToAction(nameof(Utilisateurs));
        }

        // =========================
        // DELETE (POST)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteUser(int id)
        {
            var utilisateur = _context.Utilisateurs.Find(id);
            if (utilisateur != null)
            {
                _context.Utilisateurs.Remove(utilisateur);
                _context.SaveChanges();
            }

            return RedirectToAction(nameof(Utilisateurs));
        }
    }
}
