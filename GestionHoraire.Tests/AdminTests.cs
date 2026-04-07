using Xunit;
using Microsoft.EntityFrameworkCore;
using GestionHoraire.Controllers;
using GestionHoraire.Data;
using GestionHoraire.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
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

        [Fact]
        public async Task Dashboard_ShouldReturnViewWithCounts()
        {
            using var context = GetDbContext();
            context.Utilisateurs.Add(new Utilisateur { Id = 1, Nom = "A", Email = "a@a.com", Role = "Administrateur", MotDePasseHash = Array.Empty<byte>(), MotDePasseSalt = Guid.NewGuid() });
            context.Utilisateurs.Add(new Utilisateur { Id = 2, Nom = "B", Email = "b@b.com", Role = "Professeur", MotDePasseHash = Array.Empty<byte>(), MotDePasseSalt = Guid.NewGuid() });
            context.Salles.Add(new Salle { Id = 1, Nom = "S1", Type = "Amphi" });
            context.Groupes.Add(new Groupe { Id = 1, Nom = "G1", DepartementId = 1, Niveau = "L1", Effectif = 20 });
            context.Departements.Add(new Departement { Id = 1, Nom = "D1" });
            await context.SaveChangesAsync();

            var controller = GetController(context);

            var result = await controller.Dashboard();

            Assert.IsType<ViewResult>(result);
            Assert.Equal(2, controller.ViewBag.TotalUsers);
            Assert.Equal(1, controller.ViewBag.TotalSalles);
            Assert.Equal(1, controller.ViewBag.TotalGroupes);
            Assert.Equal(1, controller.ViewBag.TotalDepts);
        }

        [Fact]
        public async Task Utilisateurs_WithRoleFilter_ShouldFilter()
        {
            using var context = GetDbContext();
            context.Utilisateurs.Add(new Utilisateur { Id = 1, Nom = "A", Email = "a@a.com", Role = "Administrateur", MotDePasseHash = Array.Empty<byte>(), MotDePasseSalt = Guid.NewGuid() });
            context.Utilisateurs.Add(new Utilisateur { Id = 2, Nom = "B", Email = "b@b.com", Role = "Professeur", MotDePasseHash = Array.Empty<byte>(), MotDePasseSalt = Guid.NewGuid() });
            await context.SaveChangesAsync();

            var controller = GetController(context);

            var result = await controller.Utilisateurs(roleFilter: "Professeur");

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsAssignableFrom<IEnumerable<Utilisateur>>(viewResult.Model);
            Assert.Single(model);
            Assert.Equal("Professeur", model.First().Role);
        }

        [Fact]
        public async Task Utilisateurs_WithSearchTerm_ShouldFilterByNameOrEmail()
        {
            using var context = GetDbContext();
            context.Utilisateurs.Add(new Utilisateur { Id = 1, Nom = "Alice Martin", Email = "alice@example.com", Role = "Administrateur", MotDePasseHash = Array.Empty<byte>(), MotDePasseSalt = Guid.NewGuid() });
            context.Utilisateurs.Add(new Utilisateur { Id = 2, Nom = "Bruno Prof", Email = "prof.bruno@example.com", Role = "Professeur", MotDePasseHash = Array.Empty<byte>(), MotDePasseSalt = Guid.NewGuid() });
            context.Utilisateurs.Add(new Utilisateur { Id = 3, Nom = "Claire", Email = "claire@example.com", Role = "ResponsableDépartement", MotDePasseHash = Array.Empty<byte>(), MotDePasseSalt = Guid.NewGuid() });
            await context.SaveChangesAsync();

            var controller = GetController(context);

            var result = await controller.Utilisateurs(searchTerm: "prof.bruno");

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsAssignableFrom<IEnumerable<Utilisateur>>(viewResult.Model);
            var user = Assert.Single(model);
            Assert.Equal("Bruno Prof", user.Nom);
        }

        [Fact]
        public async Task CreateUser_ValidModel_ShouldAddAndRedirect()
        {
            using var context = GetDbContext();
            var controller = GetController(context);

            var newUser = new Utilisateur
            {
                Id = 10,
                Nom = "Nouveau",
                Email = "nouveau@example.com",
                Role = "Professeur",
                MotDePasseHash = Array.Empty<byte>(),
                MotDePasseSalt = Guid.NewGuid()
            };

            var result = await controller.CreateUser(newUser);

            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Utilisateurs", redirectResult.ActionName);
            Assert.Equal("Utilisateur créé avec succès !", controller.TempData["Success"]);
            Assert.Equal(1, await context.Utilisateurs.CountAsync());
            Assert.Equal("Nouveau", (await context.Utilisateurs.FirstAsync()).Nom);
        }

        [Fact]
        public async Task EditUser_ValidModel_ShouldUpdateAndRedirect()
        {
            using var context = GetDbContext();
            context.Utilisateurs.Add(new Utilisateur
            {
                Id = 8,
                Nom = "Avant",
                Email = "avant@example.com",
                Role = "Professeur",
                DepartementId = 1,
                MotDePasseHash = Array.Empty<byte>(),
                MotDePasseSalt = Guid.NewGuid()
            });
            await context.SaveChangesAsync();

            var controller = GetController(context);

            var updatedUser = new Utilisateur
            {
                Id = 8,
                Nom = "Apres",
                Email = "apres@example.com",
                Role = "ResponsableDépartement",
                DepartementId = 2,
                MotDePasseHash = Array.Empty<byte>(),
                MotDePasseSalt = Guid.NewGuid()
            };

            var result = await controller.EditUser(8, updatedUser);

            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Utilisateurs", redirectResult.ActionName);
            Assert.Equal("Informations mises à jour.", controller.TempData["Success"]);

            var user = await context.Utilisateurs.FindAsync(8);
            Assert.NotNull(user);
            Assert.Equal("Apres", user!.Nom);
            Assert.Equal("apres@example.com", user.Email);
            Assert.Equal("ResponsableDépartement", user.Role);
            Assert.Equal(2, user.DepartementId);
        }

        [Fact]
        public async Task DeleteUser_ShouldRemoveUser()
        {
            using var context = GetDbContext();
            context.Utilisateurs.Add(new Utilisateur { Id = 5, Nom = "A supprimer", Email = "s@s.com", Role = "Professeur", MotDePasseHash = Array.Empty<byte>(), MotDePasseSalt = Guid.NewGuid() });
            await context.SaveChangesAsync();

            var controller = GetController(context);

            var result = await controller.DeleteUser(5);

            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Utilisateurs", redirectResult.ActionName);
            Assert.Equal("Utilisateur supprimé.", controller.TempData["Success"]);
            Assert.Equal(0, await context.Utilisateurs.CountAsync());
        }

        [Fact]
        public void Index_ShouldRedirectToDashboard()
        {
            using var context = GetDbContext();
            var controller = GetController(context);

            var result = controller.Index();

            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Dashboard", redirectResult.ActionName);
        }

        [Fact]
        public void Dashboard_View_ShouldContainExpectedCardLinks()
        {
            var dashboardPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "GestionHoraire", "Views", "Admin", "Dashboard.cshtml"));

            var dashboardContent = File.ReadAllText(dashboardPath);

            Assert.Contains("asp-controller=\"Admin\" asp-action=\"Utilisateurs\"", dashboardContent);
            Assert.Contains("asp-controller=\"Salles\" asp-action=\"Index\"", dashboardContent);
            Assert.Contains("asp-controller=\"Groupes\" asp-action=\"Index\"", dashboardContent);
            Assert.Contains("asp-controller=\"Departements\" asp-action=\"Index\"", dashboardContent);
            Assert.Contains("asp-controller=\"Planning\" asp-action=\"Index\"", dashboardContent);
        }
    }
}
