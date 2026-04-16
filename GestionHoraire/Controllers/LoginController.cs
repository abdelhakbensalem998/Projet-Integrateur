using GestionHoraire.Data;
using GestionHoraire.Models;
using GestionHoraire.Models.ViewModels;
using GestionHoraire.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GestionHoraire.Controllers
{
    public class LoginController : Controller
    {
        // Contexte EF pour accéder à la base de données
        private readonly AppDbContext _context;

        // Service utilisé pour la gestion du 2FA par Authenticator
        private readonly TwoFactorService _twoFactorService;

        // Nombre maximal de tentatives avant verrouillage
        private const int LOCKOUT_MAX_ATTEMPTS = 5;

        // Durée du verrouillage du compte
        private static readonly TimeSpan LOCKOUT_DURATION = TimeSpan.FromMinutes(10);

        // Nom du cookie utilisé pour mémoriser un appareil de confiance
        private const string TRUST_COOKIE = "gh_trusted";

        // Durée de confiance d’un appareil
        private static readonly TimeSpan TRUST_DURATION = TimeSpan.FromDays(30);

        // Clé de session temporaire pour stocker le secret Authenticator pendant la configuration
        private const string PendingAuthenticatorSecretSessionKey = "PendingAuthenticatorSecret";

        // Clé TempData pour transmettre les codes de secours à l’interface
        private const string RecoveryCodesTempDataKey = "RecoveryCodes";

        // Nom logique du provider 2FA par email
        private const string TwoFactorProviderEmail = "Email";

        // Nom logique du provider 2FA par application Authenticator
        private const string TwoFactorProviderAuthenticator = "Authenticator";

        public LoginController(AppDbContext context, TwoFactorService twoFactorService)
        {
            _context = context;
            _twoFactorService = twoFactorService;
        }

        // =========================
        // LOGIN
        // =========================

        [HttpGet]
        public IActionResult Index(string? expired = null)
        {
            // Vérifie si l’utilisateur est déjà connecté
            var userId = HttpContext.Session.GetInt32("UserId");
            var role = HttpContext.Session.GetString("UserRole");

            // Si une session existe déjà, redirige directement selon le rôle
            if (userId != null && !string.IsNullOrEmpty(role))
                return RedirectSelonRole(role);

            if (expired == "1")
                ViewBag.Error = "Votre session a expiré. Reconnectez-vous pour continuer.";

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Index(string email, string motDePasse)
        {
            // Validation minimale des champs
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(motDePasse))
            {
                ViewBag.Error = "Email ou mot de passe incorrect";
                return View();
            }

            // Recherche de l’utilisateur avec son département
            var user = _context.Utilisateurs
                .Include(u => u.Departement)
                .FirstOrDefault(u => u.Email == email);

            // Si l’utilisateur n’existe pas
            if (user == null)
            {
                ViewBag.Error = "Email ou mot de passe incorrect";
                return View();
            }

            // Vérifie si le compte est temporairement verrouillé
            if (user.LockoutUntil != null && user.LockoutUntil > DateTime.UtcNow)
            {
                ViewBag.Error = "Compte verrouillé temporairement. Réessayez plus tard.";
                LogSecurity(user.Id, "LOGIN_LOCKED", $"LockoutUntil={user.LockoutUntil:O}");
                return View();
            }

            // Vérifie le mot de passe
            if (!VerifierMotDePasseSHA256AvecSalt(motDePasse, user.MotDePasseSalt, user.MotDePasseHash))
            {
                user.FailedLoginAttempts += 1;

                // Si trop d’échecs, verrouille le compte
                if (user.FailedLoginAttempts >= LOCKOUT_MAX_ATTEMPTS)
                {
                    user.LockoutUntil = DateTime.UtcNow.Add(LOCKOUT_DURATION);
                    user.FailedLoginAttempts = 0;
                    LogSecurity(user.Id, "LOGIN_LOCKOUT_SET", $"Until={user.LockoutUntil:O}");
                }
                else
                {
                    LogSecurity(user.Id, "LOGIN_FAILED", $"Attempt={user.FailedLoginAttempts}");
                }

                _context.SaveChanges();
                ViewBag.Error = "Email ou mot de passe incorrect";
                return View();
            }

            // En cas de succès, réinitialise les compteurs d’échec
            user.FailedLoginAttempts = 0;
            user.LockoutUntil = null;
            _context.SaveChanges();
            LogSecurity(user.Id, "LOGIN_SUCCESS", null);

            // Si l’utilisateur utilise encore un mot de passe provisoire
            if (user.EstMotDePasseProvisoire)
            {
                TempData["Info"] = "Vous devez changer votre mot de passe provisoire.";
                return RedirectToAction("ChangeTempPassword", new { email = user.Email });
            }

            // Si le 2FA est requis et que l’appareil n’est pas reconnu
            if (RequiresTwoFactor(user) && !IsTrustedDevice(user.Id))
            {
                // On garde l’ID de l’utilisateur en attente de validation 2FA
                HttpContext.Session.SetInt32("Pending2FAUserId", user.Id);

                if (UsesAuthenticator(user))
                {
                    LogSecurity(user.Id, "2FA_CHALLENGE_STARTED", $"Provider={TwoFactorProviderAuthenticator}");
                }
                else
                {
                    // Envoie un code si le provider utilisé est l’email
                    Send2FACode(user, null);
                }

                return RedirectToAction("Verify2FA");
            }

            // Ouvre la session complète si tout est validé
            OpenFullSession(user);
            return RedirectSelonRole(user.Role ?? "");
        }

        // =========================
        // VERIFY 2FA
        // =========================

        [HttpGet]
        public IActionResult Verify2FA()
        {
            // Vérifie qu’un utilisateur est bien en attente de validation 2FA
            var pending = HttpContext.Session.GetInt32("Pending2FAUserId");
            if (pending == null) return RedirectToAction("Index");

            var user = _context.Utilisateurs.FirstOrDefault(u => u.Id == pending.Value);
            if (user == null)
            {
                HttpContext.Session.Remove("Pending2FAUserId");
                return RedirectToAction("Index");
            }

            // Envoie le ViewModel adapté au provider 2FA
            return View(BuildVerifyTwoFactorViewModel(user));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Verify2FA(string verificationCode, bool rememberDevice)
        {
            var pending = HttpContext.Session.GetInt32("Pending2FAUserId");
            if (pending == null) return RedirectToAction("Index");

            var user = _context.Utilisateurs.FirstOrDefault(u => u.Id == pending.Value);
            if (user == null)
            {
                HttpContext.Session.Remove("Pending2FAUserId");
                return RedirectToAction("Index");
            }

            // Vérifie que le code a été saisi
            if (string.IsNullOrWhiteSpace(verificationCode))
            {
                ViewBag.Error = "Veuillez entrer le code.";
                return View(BuildVerifyTwoFactorViewModel(user));
            }

            var usedBackupCode = false;
            var success = false;

            if (UsesAuthenticator(user))
            {
                // Validation avec le code TOTP Authenticator
                success = !string.IsNullOrWhiteSpace(user.AuthenticatorSecretKey) &&
                    _twoFactorService.ValidateTotp(user.AuthenticatorSecretKey, verificationCode);

                // Si le code principal échoue, on tente avec un code de secours
                if (!success)
                {
                    success = TryUseBackupCode(user.Id, verificationCode);
                    usedBackupCode = success;
                }
            }
            else
            {
                // Validation d’un code OTP reçu par email
                success = VerifyOtp(user.Id, "2FA", verificationCode.Trim(), out var otp);
                if (success)
                {
                    otp!.UsedAt = DateTime.UtcNow;
                    _context.SaveChanges();
                }
            }

            // En cas d’échec
            if (!success)
            {
                ViewBag.Error = UsesAuthenticator(user)
                    ? "Code Authenticator ou code de secours invalide."
                    : "Code invalide ou expiré.";

                LogSecurity(user.Id, "2FA_FAIL", $"Provider={GetTwoFactorProvider(user)}");
                return View(BuildVerifyTwoFactorViewModel(user));
            }

            // Si l’utilisateur choisit de mémoriser l’appareil
            if (rememberDevice)
                CreateTrustedDevice(user.Id, BuildTrustedDeviceName());

            // Retire l’état temporaire de session 2FA
            HttpContext.Session.Remove("Pending2FAUserId");

            // Ouvre la session normale
            OpenFullSession(user);

            LogSecurity(user.Id, "2FA_SUCCESS", $"Provider={GetTwoFactorProvider(user)};Remember={rememberDevice};BackupCode={usedBackupCode}");
            return RedirectSelonRole(user.Role ?? "");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Resend2FA([FromServices] EmailService emailService)
        {
            var pending = HttpContext.Session.GetInt32("Pending2FAUserId");
            if (pending == null) return RedirectToAction("Index");

            var user = _context.Utilisateurs.FirstOrDefault(u => u.Id == pending.Value);
            if (user == null)
            {
                HttpContext.Session.Remove("Pending2FAUserId");
                return RedirectToAction("Index");
            }

            // Si l’utilisateur utilise Authenticator, il n’y a rien à renvoyer
            if (UsesAuthenticator(user))
            {
                TempData["Info"] = "Le code est généré dans votre application Authenticator.";
                return RedirectToAction("Verify2FA");
            }

            // Sinon, renvoie un nouveau code par email
            Send2FACode(user, emailService);
            TempData["Info"] = "Nouveau code envoyé.";
            return RedirectToAction("Verify2FA");
        }

        // =========================
        // MANAGE 2FA
        // =========================

        [HttpGet]
        public IActionResult Manage2FA()
        {
            // Récupère l’utilisateur connecté
            var user = GetCurrentSessionUser();
            if (user == null) return RedirectToAction("Index");

            // Construit le ViewModel de gestion du 2FA
            return View(BuildManageTwoFactorViewModel(user, ReadRecoveryCodesFromTempData()));
        }

        [HttpGet]
        public IActionResult AuthenticatorQr()
        {
            var user = GetCurrentSessionUser();
            if (user == null) return RedirectToAction("Index");

            var pendingSecret = GetPendingAuthenticatorSecret();
            if (string.IsNullOrWhiteSpace(pendingSecret))
                return RedirectToAction("Manage2FA");

            var svg = BuildAuthenticatorQrCodeSvg(user, pendingSecret);
            return Content(svg, "image/svg+xml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult BeginAuthenticatorSetup()
        {
            var user = GetCurrentSessionUser();
            if (user == null) return RedirectToAction("Index");

            // Génère un nouveau secret temporaire pour l’application Authenticator
            SetPendingAuthenticatorSecret(_twoFactorService.GenerateSharedKey());

            TempData["Info"] = UsesAuthenticator(user)
                ? "Scannez le nouveau QR code puis confirmez avec un code pour remplacer la configuration actuelle."
                : "Scannez le QR code puis saisissez le code généré par votre application Authenticator.";

            LogSecurity(user.Id, "AUTHENTICATOR_SETUP_STARTED", null);
            return RedirectToAction("Manage2FA");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EnableAuthenticator(string verificationCode)
        {
            var user = GetCurrentSessionUser();
            if (user == null) return RedirectToAction("Index");

            // Récupère le secret temporaire généré au début de la configuration
            var pendingSecret = GetPendingAuthenticatorSecret();
            if (string.IsNullOrWhiteSpace(pendingSecret))
            {
                TempData["Error"] = "Commencez par générer un QR code de configuration.";
                return RedirectToAction("Manage2FA");
            }

            // Vérifie que l’utilisateur a entré un code
            if (string.IsNullOrWhiteSpace(verificationCode))
            {
                TempData["Error"] = "Entrez le code affiché par votre application Authenticator.";
                return RedirectToAction("Manage2FA");
            }

            // Vérifie la validité du code généré par l’application
            if (!_twoFactorService.ValidateTotp(pendingSecret, verificationCode))
            {
                TempData["Error"] = "Code Authenticator invalide.";
                LogSecurity(user.Id, "AUTHENTICATOR_ENABLE_FAIL", null);
                return RedirectToAction("Manage2FA");
            }

            // Génère et remplace les codes de secours
            var recoveryCodes = ReplaceBackupCodes(user.Id);

            // Active l’Authenticator comme provider principal
            user.AuthenticatorSecretKey = pendingSecret;
            user.AuthenticatorEnabledAt = DateTime.UtcNow;
            user.TwoFactorEnabled = true;
            user.TwoFactorProvider = TwoFactorProviderAuthenticator;

            // Révoque les appareils de confiance et nettoie le secret temporaire
            ClearTrustedDevices(user.Id);
            ClearPendingAuthenticatorSecret();

            _context.SaveChanges();

            // Transmet les nouveaux codes de secours à la vue
            TempData[RecoveryCodesTempDataKey] = JsonSerializer.Serialize(recoveryCodes);
            TempData["Success"] = "Application Authenticator activée.";

            LogSecurity(user.Id, "AUTHENTICATOR_ENABLED", $"BackupCodes={recoveryCodes.Count}");

            return RedirectToAction("Manage2FA");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RegenerateBackupCodes()
        {
            var user = GetCurrentSessionUser();
            if (user == null) return RedirectToAction("Index");

            // Empêche la régénération si Authenticator n’est pas actif
            if (!UsesAuthenticator(user))
            {
                TempData["Info"] = "Activez d'abord l'application Authenticator.";
                return RedirectToAction("Manage2FA");
            }

            // Remplace les anciens codes de secours
            var recoveryCodes = ReplaceBackupCodes(user.Id);
            _context.SaveChanges();

            TempData[RecoveryCodesTempDataKey] = JsonSerializer.Serialize(recoveryCodes);
            TempData["Success"] = "De nouveaux codes de secours ont été générés.";

            LogSecurity(user.Id, "BACKUP_CODES_REGENERATED", $"Count={recoveryCodes.Count}");

            return RedirectToAction("Manage2FA");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DisableAuthenticator()
        {
            var user = GetCurrentSessionUser();
            if (user == null) return RedirectToAction("Index");

            // Désactive complètement le 2FA Authenticator
            user.TwoFactorEnabled = false;
            user.TwoFactorProvider = null;
            user.AuthenticatorSecretKey = null;
            user.AuthenticatorEnabledAt = null;

            // Révoque tous les codes et appareils de confiance
            RevokeBackupCodes(user.Id);
            ClearTrustedDevices(user.Id);
            ClearPendingAuthenticatorSecret();

            _context.SaveChanges();

            TempData["Success"] = "L'application Authenticator a été désactivée.";
            LogSecurity(user.Id, "AUTHENTICATOR_DISABLED", null);

            return RedirectToAction("Manage2FA");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EnableEmailTwoFactor()
        {
            var user = GetCurrentSessionUser();
            if (user == null) return RedirectToAction("Index");

            if (string.IsNullOrWhiteSpace(user.Email))
            {
                TempData["Error"] = "Aucune adresse email n'est associee a ce compte.";
                return RedirectToAction("Manage2FA");
            }

            user.TwoFactorEnabled = true;
            user.TwoFactorProvider = TwoFactorProviderEmail;
            user.AuthenticatorSecretKey = null;
            user.AuthenticatorEnabledAt = null;

            RevokeBackupCodes(user.Id);
            ClearTrustedDevices(user.Id);
            ClearPendingAuthenticatorSecret();

            _context.SaveChanges();

            TempData["Success"] = "Le 2FA par email est active.";
            LogSecurity(user.Id, "EMAIL_2FA_ENABLED", null);

            return RedirectToAction("Manage2FA");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DisableEmailTwoFactor()
        {
            var user = GetCurrentSessionUser();
            if (user == null) return RedirectToAction("Index");

            if (!UsesEmailTwoFactor(user))
            {
                TempData["Info"] = "Le 2FA par email n'est pas actif.";
                return RedirectToAction("Manage2FA");
            }

            user.TwoFactorEnabled = false;
            user.TwoFactorProvider = null;
            ClearTrustedDevices(user.Id);

            _context.SaveChanges();

            TempData["Success"] = "Le 2FA par email est desactive.";
            LogSecurity(user.Id, "EMAIL_2FA_DISABLED", null);

            return RedirectToAction("Manage2FA");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RevokeTrustedDevice(int id)
        {
            var user = GetCurrentSessionUser();
            if (user == null) return RedirectToAction("Index");

            var device = _context.TrustedDevices.FirstOrDefault(td => td.Id == id && td.UserId == user.Id);
            if (device == null)
            {
                TempData["Info"] = "Cet appareil n'est plus dans la liste des appareils de confiance.";
                return RedirectToAction("Manage2FA");
            }

            var wasCurrentDevice = GetTrustedDeviceCookieId() == device.Id;

            _context.TrustedDevices.Remove(device);
            _context.SaveChanges();

            if (wasCurrentDevice)
                Response.Cookies.Delete(TRUST_COOKIE);

            TempData["Success"] = "Appareil de confiance supprime.";
            LogSecurity(user.Id, "TRUSTED_DEVICE_REVOKED", $"DeviceId={id};Current={wasCurrentDevice}");

            return RedirectToAction("Manage2FA");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RevokeAllTrustedDevices()
        {
            var user = GetCurrentSessionUser();
            if (user == null) return RedirectToAction("Index");

            var devices = _context.TrustedDevices.Where(td => td.UserId == user.Id).ToList();
            if (devices.Count > 0)
            {
                _context.TrustedDevices.RemoveRange(devices);
                _context.SaveChanges();
            }

            Response.Cookies.Delete(TRUST_COOKIE);

            TempData["Success"] = "Tous les appareils de confiance ont ete supprimes.";
            LogSecurity(user.Id, "TRUSTED_DEVICES_REVOKED", $"Count={devices.Count}");

            return RedirectToAction("Manage2FA");
        }

        [HttpGet]
        public IActionResult SecurityHistory()
        {
            var user = GetCurrentSessionUser();
            if (user == null) return RedirectToAction("Index");

            var logs = _context.SecurityLogs
                .Where(log => log.UserId == user.Id)
                .OrderByDescending(log => log.CreatedAt)
                .Take(100)
                .AsEnumerable()
                .Select(log => new SecurityLogItemViewModel
                {
                    Action = log.Action,
                    Label = GetSecurityLogLabel(log.Action),
                    Details = log.Details,
                    IpAddress = log.IpAddress,
                    CreatedAt = log.CreatedAt
                })
                .ToList();

            return View(new SecurityHistoryViewModel { Logs = logs });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Toggle2FA()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Index");

            // Cette action redirige maintenant vers la gestion dédiée du 2FA
            TempData["Info"] = "La gestion du 2FA se fait maintenant via l'application Authenticator.";
            return RedirectToAction("Manage2FA");
        }

        // =========================
        // LOGOUT
        // =========================

        [HttpGet]
        public IActionResult Logout()
        {
            // Supprime toute la session utilisateur
            HttpContext.Session.Clear();
            return RedirectToAction("Index");
        }

        // =========================
        // CHANGER MOT DE PASSE PROVISOIRE + question sécurité
        // =========================

        [HttpGet]
        public IActionResult ChangeTempPassword(string email)
        {
            // Envoie l’email et la liste des questions à la vue
            ViewBag.Email = email;
            ViewBag.Questions = GetQuestionsSecurite();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ChangeTempPassword(
            string email,
            string motDePasseProvisoire,
            string nouveauMotDePasse,
            string confirmerMotDePasse,
            string questionSecurite,
            string reponseSecurite,
            [FromServices] EmailService emailService)
        {
            ViewBag.Email = email;
            ViewBag.Questions = GetQuestionsSecurite();

            // Vérifie que tous les champs requis sont remplis
            if (string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(motDePasseProvisoire) ||
                string.IsNullOrWhiteSpace(nouveauMotDePasse) ||
                string.IsNullOrWhiteSpace(confirmerMotDePasse) ||
                string.IsNullOrWhiteSpace(questionSecurite) ||
                string.IsNullOrWhiteSpace(reponseSecurite))
            {
                ViewBag.Error = "Tous les champs sont obligatoires.";
                return View();
            }

            // Vérifie la confirmation
            if (nouveauMotDePasse != confirmerMotDePasse)
            {
                ViewBag.Error = "La confirmation ne correspond pas.";
                return View();
            }

            // Vérifie la complexité du mot de passe
            if (!MotDePasseValide(nouveauMotDePasse))
            {
                ViewBag.Error = "Mot de passe non conforme.";
                return View();
            }

            var user = _context.Utilisateurs.FirstOrDefault(u => u.Email == email);
            if (user == null)
            {
                ViewBag.Error = "Informations invalides.";
                return View();
            }

            // Vérifie que le compte est bien en mode mot de passe provisoire
            if (!user.EstMotDePasseProvisoire)
            {
                ViewBag.Error = "Aucun mot de passe provisoire actif.";
                return View();
            }

            // Vérifie le mot de passe provisoire
            if (!VerifierMotDePasseSHA256AvecSalt(motDePasseProvisoire, user.MotDePasseSalt, user.MotDePasseHash))
            {
                ViewBag.Error = "Informations invalides.";
                return View();
            }

            // Met à jour le mot de passe définitif
            Guid salt = Guid.NewGuid();
            user.MotDePasseSalt = salt;
            user.MotDePasseHash = CalculerSHA256AvecSalt(nouveauMotDePasse, salt);
            user.EstMotDePasseProvisoire = false;

            // Normalise et enregistre la réponse à la question de sécurité
            string normalized = (reponseSecurite ?? "").Trim().ToLowerInvariant();
            Guid repSalt = Guid.NewGuid();
            user.QuestionSecurite = questionSecurite;
            user.ReponseSecuriteSalt = repSalt;
            user.ReponseSecuriteHash = CalculerSHA256AvecSalt(normalized, repSalt);

            _context.SaveChanges();

            // Envoi d’un email de confirmation si possible
            try
            {
                emailService.Send(user.Email ?? "", "Mot de passe modifié", "Votre mot de passe a été modifié.");
            }
            catch { }

            LogSecurity(user.Id, "PWD_CHANGED_FROM_TEMP", null);

            TempData["Success"] = "Mot de passe changé et question de sécurité enregistrée. Vous pouvez vous connecter.";
            return RedirectToAction("Index");
        }

        // =========================
        // FORGOT PASSWORD
        // =========================

        [HttpGet]
        public IActionResult ForgotPassword() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ForgotPassword(string email)
        {
            // Vérifie que l’email a été fourni
            if (string.IsNullOrWhiteSpace(email))
            {
                ViewBag.Error = "Veuillez entrer votre email.";
                return View();
            }

            var user = _context.Utilisateurs.FirstOrDefault(u => u.Email == email);
            if (user == null)
            {
                ViewBag.Error = "Aucun compte trouvé avec cet email.";
                return View();
            }

            // Redirige vers le processus de réinitialisation
            return RedirectToAction("ForgotPasswordReset", new { id = user.Id });
        }

        // =========================
        // RESET PASSWORD
        // =========================

        [HttpGet]
        public IActionResult ForgotPasswordReset(int id, int? step)
        {
            var user = _context.Utilisateurs.FirstOrDefault(u => u.Id == id);
            if (user == null) return RedirectToAction("ForgotPassword");

            // Vérifie qu’une question de sécurité est configurée
            if (string.IsNullOrWhiteSpace(user.QuestionSecurite) ||
                user.ReponseSecuriteSalt == null ||
                user.ReponseSecuriteHash == null)
            {
                TempData["Info"] = "Aucune question de sécurité n'est configurée pour ce compte.";
                return RedirectToAction("ForgotPassword");
            }

            ViewBag.UserId = user.Id;
            ViewBag.Question = user.QuestionSecurite;

            // Si l’étape demandée est la saisie du code email
            if (step.HasValue && step.Value == 4)
            {
                ViewBag.Step = 4;
                return View();
            }

            // Si la question de sécurité a déjà été validée en session
            var qOk = HttpContext.Session.GetInt32("ResetQuestionVerifiedUserId");
            if (qOk != null && qOk.Value == user.Id)
            {
                ViewBag.Step = 4;
                return View();
            }

            // Par défaut, afficher l’étape de question de sécurité
            ViewBag.Step = 2;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ForgotPasswordReset(
            int userId,
            string step,
            string reponseSecurite,
            string codeEmail,
            string nouveauMotDePasse,
            string confirmerMotDePasse,
            [FromServices] EmailService emailService)
        {
            var user = _context.Utilisateurs.FirstOrDefault(u => u.Id == userId);
            if (user == null)
            {
                ViewBag.Error = "Informations invalides.";
                ViewBag.Step = 2;
                return View();
            }

            ViewBag.UserId = user.Id;
            ViewBag.Question = user.QuestionSecurite;

            // Étape 2 : vérification de la réponse à la question de sécurité
            if (step == "2")
            {
                if (!VerifierReponseSecurite(user, reponseSecurite))
                {
                    ViewBag.Error = "Réponse incorrecte.";
                    ViewBag.Step = 2;
                    LogSecurity(user.Id, "PWD_RESET_Q_FAIL", null);
                    return View();
                }

                // Mémorise en session que la question a été validée
                HttpContext.Session.SetInt32("ResetQuestionVerifiedUserId", user.Id);

                // Génère et envoie un code OTP par email
                CreateOtp(user.Id, "RESET", TimeSpan.FromMinutes(10));
                var code = (string)HttpContext.Items["__otp_code"]!;

                try
                {
                    emailService.Send(user.Email ?? "", "Code de réinitialisation", $"Votre code est : {code}\nValable 10 minutes.");
                }
                catch { }

                TempData["Info"] = "Un code a été envoyé par email. Entrez-le pour continuer.";
                ViewBag.Step = 4;
                LogSecurity(user.Id, "PWD_RESET_CODE_SENT", null);
                return View();
            }

            // Étape 4 : validation du code reçu par email
            if (step == "4")
            {
                var qOk = HttpContext.Session.GetInt32("ResetQuestionVerifiedUserId");
                if (qOk == null || qOk.Value != user.Id)
                {
                    ViewBag.Error = "Veuillez répondre à la question avant.";
                    ViewBag.Step = 2;
                    return View();
                }

                if (!VerifyOtp(user.Id, "RESET", (codeEmail ?? "").Trim(), out var otp))
                {
                    ViewBag.Error = "Code invalide ou expiré.";
                    ViewBag.Step = 4;
                    LogSecurity(user.Id, "PWD_RESET_CODE_FAIL", null);
                    return View();
                }

                // Marque le code OTP comme utilisé
                otp!.UsedAt = DateTime.UtcNow;
                _context.SaveChanges();

                // Mémorise que l’étape email est validée
                HttpContext.Session.SetInt32("ResetEmailVerifiedUserId", user.Id);
                ViewBag.Step = 3;
                return View();
            }

            // Étape 3 : définition du nouveau mot de passe
            if (step == "3")
            {
                var ok = HttpContext.Session.GetInt32("ResetEmailVerifiedUserId");
                if (ok == null || ok.Value != user.Id)
                {
                    ViewBag.Error = "Veuillez valider le code email avant.";
                    ViewBag.Step = 4;
                    return View();
                }

                // Vérifie la confirmation
                if (nouveauMotDePasse != confirmerMotDePasse)
                {
                    ViewBag.Error = "La confirmation ne correspond pas.";
                    ViewBag.Step = 3;
                    return View();
                }

                // Vérifie la complexité du mot de passe
                if (!MotDePasseValide(nouveauMotDePasse))
                {
                    ViewBag.Error = "Mot de passe non conforme.";
                    ViewBag.Step = 3;
                    return View();
                }

                // Empêche de réutiliser l’ancien mot de passe
                if (VerifierMotDePasseSHA256AvecSalt(nouveauMotDePasse, user.MotDePasseSalt, user.MotDePasseHash))
                {
                    ViewBag.Error = "Le nouveau mot de passe doit être différent de l'ancien.";
                    ViewBag.Step = 3;
                    return View();
                }

                // Sauvegarde le nouveau mot de passe
                Guid salt = Guid.NewGuid();
                user.MotDePasseSalt = salt;
                user.MotDePasseHash = CalculerSHA256AvecSalt(nouveauMotDePasse, salt);
                user.EstMotDePasseProvisoire = false;

                _context.SaveChanges();

                // Email d’information après réinitialisation
                try
                {
                    emailService.Send(user.Email ?? "", "Mot de passe réinitialisé", "Votre mot de passe a été réinitialisé.");
                }
                catch { }

                LogSecurity(user.Id, "PWD_RESET_SUCCESS", null);

                // Nettoie les variables de session utilisées pour le reset
                HttpContext.Session.Remove("ResetQuestionVerifiedUserId");
                HttpContext.Session.Remove("ResetEmailVerifiedUserId");

                TempData["Success"] = "Mot de passe réinitialisé. Vous pouvez vous connecter.";
                return RedirectToAction("Index");
            }

            ViewBag.Step = 2;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ResendResetCode(int userId, [FromServices] EmailService emailService)
        {
            // Vérifie que la réponse à la question a bien été validée
            var qOk = HttpContext.Session.GetInt32("ResetQuestionVerifiedUserId");
            if (qOk == null || qOk.Value != userId)
            {
                TempData["Info"] = "Veuillez répondre à la question avant.";
                return RedirectToAction("ForgotPasswordReset", new { id = userId });
            }

            // Génère un nouveau code OTP de réinitialisation
            CreateOtp(userId, "RESET", TimeSpan.FromMinutes(10));
            var code = (string)HttpContext.Items["__otp_code"]!;

            try
            {
                emailService.Send(
                    _context.Utilisateurs.Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefault() ?? "",
                    "Nouveau code",
                    $"Votre nouveau code est : {code}\nValable 10 minutes.");
            }
            catch { }

            TempData["Info"] = "Nouveau code envoyé.";
            return RedirectToAction("ForgotPasswordReset", new { id = userId, step = 4 });
        }

        // =========================
        // HELPERS
        // =========================

        private Utilisateur? GetCurrentSessionUser()
        {
            // Récupère l’ID de l’utilisateur connecté dans la session
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return null;

            // Retourne l’utilisateur correspondant
            return _context.Utilisateurs.FirstOrDefault(u => u.Id == userId.Value);
        }

        private void OpenFullSession(Utilisateur user)
        {
            // Stocke les informations principales de l’utilisateur dans la session
            HttpContext.Session.SetInt32("UserId", user.Id);
            HttpContext.Session.SetString("UserRole", user.Role ?? "");
            HttpContext.Session.SetString("UserNom", user.Nom ?? "");
            HttpContext.Session.SetString("UserEmail", user.Email ?? "");

            // Stocke aussi le département si présent
            if (user.DepartementId.HasValue)
                HttpContext.Session.SetInt32("DepartementId", user.DepartementId.Value);
        }

        private IActionResult RedirectSelonRole(string role)
        {
            // Nettoie la valeur du rôle puis redirige vers le bon contrôleur
            role = (role ?? "").Trim();

            return role switch
            {
                "Administrateur" => RedirectToAction("Index", "Admin"),
                "ResponsableDépartement" => RedirectToAction("Index", "Responsable"),
                "Professeur" => RedirectToAction("Index", "Professeur"),
                _ => RedirectToAction("Index", "Home")
            };
        }

        private static string[] GetQuestionsSecurite() => new[]
        {
            "Quel est le nom de ta première école ?",
            "Quel est le prénom de ta mère ?",
            "Quel est le nom de ton premier animal ?",
            "Dans quelle ville es-tu née ?",
            "Quel est ton plat préféré ?"
        };

        private void LogSecurity(int? userId, string action, string? details)
        {
            // Ajoute une trace dans le journal de sécurité
            _context.SecurityLogs.Add(new SecurityLog
            {
                UserId = userId,
                Action = action,
                Details = details,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                CreatedAt = DateTime.UtcNow
            });

            _context.SaveChanges();
        }

        private static bool MotDePasseValide(string motDePasse)
        {
            // Vérifie longueur minimale
            if (string.IsNullOrWhiteSpace(motDePasse) || motDePasse.Length < 8) return false;

            // Vérifie les critères de complexité
            bool hasUpper = motDePasse.Any(char.IsUpper);
            bool hasLower = motDePasse.Any(char.IsLower);
            bool hasDigit = motDePasse.Any(char.IsDigit);
            bool hasSpecial = motDePasse.Any(ch => !char.IsLetterOrDigit(ch));

            return hasUpper && hasLower && hasDigit && hasSpecial;
        }

        private static bool VerifierMotDePasseSHA256AvecSalt(string motDePasse, Guid saltGuid, byte[] hashStocke)
        {
            // Recalcule le hash et compare de façon sécurisée
            byte[] hashCalcule = CalculerSHA256AvecSalt(motDePasse, saltGuid);
            return CryptographicOperations.FixedTimeEquals(hashCalcule, hashStocke);
        }

        private static byte[] CalculerSHA256AvecSalt(string motDePasse, Guid saltGuid)
        {
            // Convertit le salt et le mot de passe en bytes
            byte[] salt = saltGuid.ToByteArray();
            byte[] mdpBytes = Encoding.UTF8.GetBytes(motDePasse);

            // Concatène salt + mot de passe
            byte[] input = new byte[salt.Length + mdpBytes.Length];
            Buffer.BlockCopy(salt, 0, input, 0, salt.Length);
            Buffer.BlockCopy(mdpBytes, 0, input, salt.Length, mdpBytes.Length);

            // Retourne le hash SHA256 du tout
            return SHA256.HashData(input);
        }

        private static bool VerifierReponseSecurite(Utilisateur user, string reponse)
        {
            // Vérifie que les données nécessaires existent
            if (user.ReponseSecuriteSalt == null || user.ReponseSecuriteHash == null) return false;

            // Normalise la réponse utilisateur avant hash
            string normalized = (reponse ?? "").Trim().ToLowerInvariant();
            byte[] hashCalcule = CalculerSHA256AvecSalt(normalized, user.ReponseSecuriteSalt.Value);

            return CryptographicOperations.FixedTimeEquals(hashCalcule, user.ReponseSecuriteHash);
        }

        private bool RequiresTwoFactor(Utilisateur user)
        {
            // Le 2FA est requis si l’utilisateur utilise Authenticator ou email
            return UsesAuthenticator(user) || UsesEmailTwoFactor(user);
        }

        private bool UsesAuthenticator(Utilisateur user)
        {
            // Vérifie si l’utilisateur utilise l’application Authenticator
            return user.TwoFactorEnabled &&
                string.Equals(user.TwoFactorProvider, TwoFactorProviderAuthenticator, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(user.AuthenticatorSecretKey);
        }

        private bool UsesEmailTwoFactor(Utilisateur user)
        {
            // Si le 2FA est activé sans Authenticator, on considère que c’est par email
            return user.TwoFactorEnabled && !UsesAuthenticator(user);
        }

        private string GetTwoFactorProvider(Utilisateur user)
        {
            // Retourne le provider actuel
            return UsesAuthenticator(user) ? TwoFactorProviderAuthenticator : TwoFactorProviderEmail;
        }

        private string GetTwoFactorProviderLabel(Utilisateur user)
        {
            // Retourne un libellé lisible pour l’interface
            return UsesAuthenticator(user)
                ? "Application Authenticator"
                : UsesEmailTwoFactor(user)
                    ? "Code par email"
                    : "Aucune";
        }

        private string GetAuthenticatorIssuer() => "GestionHoraire";

        private string GetAuthenticatorAccountLabel(Utilisateur user)
        {
            // Utilise l’email si disponible, sinon le nom, sinon un identifiant par défaut
            return !string.IsNullOrWhiteSpace(user.Email)
                ? user.Email!
                : !string.IsNullOrWhiteSpace(user.Nom)
                    ? user.Nom!
                    : $"Utilisateur-{user.Id}";
        }

        private ManageTwoFactorViewModel BuildManageTwoFactorViewModel(Utilisateur user, IReadOnlyList<string>? recoveryCodes = null)
        {
            RemoveExpiredTrustedDevices(user.Id);

            // Secret temporaire si une configuration Authenticator est en cours
            var pendingSecret = GetPendingAuthenticatorSecret();
            var isSetupInProgress = !string.IsNullOrWhiteSpace(pendingSecret);

            return new ManageTwoFactorViewModel
            {
                IsTwoFactorEnabled = user.TwoFactorEnabled,
                IsAuthenticatorConfigured = UsesAuthenticator(user),
                IsEmailTwoFactorEnabled = UsesEmailTwoFactor(user),
                IsSetupInProgress = isSetupInProgress,
                ProviderLabel = GetTwoFactorProviderLabel(user),

                // Clé manuelle lisible à saisir dans l’app
                ManualEntryKey = isSetupInProgress ? _twoFactorService.FormatSharedKey(pendingSecret!) : null,

                // QR code SVG pour l’application Authenticator
                QrCodeSvg = isSetupInProgress ? BuildAuthenticatorQrCodeSvg(user, pendingSecret!) : null,

                AccountLabel = GetAuthenticatorAccountLabel(user),
                RemainingBackupCodes = CountRemainingBackupCodes(user.Id),
                RecoveryCodes = recoveryCodes ?? Array.Empty<string>(),
                TrustedDevices = BuildTrustedDeviceViewModels(user.Id)
            };
        }

        private string BuildAuthenticatorQrCodeSvg(Utilisateur user, string sharedKey)
        {
            return _twoFactorService.GenerateQrCodeSvg(
                _twoFactorService.BuildOtpAuthUri(
                    GetAuthenticatorIssuer(),
                    GetAuthenticatorAccountLabel(user),
                    sharedKey));
        }

        private IReadOnlyList<TrustedDeviceViewModel> BuildTrustedDeviceViewModels(int userId)
        {
            var currentDeviceId = GetTrustedDeviceCookieId();

            return _context.TrustedDevices
                .Where(td => td.UserId == userId && td.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(td => td.CreatedAt)
                .Select(td => new TrustedDeviceViewModel
                {
                    Id = td.Id,
                    DeviceName = string.IsNullOrWhiteSpace(td.DeviceName) ? "Appareil Web" : td.DeviceName!,
                    CreatedAt = td.CreatedAt,
                    ExpiresAt = td.ExpiresAt,
                    IsCurrentDevice = currentDeviceId.HasValue && td.Id == currentDeviceId.Value
                })
                .ToList();
        }

        private static string GetSecurityLogLabel(string action)
        {
            return action switch
            {
                "LOGIN_SUCCESS" => "Connexion reussie",
                "LOGIN_FAILED" => "Echec de connexion",
                "LOGIN_LOCKED" => "Connexion bloquee",
                "LOGIN_LOCKOUT_SET" => "Compte verrouille temporairement",
                "2FA_CHALLENGE_STARTED" => "Verification 2FA demandee",
                "2FA_CODE_SENT" => "Code 2FA envoye",
                "2FA_FAIL" => "Echec 2FA",
                "2FA_SUCCESS" => "Verification 2FA reussie",
                "AUTHENTICATOR_SETUP_STARTED" => "Configuration Authenticator commencee",
                "AUTHENTICATOR_ENABLE_FAIL" => "Echec activation Authenticator",
                "AUTHENTICATOR_ENABLED" => "Authenticator active",
                "AUTHENTICATOR_DISABLED" => "Authenticator desactive",
                "EMAIL_2FA_ENABLED" => "2FA par email active",
                "EMAIL_2FA_DISABLED" => "2FA par email desactive",
                "BACKUP_CODES_REGENERATED" => "Backup codes regeneres",
                "TRUSTED_DEVICE_REVOKED" => "Appareil de confiance supprime",
                "TRUSTED_DEVICES_REVOKED" => "Appareils de confiance supprimes",
                "PWD_CHANGED_FROM_TEMP" => "Mot de passe provisoire remplace",
                "PWD_RESET_Q_FAIL" => "Echec question de securite",
                "PWD_RESET_CODE_SENT" => "Code de reinitialisation envoye",
                "PWD_RESET_CODE_FAIL" => "Echec code de reinitialisation",
                "PWD_RESET_SUCCESS" => "Mot de passe reinitialise",
                _ => action
            };
        }

        private VerifyTwoFactorViewModel BuildVerifyTwoFactorViewModel(Utilisateur user)
        {
            // ViewModel spécifique pour l’application Authenticator
            if (UsesAuthenticator(user))
            {
                return new VerifyTwoFactorViewModel
                {
                    ProviderLabel = "Application Authenticator",
                    Instruction = "Entrez le code à 6 chiffres affiché dans votre application Authenticator.",
                    InputLabel = "Code Authenticator ou code de secours",
                    AllowResend = false,
                    AllowBackupCodes = true,
                    RemainingBackupCodes = CountRemainingBackupCodes(user.Id)
                };
            }

            // ViewModel spécifique au 2FA par email
            return new VerifyTwoFactorViewModel
            {
                ProviderLabel = "Code par email",
                Instruction = "Entrez le code reçu par email pour continuer.",
                InputLabel = "Code de vérification",
                AllowResend = true,
                AllowBackupCodes = false
            };
        }

        private string? GetPendingAuthenticatorSecret()
        {
            // Lit le secret temporaire dans la session
            return HttpContext.Session.GetString(PendingAuthenticatorSecretSessionKey);
        }

        private void SetPendingAuthenticatorSecret(string secret)
        {
            // Stocke le secret temporaire dans la session
            HttpContext.Session.SetString(PendingAuthenticatorSecretSessionKey, secret);
        }

        private void ClearPendingAuthenticatorSecret()
        {
            // Supprime le secret temporaire de la session
            HttpContext.Session.Remove(PendingAuthenticatorSecretSessionKey);
        }

        private IReadOnlyList<string> ReadRecoveryCodesFromTempData()
        {
            // Vérifie si TempData contient les codes de secours
            if (!TempData.TryGetValue(RecoveryCodesTempDataKey, out var rawValue) || rawValue == null)
                return Array.Empty<string>();

            try
            {
                return JsonSerializer.Deserialize<string[]>(rawValue.ToString() ?? string.Empty) ?? Array.Empty<string>();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private int CountRemainingBackupCodes(int userId)
        {
            // Compte les codes encore valides et non utilisés
            return _context.BackupCodes.Count(b => b.UserId == userId && b.UsedAt == null && b.RevokedAt == null);
        }

        private List<string> ReplaceBackupCodes(int userId, int count = 8)
        {
            // Révoque d’abord les anciens codes
            RevokeBackupCodes(userId);

            // Génère de nouveaux codes bruts
            var codes = _twoFactorService.GenerateBackupCodes(count).ToList();

            // Stocke uniquement les versions hashées
            foreach (var code in codes)
            {
                var salt = Guid.NewGuid();
                _context.BackupCodes.Add(new BackupCode
                {
                    UserId = userId,
                    CodeSalt = salt,
                    CodeHash = _twoFactorService.HashBackupCode(code, salt),
                    CreatedAt = DateTime.UtcNow
                });
            }

            return codes;
        }

        private void RevokeBackupCodes(int userId)
        {
            // Récupère les anciens codes actifs
            var existingCodes = _context.BackupCodes
                .Where(b => b.UserId == userId && b.UsedAt == null && b.RevokedAt == null)
                .ToList();

            // Les marque comme révoqués
            foreach (var code in existingCodes)
                code.RevokedAt = DateTime.UtcNow;
        }

        private bool TryUseBackupCode(int userId, string code)
        {
            // Normalise le code saisi
            var normalized = _twoFactorService.NormalizeBackupCode(code);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            // Récupère les codes valides
            var backupCodes = _context.BackupCodes
                .Where(b => b.UserId == userId && b.UsedAt == null && b.RevokedAt == null)
                .ToList();

            // Compare le code saisi avec chaque hash stocké
            foreach (var backupCode in backupCodes)
            {
                var calculatedHash = _twoFactorService.HashBackupCode(normalized, backupCode.CodeSalt);
                if (!CryptographicOperations.FixedTimeEquals(calculatedHash, backupCode.CodeHash))
                    continue;

                // Marque le code comme utilisé
                backupCode.UsedAt = DateTime.UtcNow;
                _context.SaveChanges();
                return true;
            }

            return false;
        }

        // ===== OTP helpers =====

        private static string Generate6DigitCode()
        {
            // Génère un entier pseudo-aléatoire sécurisé puis le convertit en code sur 6 chiffres
            var bytes = new byte[4];
            RandomNumberGenerator.Fill(bytes);
            int val = BitConverter.ToInt32(bytes, 0) & 0x7FFFFFFF;
            return (val % 1000000).ToString("D6");
        }

        private EmailOtp CreateOtp(int userId, string purpose, TimeSpan ttl)
        {
            // Désactive les anciens OTP encore actifs pour ce user et ce type d’usage
            var olds = _context.EmailOtps
                .Where(o => o.UserId == userId && o.Purpose == purpose && o.UsedAt == null)
                .ToList();

            foreach (var o in olds)
                o.UsedAt = DateTime.UtcNow;

            // Génère un nouveau code OTP
            string code = Generate6DigitCode();
            Guid salt = Guid.NewGuid();
            byte[] hash = CalculerSHA256AvecSalt(code, salt);

            var otp = new EmailOtp
            {
                UserId = userId,
                Purpose = purpose,
                CodeSalt = salt,
                CodeHash = hash,
                ExpiresAt = DateTime.UtcNow.Add(ttl),
                LastSentAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            _context.EmailOtps.Add(otp);
            _context.SaveChanges();

            // Stocke temporairement le code brut pour pouvoir l’envoyer par email
            HttpContext.Items["__otp_code"] = code;
            return otp;
        }

        private bool VerifyOtp(int userId, string purpose, string code, out EmailOtp? otp)
        {
            // Prend le dernier OTP non utilisé correspondant
            otp = _context.EmailOtps
                .Where(o => o.UserId == userId && o.Purpose == purpose && o.UsedAt == null)
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefault();

            if (otp == null) return false;
            if (otp.ExpiresAt <= DateTime.UtcNow) return false;
            if (otp.Attempts >= 5) return false;

            // Incrémente le nombre d’essais
            otp.Attempts += 1;
            _context.SaveChanges();

            // Compare le code saisi avec le hash stocké
            byte[] calc = CalculerSHA256AvecSalt(code, otp.CodeSalt);
            return CryptographicOperations.FixedTimeEquals(calc, otp.CodeHash);
        }

        private void Send2FACode(Utilisateur user, EmailService? emailService)
        {
            // Récupère EmailService depuis le conteneur si besoin
            emailService ??= HttpContext.RequestServices.GetService(typeof(EmailService)) as EmailService;

            // Crée un OTP pour le 2FA
            CreateOtp(user.Id, "2FA", TimeSpan.FromMinutes(10));
            var code = (string)HttpContext.Items["__otp_code"]!;

            try
            {
                emailService?.Send(user.Email ?? "", "Code de connexion (2FA)", $"Votre code est : {code}\nValable 10 minutes.");
            }
            catch { }

            LogSecurity(user.Id, "2FA_CODE_SENT", $"Provider={TwoFactorProviderEmail}");
        }

        // ===== Trusted device =====

        private string BuildTrustedDeviceName()
        {
            var userAgent = Request.Headers["User-Agent"].ToString();
            if (string.IsNullOrWhiteSpace(userAgent))
                return "Navigateur Web";

            var browser = userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Microsoft Edge" :
                userAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) ? "Chrome" :
                userAgent.Contains("Firefox/", StringComparison.OrdinalIgnoreCase) ? "Firefox" :
                userAgent.Contains("Safari/", StringComparison.OrdinalIgnoreCase) ? "Safari" :
                "Navigateur Web";

            var system = userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows" :
                userAgent.Contains("Mac OS", StringComparison.OrdinalIgnoreCase) ? "macOS" :
                userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android" :
                userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ? "iPhone" :
                userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux" :
                "appareil inconnu";

            return $"{browser} sur {system}";
        }

        private void CreateTrustedDevice(int userId, string deviceName)
        {
            // Génère un token aléatoire unique pour l’appareil
            var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            var salt = Guid.NewGuid();
            var hash = CalculerSHA256AvecSalt(token, salt);

            var td = new TrustedDevice
            {
                UserId = userId,
                DeviceName = deviceName,
                TokenSalt = salt,
                TokenHash = hash,
                ExpiresAt = DateTime.UtcNow.Add(TRUST_DURATION),
                CreatedAt = DateTime.UtcNow
            };

            _context.TrustedDevices.Add(td);
            _context.SaveChanges();

            // Écrit le cookie contenant l’ID et le token brut
            Response.Cookies.Append(TRUST_COOKIE, $"{td.Id}:{token}", new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.Add(TRUST_DURATION)
            });
        }

        private void ClearTrustedDevices(int userId)
        {
            // Supprime tous les appareils de confiance liés à l’utilisateur
            var trustedDevices = _context.TrustedDevices.Where(td => td.UserId == userId).ToList();
            if (trustedDevices.Count > 0)
                _context.TrustedDevices.RemoveRange(trustedDevices);

            // Supprime aussi le cookie côté client
            Response.Cookies.Delete(TRUST_COOKIE);
        }

        private int? GetTrustedDeviceCookieId()
        {
            if (!Request.Cookies.TryGetValue(TRUST_COOKIE, out var value)) return null;
            if (string.IsNullOrWhiteSpace(value)) return null;

            var parts = value.Split(':');
            if (parts.Length != 2) return null;

            return int.TryParse(parts[0], out var id) ? id : null;
        }

        private void RemoveExpiredTrustedDevices(int userId)
        {
            var now = DateTime.UtcNow;
            var expiredDevices = _context.TrustedDevices
                .Where(td => td.UserId == userId && td.ExpiresAt <= now)
                .ToList();

            if (expiredDevices.Count == 0)
                return;

            var currentDeviceId = GetTrustedDeviceCookieId();
            if (currentDeviceId.HasValue && expiredDevices.Any(td => td.Id == currentDeviceId.Value))
                Response.Cookies.Delete(TRUST_COOKIE);

            _context.TrustedDevices.RemoveRange(expiredDevices);
            _context.SaveChanges();
        }

        private bool IsTrustedDevice(int userId)
        {
            // Vérifie si le cookie existe
            if (!Request.Cookies.TryGetValue(TRUST_COOKIE, out var value)) return false;
            if (string.IsNullOrWhiteSpace(value)) return false;

            // Le cookie doit contenir "id:token"
            var parts = value.Split(':');
            if (parts.Length != 2) return false;

            if (!int.TryParse(parts[0], out var id)) return false;
            var token = parts[1];

            // Recherche l’appareil en base
            var td = _context.TrustedDevices.FirstOrDefault(x => x.Id == id && x.UserId == userId);
            if (td == null) return false;
            if (td.ExpiresAt <= DateTime.UtcNow)
            {
                _context.TrustedDevices.Remove(td);
                _context.SaveChanges();
                Response.Cookies.Delete(TRUST_COOKIE);
                return false;
            }

            // Recalcule le hash et compare
            var calc = CalculerSHA256AvecSalt(token, td.TokenSalt);
            return CryptographicOperations.FixedTimeEquals(calc, td.TokenHash);
        }
    }
}
