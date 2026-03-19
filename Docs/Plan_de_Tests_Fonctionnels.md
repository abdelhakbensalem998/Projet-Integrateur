# 📘 Plan de Tests Fonctionnels : GestionHoraire
## 🗓️ Stratégie de Validation du Système de Planning

Ce document sert de guide de référence pour assurer la qualité et la robustesse de l'application. Il détaille l'approche de tests automatisés ainsi que les scénarios manuels essentiels.

---

##  1. Architecture et Méthode de Test
L'application utilise une approche moderne de tests unitaires et d'intégration :
- **Framework :** xUnit
- **Base de données :** In-Memory (EF Core) pour une exécution rapide.
- **Mocks :** Simulation des données de Session (Utilisateur connecté) et TempData (Messages d'état).

---

##  2. Authentification et Sécurité
*Garantir la protection des données et le contrôle d'accès par rôle.*

###  Tests Automatisés (`LoginTests.cs`)
1.  **Connexion Réussie :** Vérifie le hashage **SHA256 + Salt** et la redirection selon le rôle (Admin, Prof, etc.).
2.  **Identifiants Invalides :** Vérifie que le système rejette les mauvais mots de passe ou emails inexistants.
3.  **Premier Login :** Teste si un utilisateur avec un mot de passe provisoire est forcé de le changer avant d'accéder à l'application.

### 🟡 Tests Manuels Recommandés
- **Validation MDP :** Tenter de définir un mot de passe ne respectant pas les règles (ex: sans majuscule).
- **Déconnexion :** Vérifier que le bouton "Déconnexion" vide bien la session et interdit le retour en arrière.

---

## 📅 3. Moteur de Planning et Conflits
*S'assurer de l'exactitude des horaires et de l'indépendance des groupes.*

### ✅ Tests Automatisés (`PlanningTests.cs`)
1.  **Conflit de Salle :** Empêche de placer deux cours dans la même salle au même moment.
2.  **Conflit de Professeur :** Empêche un enseignant d'être assigné à deux cours simultanés.
3.  **Indisponibilités (Absences) :** Le générateur automatique saute systématiquement les créneaux où le professeur est marqué absent.
4.  **Isolation des Groupes :** Le groupe G1 ne doit jamais voir les cours du groupe G2 sur son calendrier.

### 🟡 Tests Manuels Recommandés
- **Glisser-Déposer (Drag & Drop) :** Vérifier visuellement que le déplacement manuel d'un cours met à jour les données.
- **Affichage Calendrier :** S'assurer que les cours longs (ex: 3h) s'affichent correctement sans chevauchement visuel.

---

## 👥 4. Gestion Administrative (Admin)
*Validation de la gestion globale des utilisateurs et des ressources.*

### ✅ Tests Automatisés (`AdminTests.cs`)
1.  **Dashboard Stats :** Vérifie que le tableau de bord affiche les bons compteurs (Utilisateurs, Salles, Groupes, Départements).
2.  **Filtrage par Rôle :** S'assure que la liste des utilisateurs peut être filtrée correctement (ex: voir uniquement les Professeurs).
3.  **Création d'Utilisateur :** Valide l'ajout d'un nouvel utilisateur et la redirection vers l'index.
4.  **Suppression :** Vérifie qu'un utilisateur est bien retiré de la base de données après suppression.

---

## ⏳ 5. Disponibilités et Demandes (Professeurs & Responsables)
*Garantir la gestion des créneaux et le processus de validation.*

### ✅ Tests Automatisés (`DisponibiliteTests.cs` & `ResponsableTests.cs`)
1.  **Isolation Professeur :** Un professeur ne peut voir que ses propres disponibilités.
2.  **Interdiction de Création :** Un professeur ne peut pas créer de créneau pour un autre collègue (ForbidResult).
3.  **Génération par Défaut :** Le Responsable peut générer automatiquement 25 créneaux hebdomadaires pour un prof.
4.  **Gestion des Conflits :** L'algorithme du Responsable détecte les chevauchements de salles et de profs surbookés.
5.  **Traitement des Demandes :** Validation du changement de statut (Approuvé/Refusé) d'une demande de professeur.

---

## 🚀 6. Exécution des Tests Automatisés

Pour lancer l'intégralité de la suite de tests (**21 tests à ce jour**), ouvrez un terminal et tapez :

```powershell
dotnet test
```

### ✅ Résultat Attendu :
Le terminal doit afficher la ligne suivante en fin d'exécution :
`Réussi!  - échec : 0, réussite : 21, ignorée(s) : 0, total : 21`

---

> [!NOTE]
> Ce document est vivant et doit être mis à jour à chaque nouvelle fonctionnalité ajoutée au projet.
