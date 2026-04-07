# Tâches Jira - Sprint Final

Voici les tâches Jira (User Stories et Sous-tâches) pour le dernier sprint, axées sur les modules Responsable, Professeur et Planning/Affectations.

### 👥 Épic 1 : Finalisation du Module Responsable
**User Story :** En tant que Responsable, je veux avoir un tableau de bord complet et fonctionnel pour gérer les professeurs, les demandes et les affectations.

* **Tâche 1.1 :** Finaliser la vue d'index du tableau de bord Responsable (`Views\Responsable\Index.cshtml`).
  * _Description_ : Intégrer les derniers éléments statistiques et les raccourcis vers la gestion des professeurs et du planning.
  * _Estimation_ : 3 points
* **Tâche 1.2 :** Nettoyage et optimisation du `ResponsableController.cs`.
  * _Description_ : Refactoriser les méthodes redondantes, s'assurer que les autorisations basées sur les rôles (RBAC) sont correctement appliquées sur toutes les actions.
  * _Estimation_ : 5 points
* **Tâche 1.3 :** Gérer l'archivage et la suppression des demandes des professeurs.
  * _Description_ : S'assurer que les actions de traitement des demandes (acceptation, refus, archivage) sont totalement fonctionnelles et répercutées dans la base de données.
  * _Estimation_ : 3 points

### 🎓 Épic 2 : Finalisation du Module Professeur
**User Story :** En tant que Professeur, je veux pouvoir soumettre mes disponibilités et mes demandes/absences sans erreur.

* **Tâche 2.1 :** Consolider la gestion des disponibilités (`DisponibiliteController`).
  * _Description_ : Finir la liaison entre les disponibilités saisies par le professeur et l'affichage pour le responsable.
  * _Estimation_ : 3 points
* **Tâche 2.2 :** Interface de déclaration "Signaler et demande".
  * _Description_ : Vérifier l'upload/téléchargement des pièces jointes (plans de cours, justificatifs) associées aux demandes.
  * _Estimation_ : 5 points

### 📅 Épic 3 : Gestion du Planning et des Affectations (Groupes & Salles)
**User Story :** En tant que Responsable, je veux pouvoir affecter des professeurs aux cours, groupes et salles en fonction de leurs disponibilités.

* **Tâche 3.1 :** Optimisation de la vue de consultation des disponibilités (`Views\Affectations\VoirDispos.cshtml`).
  * _Description_ : Améliorer l'UI/UX pour que le responsable puisse visualiser facilement qui est disponible à quel moment.
  * _Estimation_ : 3 points
* **Tâche 3.2 :** Mise en place et finalisation de la vue des Groupes (`Views\Groupes\groupe.cshtml`).
  * _Description_ : Afficher la liste des groupes liés à chaque cours avec leurs professeurs affectés.
  * _Estimation_ : 3 points
* **Tâche 3.3 :** Mise en place et finalisation de la vue des Salles (`Views\Salles\index.cshtml`).
  * _Description_ : Gérer les conflits d'affectation de salles (éviter qu'une salle soit réservée par deux groupes en même temps).
  * _Estimation_ : 5 points
* **Tâche 3.4 :** Optimisation de l'affichage des Affectations.
  * _Description_ : Grouper les affectations par titre de cours et par professeur pour éviter les doublons visuels dans les tableaux croisés.
  * _Estimation_ : 2 points

### 🛠️ Épic 4 : Tests, Recette et Livraison du Sprint
**User Story :** En tant que membre de l'équipe, je veux m'assurer que le code livré est exempt de bugs majeurs avant la présentation finale.

* **Tâche 4.1 :** Tests croisés des rôles (Responsable vs. Professeur).
  * _Description_ : Vous connecter en tant que professeur, faire une demande, vous connecter en responsable, traiter la demande, vérifier l'affectation.
  * _Estimation_ : 2 points
* **Tâche 4.2 :** Nettoyage du code et fusion vers la branche principale.
  * _Description_ : Régler les potentiels conflits Git, retirer les `Console.WriteLine` ou commentaires non nécessaires, s'assurer que la base de données SQLite est stable.
  * _Estimation_ : 2 points
