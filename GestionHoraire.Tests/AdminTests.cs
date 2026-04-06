// ============================================================
// Fichier : AdminTests.cs
// Description : Tests unitaires pour l'AdminController
// Vérifie le bon fonctionnement du Dashboard, du filtrage des 
// utilisateurs, et du CRUD (Création, suppression...).
// ============================================================

using Xunit;
using Microsoft.EntityFrameworkCore;
using GestionHoraire.Controllers;
using GestionHoraire.Data;
using GestionHoraire.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GestionHoraire.Tests
{
    public class AdminTests
    {
        private AppDbContext GetDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            var context = new AppDbContext(options);
            context.Database.EnsureCreated();
            return context;
        }

        private AdminController GetController(AppDbContext context, string userNom = "Admin", string userRole = "Administrateur")
        {
            var controller = new AdminController(context);
            var httpContext = new DefaultHttpContext();
            httpContext.Session = new TestSession();
            httpContext.Session.SetString("UserNom", userNom);
            httpContext.Session.SetString("UserRole", userRole);
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new MockTempData();
            return controller;
        }

        private class MockTempData : Dictionary<string, object>, Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataDictionary
        {
            public void Keep() { }
            public void Keep(string key) { }
            public void Load() { }
            public void Save() { }
            public object Peek(string key) => ContainsKey(key) ? this[key] : null;
        }

        // ============================================================
        // TEST 1 : Le Dashboard affiche les bonnes statistiques
        // ============================================================
        [Fact]
        public async Task Dashboard_ShouldReturnViewWithCounts()
        {
            // --- PRÉPARATION ---
            using var context = GetDbContext();
            context.Utilisateurs.Add(new Utilisateur { Id = 1, Nom = "A", Email="a@a.com", Role="Administrateur", MotDePasseHash=new byte[0], MotDePasseSalt=Guid.NewGuid() });
            context.Utilisateurs.Add(new Utilisateur { Id = 2, Nom = "B", Email="b@b.com", Role="Professeur", MotDePasseHash=new byte[0], MotDePasseSalt=Guid.NewGuid() });
            context.Salles.Add(new Salle { Id = 1, Nom = "S1", Type = "Amphi" });
            context.Groupes.Add(new Groupe { Id = 1, Nom = "G1", DepartementId = 1, Niveau = "L1", Effectif = 20 });
            context.Departements.Add(new Departement { Id = 1, Nom = "D1" });
            await context.SaveChangesAsync();

            var controller = GetController(context);

            // --- ACTION ---
            var result = await controller.Dashboard();

            // --- VÉRIFICATION ---
            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.Equal(2, controller.ViewBag.TotalUsers);
            Assert.Equal(1, controller.ViewBag.TotalSalles);
            Assert.Equal(1, controller.ViewBag.TotalGroupes);
            Assert.Equal(1, controller.ViewBag.TotalDepts);
        }

        // ============================================================
        // TEST 2 : Le filtre par rôle fonctionne
        // ============================================================
        [Fact]
        public async Task Utilisateurs_WithRoleFilter_ShouldFilter()
        {
            // --- PRÉPARATION ---
            using var context = GetDbContext();
            context.Utilisateurs.Add(new Utilisateur { Id = 1, Nom = "A", Email="a@a.com", Role="Administrateur", MotDePasseHash=new byte[0], MotDePasseSalt=Guid.NewGuid() });
            context.Utilisateurs.Add(new Utilisateur { Id = 2, Nom = "B", Email="b@b.com", Role="Professeur", MotDePasseHash=new byte[0], MotDePasseSalt=Guid.NewGuid() });
            await context.SaveChangesAsync();

            var controller = GetController(context);

            // --- ACTION ---
            var result = await controller.Utilisateurs(roleFilter: "Professeur");

            // --- VÉRIFICATION ---
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsAssignableFrom<IEnumerable<Utilisateur>>(viewResult.Model);
            Assert.Single(model); // Seulement un "Professeur"
            Assert.Equal("Professeur", model.First().Role);
        }

        // ============================================================
        // TEST 3 : La création d'un utilisateur fonctionne
        // ============================================================
        [Fact]
        public async Task CreateUser_ValidModel_ShouldAddAndRedirect()
        {
            // --- PRÉPARATION ---
            using var context = GetDbContext();
            var controller = GetController(context);

            var newUser = new Utilisateur { Id = 10, Nom = "Nouveau", Email = "nouveau@example.com", Role = "Professeur", MotDePasseHash=new byte[0], MotDePasseSalt=Guid.NewGuid() };
            
            // --- ACTION ---
            var result = await controller.CreateUser(newUser);

            // --- VÉRIFICATION ---
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Utilisateurs", redirectResult.ActionName);
            
            // Vérifie que l'utilisateur est bien enregistré
            Assert.Equal(1, await context.Utilisateurs.CountAsync());
            Assert.Equal("Nouveau", (await context.Utilisateurs.FirstAsync()).Nom);
        }

        // ============================================================
        // TEST 4 : La suppression retire l'utilisateur de la base
        // ============================================================
        [Fact]
        public async Task DeleteUser_ShouldRemoveUser()
        {
            // --- PRÉPARATION ---
            using var context = GetDbContext();
            context.Utilisateurs.Add(new Utilisateur { Id = 5, Nom = "A supprimer", Email="s@s.com", Role="Professeur", MotDePasseHash=new byte[0], MotDePasseSalt=Guid.NewGuid() });
            await context.SaveChangesAsync();

            var controller = GetController(context);

            // --- ACTION ---
            var result = await controller.DeleteUser(5);

            // --- VÉRIFICATION ---
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Utilisateurs", redirectResult.ActionName);
            Assert.Equal(0, await context.Utilisateurs.CountAsync());
        }
    }
}
