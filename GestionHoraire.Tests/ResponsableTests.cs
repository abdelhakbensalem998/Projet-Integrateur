// ============================================================
// Fichier : ResponsableTests.cs
// Description : Tests unitaires pour ResponsableController
// Ce contrôleur gère le tableau de bord complexe des conflits,
// l'approbation des demandes, et les professeurs du département.
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
    public class ResponsableTests
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

        private ResponsableController GetController(AppDbContext context, int userId = 1, int deptId = 1)
        {
            var controller = new ResponsableController(context);
            var httpContext = new DefaultHttpContext();
            httpContext.Session = new TestSession();
            httpContext.Session.SetInt32("UserId", userId);
            httpContext.Session.SetInt32("DepartementId", deptId);
            httpContext.Session.SetString("UserRole", "Responsable");
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            return controller;
        }

        // ============================================================
        // TEST 1 : Calcul des conflits sur le tableau de bord
        // ============================================================
        [Fact]
        public async Task Index_ShouldCalculateConflitsCorrectly()
        {
            // --- PRÉPARATION ---
            using var context = GetDbContext();
            
            // On ajoute le responsable pour qu'il existe en base
            context.Utilisateurs.Add(new Utilisateur { Id = 1, Nom = "Resp", Role = "Responsable", DepartementId = 1, MotDePasseHash=new byte[0], MotDePasseSalt=Guid.NewGuid() });
            
            // 2 cours programmés en MÊME TEMPS, avec le MÊME professeur (Id=10).
            // Le tableau de bord du responsable doit détecter cela comme 1 conflit.
            context.Cours.Add(new Cours { Id = 1, Titre = "Cours A", DepartementId = 1, UtilisateurId = 10, Jour = DayOfWeek.Monday, HeureDebut = new TimeSpan(8,0,0), HeureFin = new TimeSpan(10,0,0) });
            context.Cours.Add(new Cours { Id = 2, Titre = "Cours B", DepartementId = 1, UtilisateurId = 10, Jour = DayOfWeek.Monday, HeureDebut = new TimeSpan(8,0,0), HeureFin = new TimeSpan(10,0,0) });
            
            await context.SaveChangesAsync();

            var controller = GetController(context, userId: 1, deptId: 1);

            // --- ACTION ---
            var result = await controller.Index();

            // --- VÉRIFICATION ---
            var viewResult = Assert.IsType<ViewResult>(result);
            
            // L'algorithme a dû compter exactement 1 conflit (entre le Cours A et le Cours B)
            Assert.Equal(1, controller.ViewBag.NbConflits);
        }

        // ============================================================
        // TEST 2 : Mettre à jour le statut d'une demande
        // ============================================================
        [Fact]
        public async Task TraiterDemande_ShouldUpdateStatus()
        {
            using var context = GetDbContext();
            context.Utilisateurs.Add(new Utilisateur { Id = 1, Nom = "Professeur", Role="Professeur", DepartementId = 1, MotDePasseHash=new byte[0], MotDePasseSalt=Guid.NewGuid() });
            context.Demandes.Add(new Demande { Id = 100, UtilisateurId = 1, Type = "Vacances", Description = "Test", Statut = "En attente", DateCreation=DateTime.Now });
            await context.SaveChangesAsync();

            var controller = GetController(context);

            var result = await controller.TraiterDemande(100, action: "Approuver", note: "Ok, accordé");

            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Demandes", redirectResult.ActionName);

            // Validation de la mise à jour en base
            var demande = await context.Demandes.FindAsync(100);
            Assert.Equal("Approuvé", demande.Statut);
            Assert.Equal("Ok, accordé", demande.NoteResponsable);
        }

        // ============================================================
        // TEST 3 : Création d'un professeur avec mot de passe haché
        // ============================================================
        [Fact]
        public async Task CreerProf_ValidModel_ShouldHashPassword()
        {
            using var context = GetDbContext();
            var controller = GetController(context, deptId: 1);

            var prof = new Utilisateur { Nom = "Nouveau Prof", Email = "prof@test.com" };

            var result = await controller.CreerProf(prof, "TempPwd123!");

            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Profs", redirectResult.ActionName);

            // Valider que le procesus de hachage s'est bien déroulé
            var savedProf = await context.Utilisateurs.FirstAsync(u => u.Email == "prof@test.com");
            Assert.Equal("Professeur", savedProf.Role);
            Assert.Equal(1, savedProf.DepartementId); // Associé automatiquement au département du responsable
            Assert.True(savedProf.EstMotDePasseProvisoire);
            Assert.NotNull(savedProf.MotDePasseHash);
            Assert.NotEqual(new byte[0], savedProf.MotDePasseHash); // Le mot de passe a bien été chiffré
        }

        // ============================================================
        // TEST 4 : Isolation des départements (Sécurité)
        // ============================================================
        [Fact]
        public async Task EditerProf_FromOtherDept_ShouldReturnNotFound()
        {
            using var context = GetDbContext();
            // On a un Professeur du département Id = 2
            context.Utilisateurs.Add(new Utilisateur { Id = 5, Nom = "Prof Dept 2", DepartementId = 2, Role="Professeur", MotDePasseHash=new byte[0], MotDePasseSalt=Guid.NewGuid() });
            await context.SaveChangesAsync();

            // Et on a un Responsable du département Id = 1 
            var controller = GetController(context, deptId: 1);

            // Le responsable essaie d'éditer le prof d'un autre département
            var result = await controller.EditerProf(id: 5);

            // Doit renvoyer NotFound() car la requête est sécurisée (prof.DepartementId != GetMonDeptId())
            Assert.IsType<NotFoundResult>(result);
        }
    }
}
