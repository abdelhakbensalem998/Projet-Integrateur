// ============================================================
// Fichier : DisponibiliteTests.cs
// Description : Tests unitaires pour DisponibiliteController
// Vérifie les droits d'accès des professeurs vs responsables 
// et la génération automatique des disponibilités.
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
    public class DisponibiliteTests
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

        private DisponibiliteController GetController(AppDbContext context, int userId, string userRole)
        {
            var controller = new DisponibiliteController(context);
            var httpContext = new DefaultHttpContext();
            httpContext.Session = new TestSession();
            httpContext.Session.SetInt32("UserId", userId);
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
        // TEST 1 : Un professeur ne voit que ses propres dispos
        // ============================================================
        [Fact]
        public async Task Index_Professeur_ShouldOnlySeeOwn()
        {
            // --- PRÉPARATION ---
            using var context = GetDbContext();
            context.Utilisateurs.Add(new Utilisateur { Id = 1, Nom = "Prof 1", Role = "Professeur", Email="p1@p.com", MotDePasseHash=new byte[0], MotDePasseSalt=Guid.NewGuid() });
            context.Utilisateurs.Add(new Utilisateur { Id = 2, Nom = "Prof 2", Role = "Professeur", Email="p2@p.com", MotDePasseHash=new byte[0], MotDePasseSalt=Guid.NewGuid() });
            
            context.Disponibilites.Add(new Disponibilite { Id = 1, UtilisateurId = 1, Jour = DayOfWeek.Monday, Disponible = true });
            context.Disponibilites.Add(new Disponibilite { Id = 2, UtilisateurId = 2, Jour = DayOfWeek.Tuesday, Disponible = true });
            await context.SaveChangesAsync();

            // Professeur 1 se connecte
            var controller = GetController(context, userId: 1, userRole: "Professeur");

            // --- ACTION ---
            // Il tente de voir la page de Professeur 2
            var result = await controller.Index(professeurId: 2); 

            // --- VÉRIFICATION ---
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsAssignableFrom<IEnumerable<Disponibilite>>(viewResult.Model);
            
            // Le contrôleur le force à voir SES propres disponibilités (Id = 1)
            Assert.Single(model);
            Assert.Equal(1, model.First().UtilisateurId);
            Assert.Equal(DayOfWeek.Monday, model.First().Jour);
        }

        // ============================================================
        // TEST 2 : Un prof ne peut pas créer pour un autre
        // ============================================================
        [Fact]
        public async Task Create_Professeur_CannotCreateForOther()
        {
            using var context = GetDbContext();
            var controller = GetController(context, userId: 1, userRole: "Professeur");

            // Tente de poster une création pour Professeur 2
            var model = new Disponibilite 
            { 
                UtilisateurId = 2, 
                Jour = DayOfWeek.Monday, 
                HeureDebut = new TimeSpan(8,0,0), 
                HeureFin = new TimeSpan(10,0,0), 
                Disponible = true 
            };
            var result = await controller.Create(model);

            // Le contrôleur retourne Forbid() (Accès refusé)
            Assert.IsType<ForbidResult>(result);
        }

        // ============================================================
        // TEST 3 : Empêcher les créneaux en double
        // ============================================================
        [Fact]
        public async Task Create_Duplicate_ShouldReturnError()
        {
            using var context = GetDbContext();
            context.Disponibilites.Add(new Disponibilite 
            { 
                Id = 1, UtilisateurId = 1, Jour = DayOfWeek.Monday, 
                HeureDebut = new TimeSpan(8,0,0), HeureFin = new TimeSpan(10,0,0), Disponible = true 
            });
            await context.SaveChangesAsync();

            var controller = GetController(context, userId: 1, userRole: "Professeur");

            // Même créneau
            var dispoDupliquee = new Disponibilite 
            { 
                UtilisateurId = 1, Jour = DayOfWeek.Monday, 
                HeureDebut = new TimeSpan(8,0,0), HeureFin = new TimeSpan(10,0,0), Disponible = true 
            };

            var result = await controller.Create(dispoDupliquee);

            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.False(controller.ModelState.IsValid); // Une erreur "existe déjà" a été ajoutée
        }

        // ============================================================
        // TEST 4 : Générateur par défaut remplit la semaine
        // ============================================================
        [Fact]
        public async Task GenererDefaut_ShouldCreateWeeklySchedule()
        {
            using var context = GetDbContext();
            
            // Responsable a le droit de générer pour un prof
            var controller = GetController(context, userId: 1, userRole: "Responsable");

            // Génère pour le prof 5
            var result = await controller.GenererDefaut(professeurId: 5);

            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirectResult.ActionName);
            
            // 5 jours de la semaine * 5 créneaux de 2h = 25 disponibilités par défaut
            var count = await context.Disponibilites.CountAsync(d => d.UtilisateurId == 5);
            Assert.Equal(25, count);
        }
    }
}
