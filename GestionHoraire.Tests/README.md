# GestionHoraire.Tests

Projet de tests unitaires pour l'application **GestionHoraire** (ASP.NET Core, .NET 9).

---

## 🛠️ Technologies utilisées

| Outil | Rôle |
|---|---|
| [xUnit](https://xunit.net/) | Framework de tests unitaires |
| [Microsoft.EntityFrameworkCore.InMemory](https://learn.microsoft.com/en-us/ef/core/providers/in-memory/) | Base de données en mémoire (isolation des tests) |
| [coverlet](https://github.com/coverlet-coverage/coverlet) | Couverture de code |
| .NET 9 | Plateforme cible |

---

## 📁 Structure du projet

```
GestionHoraire.Tests/
├── LoginTests.cs       # Tests du contrôleur d'authentification
├── PlanningTests.cs    # Tests du contrôleur de planning
├── UnitTest1.cs        # Fichier généré par défaut (vide)
└── README.md           # Ce fichier
```

---

## ✅ Tests — LoginController (`LoginTests.cs`)

Ces tests vérifient que l'authentification fonctionne correctement selon différents scénarios.

| # | Nom du test | Description |
|---|---|---|
| 1 | `Login_WithCorrectCredentials_ShouldRedirectToDashboard` | Une connexion avec le bon email et mot de passe redirige vers le tableau de bord et initialise la session (`UserId`). |
| 2 | `Login_WithWrongPassword_ShouldReturnViewWithError` | Un mauvais mot de passe retourne la vue de connexion avec un message d'erreur dans le `ViewBag`. |
| 3 | `Login_WithProvisionalPassword_ShouldRedirectToChangePassword` | Un mot de passe marqué comme provisoire (`EstMotDePasseProvisoire = true`) force la redirection vers `ChangeTempPassword`. |

### Mécanismes de simulation utilisés

- **`TestSession`** — Simule la session HTTP ASP.NET Core avec un dictionnaire en mémoire.
- **`MockTempData`** — Simule le `TempData` du contrôleur (messages flash entre requêtes).
- **`HashPassword`** — Reproduit le hachage SHA-256 avec sel (identique au `LoginController`) pour créer des utilisateurs de test avec un mot de passe connu.

---

## ✅ Tests — PlanningController (`PlanningTests.cs`)

Ces tests vérifient que la génération de planning et la mise à jour des créneaux respectent les contraintes métier.

| # | Nom du test | Description |
|---|---|---|
| 1 | `GenererPlanningAleatoire_ShouldPlaceCourses` | La génération automatique place les cours dans des créneaux valides (jour ≠ Dimanche, heure ≠ 00:00). |
| 2 | `Index_WithGroupeId_ShouldFilterCorrectly` | La vue de planning filtre correctement les cours par groupe (G1 ne voit pas les cours de G2). |
| 3 | `MettreAJourPosition_ShouldFail_WhenRoomIsOccupied` | Déplacer un cours dans une salle déjà occupée au même créneau retourne une erreur `"La salle est..."`. |
| 4 | `MettreAJourPosition_ShouldFail_WhenTeacherIsOccupied` | Assigner un professeur déjà en cours à la même heure retourne une erreur `"Un des professeurs..."`. |
| 5 | `GenererPlanningAleatoire_ShouldRespectTeacherAbsence` | Un cours assigné à un professeur absent toute la semaine reste non-planifié (`HeureDebut = 00:00`). |

### Mécanismes de simulation utilisés

- **`TestSession`** — Session simulée avec `DepartementId = 1` et `UserRole = "Responsable"`.
- **`JsonResultHelper`** — Classe statique utilitaire pour lire les propriétés `success` et `message` des `JsonResult` retournés par le contrôleur.

---

## ✅ Tests — AdminController (`AdminTests.cs`)

Ces tests vérifient le bon fonctionnement du tableau de bord "Admin" et des opérations CRUD sur les utilisateurs.

| # | Nom du test | Description |
|---|---|---|
| 1 | `Dashboard_ShouldReturnViewWithCounts` | Affiche correctement les compteurs (# Utilisateurs, # Salles, # Groupes, # Départements). |
| 2 | `Utilisateurs_WithRoleFilter_ShouldFilter` | Filtre correctement la liste des utilisateurs par rôle de recherche. |
| 3 | `CreateUser_ValidModel_ShouldAddAndRedirect` | Créer un nouvel utilisateur l'ajoute en base et redirige vers la liste. |
| 4 | `DeleteUser_ShouldRemoveUser` | Supprimer un utilisateur le retire définitivement de la base. |

---

## ✅ Tests — DisponibiliteController (`DisponibiliteTests.cs`)

Ces tests vérifient les règles d'accès et la génération des disponibilités horaires d'un professeur.

| # | Nom du test | Description |
|---|---|---|
| 1 | `Index_Professeur_ShouldOnlySeeOwn` | Un `Professeur` ne peut consulter que ses propres disponibilités (accès filtré). |
| 2 | `Create_Professeur_CannotCreateForOther` | Un `Professeur` n'a pas la permission (`Forbid`) de créer un créneau pour un collègue. |
| 3 | `Create_Duplicate_ShouldReturnError` | Tenter d'ajouter un créneau à une date/heure déjà prise lève une erreur `ModelState`. |
| 4 | `GenererDefaut_ShouldCreateWeeklySchedule` | Un `Responsable` peut générer automatiquement 25 créneaux par défaut pour un professeur. |

---

## ✅ Tests — ResponsableController (`ResponsableTests.cs`)

Ces tests vérifient la logique métier propre au Responsable de département (calcul des conflits, gestion de sécurité sur son département...).

| # | Nom du test | Description |
|---|---|---|
| 1 | `Index_ShouldCalculateConflitsCorrectly` | Vérifie que le tableau de bord compte correctement les cas de professeurs surbookés ou les conflits de salles à la même heure. |
| 2 | `TraiterDemande_ShouldUpdateStatus` | Vérifie que l'approbation d'une `Demande` met bien à jour son `Statut` et la `NoteResponsable` en base. |
| 3 | `CreerProf_ValidModel_ShouldHashPassword` | Garantit que le mot de passe provisoire est bien salé et haché (SHA-256) lors de la création d'un professeur. |
| 4 | `EditerProf_FromOtherDept_ShouldReturnNotFound` | S'assure de l'isolation de sécurité : un responsable ne peut pas modifier un professeur d'un *autre* département. |

---

## ▶️ Lancer les tests

### Via la ligne de commande

```bash
# Depuis la racine de la solution
dotnet test GestionHoraire.Tests/GestionHoraire.Tests.csproj
```

### Avec affichage détaillé

```bash
dotnet test GestionHoraire.Tests/GestionHoraire.Tests.csproj --logger "console;verbosity=detailed"
```

### Avec couverture de code (coverlet)

```bash
dotnet test GestionHoraire.Tests/GestionHoraire.Tests.csproj --collect:"XPlat Code Coverage"
```

### Via Visual Studio

Ouvrir le menu **Test → Exécuter tous les tests** ou utiliser l'**Explorateur de tests**.

---

## 🔍 Principes généraux

- Chaque test est **indépendant** : il utilise une base de données InMemory avec un nom unique (`Guid.NewGuid()`) pour éviter toute interférence entre les tests.
- Les tests suivent le pattern **Arrange / Act / Assert** (préparation → action → vérification).
- Aucun accès à une base de données réelle ni à un serveur HTTP n'est nécessaire pour exécuter les tests.
