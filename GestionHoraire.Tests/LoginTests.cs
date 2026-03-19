// ============================================================
// Fichier : LoginTests.cs
// Description : Tests unitaires pour le LoginController
// Ces tests vérifient que l'authentification fonctionne 
// correctement selon différents scénarios : bon mot de passe,
// mauvais mot de passe, et mot de passe provisoire.
// ============================================================

using Xunit;
using Microsoft.EntityFrameworkCore;
using GestionHoraire.Controllers;
using GestionHoraire.Data;
using GestionHoraire.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace GestionHoraire.Tests
{
    public class LoginTests
    {
        // --------------------------------------------------------
        // Méthode d'aide : crée une base de données vide en mémoire
        // Chaque appel crée une BD avec un nom unique (Guid) pour 
        // que les tests ne se mélangent pas entre eux.
        // --------------------------------------------------------
        private AppDbContext GetDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()) // BD unique par test
                .Options;
            var context = new AppDbContext(options);
            context.Database.EnsureCreated(); // Crée les tables
            return context;
        }

        // --------------------------------------------------------
        // Méthode d'aide : crée un LoginController simulé
        // On configure un contexte HTTP factice avec une session 
        // vide, et un TempData simulé (pour les messages flash).
        // --------------------------------------------------------
        private LoginController GetController(AppDbContext context)
        {
            var controller = new LoginController(context);
            var httpContext = new DefaultHttpContext();
            httpContext.Session = new TestSession(); // Session de test (voir PlanningTests.cs)
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };
            // TempData simulé : utilisé par le contrôleur pour les messages d'erreur
            controller.TempData = new MockTempData();
            return controller;
        }

        // --------------------------------------------------------
        // Classe interne : MockTempData
        // Le contrôleur utilise TempData pour transmettre des 
        // messages entre requêtes. Ici, on le simule avec 
        // un simple dictionnaire, sans persister les données.
        // --------------------------------------------------------
        private class MockTempData : System.Collections.Generic.Dictionary<string, object>, Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataDictionary
        {
            public void Keep() { }           // Garde toutes les entrées pour la prochaine requête (non utilisé en test)
            public void Keep(string key) { } // Garde une entrée spécifique (non utilisé en test)
            public void Load() { }           // Charge depuis le cookie/session (non utilisé en test)
            public void Save() { }           // Sauvegarde vers le cookie/session (non utilisé en test)
            public object Peek(string key) => ContainsKey(key) ? this[key] : null; // Lit sans consommer
        }

        // --------------------------------------------------------
        // Méthode utilitaire : HashPassword
        // Reproduit exactement la même logique de hachage que le 
        // LoginController : on combine le sel (salt) et le mot de 
        // passe, puis on calcule le hash SHA-256.
        // Cela permet de créer des utilisateurs de test avec un 
        // mot de passe connu, sans dépendre du contrôleur lui-même.
        // --------------------------------------------------------
        private byte[] HashPassword(string password, Guid saltGuid)
        {
            byte[] salt = saltGuid.ToByteArray();                     // Le sel sous forme de bytes
            byte[] mdpBytes = Encoding.UTF8.GetBytes(password);       // Le mot de passe en UTF-8
            
            // On concatène : [sel] + [mot de passe]
            byte[] input = new byte[salt.Length + mdpBytes.Length];
            Buffer.BlockCopy(salt, 0, input, 0, salt.Length);
            Buffer.BlockCopy(mdpBytes, 0, input, salt.Length, mdpBytes.Length);
            
            // On retourne le hash SHA-256 du tout
            return SHA256.HashData(input);
        }

        // ============================================================
        // TEST 1 : Connexion réussie avec les bonnes informations
        // ============================================================
        // Ce test vérifie que quand un utilisateur entre son email 
        // et son mot de passe corrects, il est redirigé vers son 
        // tableau de bord (Admin dans ce cas), et que sa session 
        // est bien initialisée (UserId stocké).
        // ============================================================
        [Fact]
        public void Login_WithCorrectCredentials_ShouldRedirectToDashboard()
        {
            // --- PRÉPARATION ---
            using var context = GetDbContext();
            var salt = Guid.NewGuid(); // Sel aléatoire pour ce test
            var hash = HashPassword("Password123!", salt); // Hash du bon mot de passe
            
            // On crée un utilisateur de type "Administrateur" avec le bon mot de passe
            context.Utilisateurs.Add(new Utilisateur 
            { 
                Id = 1, 
                Email = "test@example.com", 
                MotDePasseSalt = salt,             // Le sel utilisé pour hacher
                MotDePasseHash = hash,             // Le hash du mot de passe
                Role = "Administrateur",           // Son rôle → redirection vers Admin
                EstMotDePasseProvisoire = false    // Mot de passe définitif (pas provisoire)
            });
            context.SaveChanges();

            var controller = GetController(context);

            // --- ACTION ---
            // On simule la soumission du formulaire de connexion
            var result = controller.Index("test@example.com", "Password123!");

            // --- VÉRIFICATION ---
            // Le résultat doit être une redirection (et non une page d'erreur)
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            
            // La redirection doit pointer vers le contrôleur "Admin"
            Assert.Equal("Admin", redirectResult.ControllerName);
            
            // La session doit contenir l'ID de l'utilisateur connecté
            Assert.Equal(1, controller.HttpContext.Session.GetInt32("UserId"));
        }

        // ============================================================
        // TEST 2 : Connexion échouée avec un mauvais mot de passe
        // ============================================================
        // Ce test vérifie que si l'utilisateur entre un mauvais mot 
        // de passe, le système ne le connecte PAS et lui retourne 
        // la page de connexion avec un message d'erreur visible.
        // ============================================================
        [Fact]
        public void Login_WithWrongPassword_ShouldReturnViewWithError()
        {
            // --- PRÉPARATION ---
            using var context = GetDbContext();
            var salt = Guid.NewGuid();
            var hash = HashPassword("CorrectPassword123!", salt); // Le bon mot de passe est "CorrectPassword123!"
            
            // On crée l'utilisateur en base
            context.Utilisateurs.Add(new Utilisateur 
            { 
                Email = "test@example.com", 
                MotDePasseSalt = salt, 
                MotDePasseHash = hash
            });
            context.SaveChanges();

            var controller = GetController(context);

            // --- ACTION ---
            // L'utilisateur entre un MAUVAIS mot de passe ("WrongPassword!")
            var result = controller.Index("test@example.com", "WrongPassword!");

            // --- VÉRIFICATION ---
            // Le résultat doit être la vue de connexion (pas une redirection)
            var viewResult = Assert.IsType<ViewResult>(result);
            
            // Un message d'erreur doit être visible dans le ViewBag
            Assert.NotNull(controller.ViewBag.Error);
        }

        // ============================================================
        // TEST 3 : Mot de passe provisoire → redirection vers changement
        // ============================================================
        // Ce test vérifie que si un utilisateur se connecte avec un 
        // mot de passe marqué comme "provisoire" (créé par l'admin), 
        // il est immédiatement redirigé vers la page de changement 
        // de mot de passe, et non vers son tableau de bord.
        // 
        // Exemple d'usage : Un admin crée un compte avec "Temp123!" 
        // et coche "Mot de passe provisoire". L'utilisateur doit 
        // changer son mot de passe dès sa première connexion.
        // ============================================================
        [Fact]
        public void Login_WithProvisionalPassword_ShouldRedirectToChangePassword()
        {
            // --- PRÉPARATION ---
            using var context = GetDbContext();
            var salt = Guid.NewGuid();
            var hash = HashPassword("Temp123!", salt); // Mot de passe provisoire connu
            
            // L'utilisateur existe, avec EstMotDePasseProvisoire = true
            context.Utilisateurs.Add(new Utilisateur 
            { 
                Email = "temp@example.com", 
                MotDePasseSalt = salt, 
                MotDePasseHash = hash,
                EstMotDePasseProvisoire = true // ← Indicateur : mot de passe provisoire !
            });
            context.SaveChanges();

            var controller = GetController(context);

            // --- ACTION ---
            // L'utilisateur entre les bonnes credentials, mais son mdp est provisoire
            var result = controller.Index("temp@example.com", "Temp123!");

            // --- VÉRIFICATION ---
            // Il doit être redirigé, mais vers "ChangeTempPassword", pas vers son dashboard
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("ChangeTempPassword", redirectResult.ActionName);
        }
    }
}
