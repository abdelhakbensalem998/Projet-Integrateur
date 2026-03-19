# Projet : Gestion des horaires 

## 1. Présentation du projet 

### 1.1. Contexte 
Dans le cadre du cours de Projet intégrateur au Collège La Cité – ITAC, ce projet vise à concevoir et développer une application web de gestion des horaires académiques.  
Le système permet la planification des cours, la gestion des salles, des professeurs et l’affectation des cours aux emplois du temps, tout en provoquant les conflits d'horaires. 

Actuellement, la planification manuelle des horaires peut entraîner des erreurs telles que : 
- Des conflits de salles, 
- Des conflits de disponibilité des professeurs, 
- Une mauvaise utilisation des ressources. 

L'application s'adresse principalement aux administrateurs et au responsable administratif, et a pour objectif de simplifier le travail administratif et d'optimiser l'organisation académique. 

### 1.2. Objectifs 
Les objectifs principaux du projet sont : 
- Permettre au responsable administratif de gérer les cours, les salles et les professeurs. 
- Permettre l'affectation des cours à des salles, des professeurs et des plages horaires. 
- Éviter automatiquement les conflits d'horaires (salles et professeurs). 
- Offrir une interface web simple, intuitive et sécurisée. 
- Assurer la confidentialité et la sécurité des données. 

**Critères d'évaluation :** 
- Fonctionnement correct des fonctionnalités 
- Absence de conflits d'horaires 
- Qualité du code et organisation du projet 
- Simplicité et clarté de l'interface utilisateur 
- Sécurité de l'accès et des données 

### 1.3. Périmètre du projet 

**Inclus dans le projet :** 
- Application web accessible via navigateur 
- Gestion des utilisateurs (administrateur, responsable administratif, professeurs) 
- Gestion des cours, salles et professeurs 
- Gestion des disponibilités des professeurs 
- Affectation des cours aux emplois du temps 
- Détection et blocage des conflits d'horaires 
- Authentification sécurisée 
- Vue calendrier / grille de planification. 

**Exclu du projet :** 
- Intégration avec des systèmes externes (ex : systèmes réels de l'établissement) 
- Gestion financière ou inscription des étudiants 
- Notifications par courriel ou SMS 
- Application mobile native 

## 2. Description fonctionnelle 

### 2.1. Besoins et exigences métiers 

**Problématique à résoudre** 
Le projet vise à résoudre les problèmes de conflits d’horaires, de mauvaise planification et de gestion manuelle de l'inefficacité des ressources académiques. 

**Cibles des utilisateurs** 

#### Administrateur 
- Gérer les comptes utilisateurs (création, activation/désactivation, attribution des rôles); 
- Accéder à l’ensemble des modules (vue globale); 
- Superviser la cohérence des données. 

#### Responsable administratif 
- Gérer les cours (création, modification, archivage/suppression contrôlée); 
- Gérer les salles (capacité, type, disponibilité); 
- Gérer les professeurs et consulter leurs disponibilités; 
- Planifier les cours (cours + salle + plage horaire) et affecter les professeurs; 
- Résoudre les conflits détectés par le système. 

#### Professeur (accès limité) 
- Se connecter à son espace; 
- Déclarer ses disponibilités pour une session/période; 
- Consulter son planning (lecture seule). 
- Effectuer des simples demandes à son supérieur (vacances, demande de matériels …) 

**Scénarios d'utilisation** 
- Le responsable administratif crée un cours et l'associe à un département. 
- Il ajoute une salle et définit sa capacité et son type. 
- Il ajoute un utilisateur. 
- Il ajoute un département. 
- Il accède au planning de différents départements. 
- Le système bloque automatiquement toute tentative de conflit. 
- L'utilisateur se connecte aux fonctionnalités autorisées. 

### 2.2. Fonctionnalités principales 

#### Gestion des utilisateurs 
- Créer un utilisateur (administrateur / responsable administratif / professeur) avec informations de base; 
- Modifier les informations et le rôle; 
- Désactiver ou supprimer un compte; 
- Consulter la liste des utilisateurs avec filtres. 

#### Authentification et sécurité d’accès (Page de connexion) 
- Connexion sécurisée par email + mot de passe. 
- Gestion de session : mémorisation de l’utilisateur connecté (UserId, rôle, département) et accès direct au tableau de bord si déjà connecté. 
- Redirection automatique selon le rôle : 
  - Administrateur : Tableau de bord Admin 
  - Responsable administratif : Tableau de bord Responsable 
  - Professeur : Tableau de bord Professeur 
- Déconnexion : fermeture de session et retour à la page de connexion. 
- Mot de passe provisoire : si le compte est en mode provisoire, l’utilisateur est forcé à changer son mot de passe avant d’accéder aux modules. 
- La configuration de la question de sécurité lors du changement de mot de passe provisoire. 
- Mot de passe oublié (processus sécurisé par étapes) : 
  - Saisie du courriel, 
  - Question de sécurité. 
  - Validation par code envoyé par courriel. 
  - Réinitialisation avec nouveau mot de passe. 
- Règles de mot de passe fort : minimum 8 caractères avec majuscule, minuscule, chiffre, caractère spécial. 
- Protection contre-attaques par essais multiples : verrouillage temporaire après plusieurs tentatives échouées. 

#### Module de gestion des cours 
- Créer un cours (nom, code, durée, programme, étape, type de salle….) 
- Modifier un cours 
- Supprimer un cours avec confirmation 
- Affecter un cours pour un ou plusieurs professeurs. 
- Consulter les cours avec filtres 

#### Module de gestion des salles 
- Ajouter une salle (code, type, capacité) 
- Modifier une salle 
- Supprimer une salle après vérification 
- Consulter les salles avec filtres 

#### Module de gestion des professeurs 
- Ajouter un professeur (nom, spécialité, courriel….) 
- Définir et modifier les disponibilités d’un professeur. 
- Supprimer un professeur après vérification de ses affectations. 
- Consulter les professeurs avec filtres (spécialité, disponibilité). 

#### Affectation et emploi du temps 
- Affecter un cours à une salle et une plage horaire 
- Vérifier la disponibilité des salles 
- Affecter un professeur à un cours 
- Vérifier la disponibilité et la spécialité du professeur 
- Empêcher toute double affectation 
- Afficher un calendrier des horaires 

#### Affectation des professeurs  
- Affecter un professeur à un cours planifié dans une salle. 
- Vérifier la disponibilité du professeur pour la date et la plage horaire choisies. 
- Empêcher l’affectation d’un professeur à deux cours simultanés. 
- S’assurer que la spécialité du professeur correspond au cours. 
- Afficher un message d’erreur en cas de conflit ou d’indisponibilité. 

### 2.3. Interface utilisateur 

**Ergonomie et design** 
- Interface claire et intuitive 
- Navigation par carte 
- Affichage sous forme de tableaux et calendriers 
- Design responsive. 

**Technologies recommandées** 
- HTML5 / CSS3 
- Framework ASP.NET Core MVC 
- JavaScript pour des interactions simples 

**Modèles filaires** 
- Page de connexion 
- Tableau de bord 
- Pages de gestion (Cours, Salles, Professeurs…) 
- Page de planification des horaires 

### 2.4. Conditions d'utilisation 
- Utilisation via navigateur web (Chrome, Edge, Firefox…) 
- Hébergement local (localhost) ou en ligne. 
- Accès réservé aux utilisateurs authentifiés. 
- Utilisation sur PC. 

## 3. Technique de description 

### 3.1. Technologies à utiliser 
- Backend : C# 
- Framework : ASP.NET Core MVC 
- Frontend : HTML5, CSS3, JavaScript 
- Base de données : SQL Server (SSMS), Entity Framework Core, hébergement en ligne. 

### 3.2. Architecture du système 
- Architecture client-serveur 
- Application web monolithique 
- Séparation des couches : Présentation (Vues), Logique métier (Contrôleurs), Accès aux données (Modèles). 

### 3.3. Sécurité 
- Authentification par email et mot de passe. 
- Politique de mot de passe fort (8+ caractères, maj, min, chiffre, spécial). 
- Stockage sécurisé des mots de passe (hash + salt). 
- Gestion des rôles et autorisations. 
- Protection des routes sensibles. 
- Gestion de session (connexion/déconnexion). 
- Mot de passe provisoire et question de sécurité à la première connexion. 
- Récupération de mot de passe sécurisée (OTP, question de sécurité). 
- Protection contre brute-force (verrouillage temporaire). 
- Journalisation des actions sensibles. 

### 3.4. Performance et évolutivité 
- Temps de réponse rapide 
- Prise en charge de plusieurs utilisateurs simultanés 
- Base de données optimisée (indexation). 

## 4. Planification et livrables 

### 4.1. Phases du projet 
- Analyse des besoins 
- Rédaction du cahier des charges 
- Conception (UML, architecture) 
- Développement de l'application 
- Tests et validation 
- Livraison finale avant le 26 avril 2026 

### 4.2. Biens livrables attendus 
- Application web fonctionnelle 
- Code source complet 
- Base de données 
- Cahier des charges 
- Diagrammes UML 

## 5. Modalité de validation 
- Tests fonctionnels des modules 
- Vérification des conflits d'horaires 
- Test de connexion et autorisations 
- Validation par démonstration du projet 
- Vérification de la conformité au cahier des charges 

## 6. Conclusion 
Ce projet de gestion des horaires permet de mettre en pratique les compétences acquises en programmation web, en analyse et en conception de systèmes. Il répond à un besoin réel de planification académique tout en respectant les contraintes de sécurité, de performance et de simplicité exigées dans un contexte académique. 
