// ============================================================
// Fichier : PlanningTests.cs
// Description : Tests unitaires pour le PlanningController
// Ces tests vérifient que la génération de planning et la 
// mise à jour des créneaux respectent bien les contraintes 
// métier (absences, conflits de salle, conflits de prof...).
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
using System.Text.Json;

// ============================================================
// Classe utilitaire : JsonResultHelper
// Permet de lire facilement les propriétés "success" et 
// "message" d'un JsonResult retourné par le contrôleur.
// On utilise la réflexion car les objets anonymes ne peuvent 
// pas être castés directement.
// ============================================================
static class JsonResultHelper
{
    /// <summary>
    /// Retourne true si le JsonResult contient { success: true }
    /// </summary>
    public static bool IsSuccess(JsonResult result)
    {
        var val = result.Value;
        // On cherche la propriété "success" (minuscule d'abord, puis majuscule)
        var prop = val?.GetType().GetProperty("success") ?? val?.GetType().GetProperty("Success");
        return prop != null && (bool)prop.GetValue(val)!;
    }

    /// <summary>
    /// Retourne le message d'erreur ou de succès contenu dans le JsonResult
    /// </summary>
    public static string? GetMessage(JsonResult result)
    {
        var val = result.Value;
        var prop = val?.GetType().GetProperty("message") ?? val?.GetType().GetProperty("Message");
        return prop?.GetValue(val)?.ToString();
    }
}

namespace GestionHoraire.Tests
{
    /// <summary>
    /// Classe de tests pour le PlanningController.
    /// Chaque test est indépendant et utilise une base de données 
    /// en mémoire (InMemory) pour ne pas toucher la vraie base.
    /// </summary>
    public class PlanningTests
    {
        // --------------------------------------------------------
        // Méthode d'aide : crée une base de données vide en mémoire
        // Chaque appel crée une BD différente (Guid unique) pour 
        // éviter que les tests s'influencent mutuellement.
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
        // Méthode d'aide : crée un PlanningController simulé
        // On simule une session utilisateur avec DepartementId=1
        // et le rôle "Responsable" (nécessaire pour générer un planning).
        // --------------------------------------------------------
        private PlanningController GetController(AppDbContext context)
        {
            var controller = new PlanningController(context);
            var httpContext = new DefaultHttpContext();
            httpContext.Session = new TestSession(); // Session simulée (voir bas du fichier)
            httpContext.Session.SetInt32("DepartementId", 1); // Simule un responsable du département 1
            httpContext.Session.SetString("UserRole", "Responsable");
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };
            return controller;
        }

        // ============================================================
        // TEST 1 : La génération place bien les cours dans le planning
        // ============================================================
        // Ce test vérifie que quand on clique sur "Générer Planning Auto",
        // les cours sont effectivement placés dans des créneaux valides
        // (jour et heure différents de zéro).
        // ============================================================
        [Fact]
        public async Task GenererPlanningAleatoire_ShouldPlaceCourses()
        {
            // --- PRÉPARATION ---
            // On crée un département, un groupe, un professeur, une salle et un cours.
            using var context = GetDbContext();
            
            context.Departements.Add(new Departement { Id = 1, Nom = "Informatique" });
            context.Groupes.Add(new Groupe { Id = 1, Nom = "G1", DepartementId = 1, Niveau = "L1", Effectif = 30 });
            context.Utilisateurs.Add(new Utilisateur { Id = 1, Nom = "Prof 1", Role = "Professeur", DepartementId = 1, MotDePasseSalt = Guid.NewGuid(), MotDePasseHash = new byte[0] });
            context.Salles.Add(new Salle { Id = 1, Nom = "Salle 101", Type = "Amphi" });
            
            // Le cours est non-planifié au départ (HeureDebut = 0 par défaut)
            context.Cours.Add(new Cours 
            { 
                Titre = "Programmation C#", 
                DepartementId = 1, 
                GroupeIds = "1",       // Assigné au groupe G1
                UtilisateurId = 1,     // Assigné au professeur 1
                SalleId = 1            // Assigné à la salle 101
            });
            
            await context.SaveChangesAsync();
            
            var controller = GetController(context);

            // --- ACTION ---
            // On appelle la méthode de génération automatique du planning
            var result = await controller.GenererPlanningAleatoire();

            // --- VÉRIFICATION ---
            var jsonResult = Assert.IsType<JsonResult>(result);
            // La réponse doit indiquer un succès
            Assert.True(JsonResultHelper.IsSuccess(jsonResult), JsonResultHelper.GetMessage(jsonResult));

            // Le cours doit maintenant avoir un créneau valide (pas Dimanche ni 00:00)
            var coursPlacé = await context.Cours.FirstAsync();
            Assert.NotEqual(DayOfWeek.Sunday, coursPlacé.Jour);        // Pas le dimanche
            Assert.NotEqual(TimeSpan.Zero, coursPlacé.HeureDebut);     // Heure non nulle → cours placé
        }

        // ============================================================
        // TEST 2 : La vue Index filtre bien par groupe
        // ============================================================
        // Ce test vérifie que quand on sélectionne le groupe G1 dans 
        // le planning, on ne voit QUE les cours du groupe G1, et pas 
        // ceux du groupe G2.
        // ============================================================
        [Fact]
        public async Task Index_WithGroupeId_ShouldFilterCorrectly()
        {
            // --- PRÉPARATION ---
            using var context = GetDbContext();
            context.Departements.Add(new Departement { Id = 1, Nom = "Informatique" });
            context.Groupes.Add(new Groupe { Id = 1, Nom = "G1", DepartementId = 1, Niveau = "L1", Effectif = 30 });
            context.Groupes.Add(new Groupe { Id = 2, Nom = "G2", DepartementId = 1, Niveau = "L1", Effectif = 30 });
            
            // Un cours pour G1 et un cours pour G2
            context.Cours.Add(new Cours { Titre = "Cours G1", DepartementId = 1, GroupeIds = "1" });
            context.Cours.Add(new Cours { Titre = "Cours G2", DepartementId = 1, GroupeIds = "2" });
            
            await context.SaveChangesAsync();
            
            var controller = GetController(context);

            // --- ACTION ---
            // On demande le planning en filtrant sur le groupe 1 (G1)
            var result = await controller.Index(1);

            // --- VÉRIFICATION ---
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsAssignableFrom<IEnumerable<Cours>>(viewResult.Model);
            
            Assert.Single(model);                          // Un seul cours doit être affiché
            Assert.Equal("Cours G1", model.First().Titre); // Et c'est bien le cours de G1
        }

        // ============================================================
        // TEST 3 : Impossible de placer deux cours dans la même salle
        // ============================================================
        // Ce test vérifie que si on essaie de déplacer un cours dans 
        // une salle déjà occupée à la même heure, le système renvoie 
        // une erreur (et ne fait pas le déplacement).
        // ============================================================
        [Fact]
        public async Task MettreAJourPosition_ShouldFail_WhenRoomIsOccupied()
        {
            // --- PRÉPARATION ---
            using var context = GetDbContext();
            context.Departements.Add(new Departement { Id = 1, Nom = "IT" });
            context.Salles.Add(new Salle { Id = 101, Nom = "Salle 101", Type = "Amphi" });
            
            // Cours 1 occupe déjà la Salle 101 le Lundi à 8h00
            context.Cours.Add(new Cours 
            { 
                Id = 10, Titre = "Cours 1", DepartementId = 1, 
                SalleId = 101, Jour = DayOfWeek.Monday, HeureDebut = new TimeSpan(8, 0, 0), HeureFin = new TimeSpan(10, 0, 0) 
            });
            
            // Cours 2 est ailleurs (Mardi 14h00, salle différente)
            context.Cours.Add(new Cours 
            { 
                Id = 11, Titre = "Cours 2", DepartementId = 1, 
                SalleId = 102, Jour = DayOfWeek.Tuesday, HeureDebut = new TimeSpan(14, 0, 0) 
            });

            await context.SaveChangesAsync();
            var controller = GetController(context);

            // --- ACTION ---
            // On essaie de déplacer Cours 2 vers Lundi 8h00 dans la Salle 101 (déjà prise !)
            var result = await controller.MettreAJourPosition(11, (int)DayOfWeek.Monday, "08:00", 101, null);

            // --- VÉRIFICATION ---
            var json = Assert.IsType<JsonResult>(result);
            Assert.False(JsonResultHelper.IsSuccess(json));                        // Doit échouer
            Assert.Contains("La salle est", JsonResultHelper.GetMessage(json));    // Avec le bon message
        }

        // ============================================================
        // TEST 4 : Impossible de planifier un prof à deux endroits
        // ============================================================
        // Ce test vérifie que si un professeur est déjà en cours à 
        // une certaine heure, on ne peut pas lui assigner un autre 
        // cours au même moment.
        // ============================================================
        [Fact]
        public async Task MettreAJourPosition_ShouldFail_WhenTeacherIsOccupied()
        {
            // --- PRÉPARATION ---
            using var context = GetDbContext();
            context.Departements.Add(new Departement { Id = 1, Nom = "IT" });
            context.Utilisateurs.Add(new Utilisateur { Id = 50, Nom = "Prof X", Role = "Professeur", DepartementId = 1, MotDePasseSalt = Guid.NewGuid() });
            
            // Prof 50 est déjà occupé le Lundi à 8h00 avec Cours A
            context.Cours.Add(new Cours 
            { 
                Id = 20, Titre = "Cours A", DepartementId = 1, 
                UtilisateurId = 50, Jour = DayOfWeek.Monday, HeureDebut = new TimeSpan(8, 0, 0), HeureFin = new TimeSpan(10, 0, 0) 
            });
            
            // Cours B n'a pas encore de créneau
            context.Cours.Add(new Cours { Id = 21, Titre = "Cours B", DepartementId = 1 });

            await context.SaveChangesAsync();
            var controller = GetController(context);

            // --- ACTION ---
            // On essaie d'assigner Prof 50 à Cours B au même moment (conflit !)
            var result = await controller.MettreAJourPosition(21, (int)DayOfWeek.Monday, "08:00", null, 50);

            // --- VÉRIFICATION ---
            var json = Assert.IsType<JsonResult>(result);
            Assert.False(JsonResultHelper.IsSuccess(json));                              // Doit échouer
            Assert.Contains("Un des professeurs", JsonResultHelper.GetMessage(json));   // Avec le bon message
        }

        // ============================================================
        // TEST 5 : La génération respecte les absences des profs
        // ============================================================
        // Ce test vérifie que si un professeur est marqué "absent" 
        // toute la semaine, le générateur de planning ne lui assigne 
        // aucun cours (le cours reste non-planifié).
        // Note : Disponible = false → Le prof est ABSENT ce créneau.
        // ============================================================
        [Fact]
        public async Task GenererPlanningAleatoire_ShouldRespectTeacherAbsence()
        {
            // --- PRÉPARATION ---
            using var context = GetDbContext();
            context.Departements.Add(new Departement { Id = 1, Nom = "IT" });
            context.Utilisateurs.Add(new Utilisateur { Id = 70, Nom = "Prof Absent", Role = "Professeur", DepartementId = 1, MotDePasseSalt = Guid.NewGuid() });
            context.Salles.Add(new Salle { Id = 1, Nom = "Salle A", Type = "Amphi" }); // Nécessaire : le contrôleur vérifie qu'il y a au moins une salle
            
            // Un cours assigné à ce professeur
            context.Cours.Add(new Cours { Id = 100, Titre = "Cours Unique", DepartementId = 1, UtilisateurId = 70, GroupeIds = "1" });
            context.Groupes.Add(new Groupe { Id = 1, Nom = "G1", DepartementId = 1, Niveau = "L1", Effectif = 20 });
            
            // On marque le prof ABSENT toute la journée, pour chaque jour de la semaine
            // (Disponible = false = absence dans la logique du contrôleur)
            var jours = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
            foreach (var j in jours)
            {
                context.Disponibilites.Add(new Disponibilite 
                { 
                    UtilisateurId = 70, 
                    Jour = j, 
                    HeureDebut = new TimeSpan(0, 0, 0),    // Début : minuit
                    HeureFin = new TimeSpan(23, 59, 59),   // Fin : 23h59 → toute la journée bloquée
                    Disponible = false                      // false = ABSENT (convention du contrôleur)
                });
            }
            
            await context.SaveChangesAsync();
            var controller = GetController(context);

            // --- ACTION ---
            // On tente de générer automatiquement le planning
            var result = await controller.GenererPlanningAleatoire();

            // --- VÉRIFICATION ---
            var json = Assert.IsType<JsonResult>(result);
            // La génération doit se terminer sans erreur système (success = true)
            Assert.True(JsonResultHelper.IsSuccess(json), JsonResultHelper.GetMessage(json));
            
            // Mais le cours ne doit PAS avoir été placé, car le prof est absent toute la semaine
            var c = await context.Cours.FindAsync(100);
            Assert.Equal(TimeSpan.Zero, c!.HeureDebut); // HeureDebut reste à 0 → cours non planifié
        }
    }

    // ============================================================
    // Classe TestSession : simulation minimale de la session HTTP
    // ============================================================
    // ASP.NET Core utilise une session pour stocker des infos comme 
    // l'ID de l'utilisateur connecté. Comme on n'a pas de vrai 
    // serveur HTTP dans les tests, on simule la session avec un 
    // simple dictionnaire en mémoire.
    // ============================================================
    public class TestSession : ISession
    {
        // Stockage interne : clé → valeur en bytes
        private readonly Dictionary<string, byte[]> _sessionStorage = new();

        public bool IsAvailable => true;                      // Toujours disponible
        public string Id => Guid.NewGuid().ToString();        // ID de session aléatoire
        public IEnumerable<string> Keys => _sessionStorage.Keys;

        public void Clear() => _sessionStorage.Clear();
        public Task CommitAsync(System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _sessionStorage.Remove(key);
        public void Set(string key, byte[] value) => _sessionStorage[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _sessionStorage.TryGetValue(key, out value);
    }
}
