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
        private readonly AppDbContext _context;
        private readonly TwoFactorService _twoFactorService;

        private const int LOCKOUT_MAX_ATTEMPTS = 5;
        private static readonly TimeSpan LOCKOUT_DURATION = TimeSpan.FromMinutes(10);

        private const string TRUST_COOKIE = "gh_trusted";
        private static readonly TimeSpan TRUST_DURATION = TimeSpan.FromDays(30);

        private const string PendingAuthenticatorSecretSessionKey = "PendingAuthenticatorSecret";
        private const string RecoveryCodesTempDataKey = "RecoveryCodes";
        private const string TwoFactorProviderEmail = "Email";
        private const string TwoFactorProviderAuthenticator = "Authenticator";

        public LoginController(AppDbContext context, TwoFactorService twoFactorService)
        {
            _context = context;
            _twoFactorService = twoFactorService;
        }

        [HttpGet]
        public IActionResult Index(string? expired = null)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            var role = HttpContext.Session.GetString("UserRole");

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
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(motDePasse))
            {
                ViewBag.Error = "Email ou mot de passe incorrect";
                return View();
            }

            var user = _context.Utilisateurs
                .Include(u => u.Departement)
                .FirstOrDefault(u => u.Email != null && u.Email.ToLower() == email.ToLower());

            Console.WriteLine($"[AUTH-DEBUG] Tentative pour Email: {email}");
            if (user == null)
            {
                Console.WriteLine("[AUTH-DEBUG] Aucun utilisateur trouvé avec cet email.");
                ViewBag.Error = "Email ou mot de passe incorrect";
                return View();
            }

            Console.WriteLine($"[AUTH-DEBUG] Utilisateur trouvé: ID={user.Id}, Role={user.Role}, 2FA={user.TwoFactorEnabled}");

            if (user.LockoutUntil != null && user.LockoutUntil > DateTime.UtcNow)
            {
                Console.WriteLine($"[AUTH-DEBUG] Compte verrouillé jusqu'à: {user.LockoutUntil}");
                ViewBag.Error = "Compte verrouillé temporairement. Réessayez plus tard.";
                LogSecurity(user.Id, "LOGIN_LOCKED", $"LockoutUntil={user.LockoutUntil:O}");
                return View();
            }

            bool passwordCorrect = VerifierMotDePasseSHA256AvecSalt(motDePasse, user.MotDePasseSalt, user.MotDePasseHash);
            Console.WriteLine($"[AUTH-DEBUG] Mot de passe correct: {passwordCorrect}");

            if (!passwordCorrect)
            {
                user.FailedLoginAttempts += 1;
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

            user.FailedLoginAttempts = 0;
            user.LockoutUntil = null;
            _context.SaveChanges();
            LogSecurity(user.Id, "LOGIN_SUCCESS", null);

            if (user.EstMotDePasseProvisoire)
            {
                TempData["Info"] = "Vous devez changer votre mot de passe provisoire.";
                return RedirectToAction("ChangeTempPassword", new { email = user.Email });
            }

            if (RequiresTwoFactor(user) && !IsTrustedDevice(user.Id))
            {
                HttpContext.Session.SetInt32("Pending2FAUserId", user.Id);

                if (UsesAuthenticator(user))
                {
                    LogSecurity(user.Id, "2FA_CHALLENGE_STARTED", $"Provider={TwoFactorProviderAuthenticator}");
                }
                else
                {
                    Send2FACode(user, null);
                }

                return RedirectToAction("Verify2FA");
            }

            OpenFullSession(user);
            return RedirectSelonRole(user.Role ?? "");
        }

        [HttpGet]
        public IActionResult Verify2FA()
        {
            var pending = HttpContext.Session.GetInt32("Pending2FAUserId");
            if (pending == null) return RedirectToAction("Index");

            var user = _context.Utilisateurs.FirstOrDefault(u => u.Id == pending.Value);
            if (user == null)
            {
                HttpContext.Session.Remove("Pending2FAUserId");
                return RedirectToAction("Index");
            }

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

            if (string.IsNullOrWhiteSpace(verificationCode))
            {
                ViewBag.Error = "Veuillez entrer le code.";
                return View(BuildVerifyTwoFactorViewModel(user));
            }

            var usedBackupCode = false;
            var success = false;

            if (UsesAuthenticator(user))
            {
                success = !string.IsNullOrWhiteSpace(user.AuthenticatorSecretKey) &&
                    _twoFactorService.ValidateTotp(user.AuthenticatorSecretKey, verificationCode);

                if (!success)
                {
                    success = TryUseBackupCode(user.Id, verificationCode);
                    usedBackupCode = success;
                }
            }
            else
            {
                success = VerifyOtp(user.Id, "2FA", verificationCode.Trim(), out var otp);
                if (success)
                {
                    otp!.UsedAt = DateTime.UtcNow;
                    _context.SaveChanges();
                }
            }

            if (!success)
            {
                ViewBag.Error = UsesAuthenticator(user) ? "Code de verification invalide." : "Code invalide ou expiré.";
                LogSecurity(user.Id, "2FA_FAIL", $"Provider={GetTwoFactorProvider(user)}");
                return View(BuildVerifyTwoFactorViewModel(user));
            }

            if (rememberDevice)
                CreateTrustedDevice(user.Id, BuildTrustedDeviceName());

            HttpContext.Session.Remove("Pending2FAUserId");
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

            if (UsesAuthenticator(user))
            {
                TempData["Info"] = "Le code est généré dans votre application Authenticator.";
                return RedirectToAction("Verify2FA");
            }

            Send2FACode(user, emailService);
            TempData["Info"] = "Nouveau code envoyé.";
            return RedirectToAction("Verify2FA");
        }

        [HttpGet]
        public IActionResult Manage2FA()
        {
            var user = GetCurrentSessionUser();
            if (user == null) return RedirectToAction("Index");
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

            SetPendingAuthenticatorSecret(_twoFactorService.GenerateSharedKey());

            TempData["Info"] = UsesAuthenticator(user)
                ? "Scannez le nouveau QR code puis confirmez avec un code."
                : "Scannez le QR code puis saisissez le code généré.";

            LogSecurity(user.Id, "AUTHENTICATOR_SETUP_STARTED", null);
            return RedirectToAction("Manage2FA");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EnableAuthenticator(string verificationCode)
        {
            var user = GetCurrentSessionUser();
            if (user == null) return RedirectToAction("Index");

            var pendingSecret = GetPendingAuthenticatorSecret();
            if (string.IsNullOrWhiteSpace(pendingSecret))
            {
                TempData["Error"] = "Commencez par générer un QR code.";
                return RedirectToAction("Manage2FA");
            }

            if (string.IsNullOrWhiteSpace(verificationCode))
            {
                TempData["Error"] = "Entrez le code affiché par votre application Authenticator.";
                return RedirectToAction("Manage2FA");
            }

            if (!_twoFactorService.ValidateTotp(pendingSecret, verificationCode))
            {
                TempData["Error"] = "Code Authenticator invalide.";
                LogSecurity(user.Id, "AUTHENTICATOR_ENABLE_FAIL", null);
                return RedirectToAction("Manage2FA");
            }

            var recoveryCodes = ReplaceBackupCodes(user.Id);
            user.AuthenticatorSecretKey = pendingSecret;
            user.AuthenticatorEnabledAt = DateTime.UtcNow;
            user.TwoFactorEnabled = true;
            user.TwoFactorProvider = TwoFactorProviderAuthenticator;

            ClearTrustedDevices(user.Id);
            ClearPendingAuthenticatorSecret();
            _context.SaveChanges();

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

            if (!UsesAuthenticator(user))
            {
                TempData["Info"] = "Activez d'abord l'application Authenticator.";
                return RedirectToAction("Manage2FA");
            }

            var recoveryCodes = ReplaceBackupCodes(user.Id);
            _context.SaveChanges();

            TempData[RecoveryCodesTempDataKey] = JsonSerializer.Serialize(recoveryCodes);
            TempData["Success"] = "Nouveaux codes de secours générés.";

            LogSecurity(user.Id, "BACKUP_CODES_REGENERATED", $"Count={recoveryCodes.Count}");
            return RedirectToAction("Manage2FA");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DisableAuthenticator()
        {
            var user = GetCurrentSessionUser();
            if (user == null) return RedirectToAction("Index");

            user.TwoFactorEnabled = false;
            user.TwoFactorProvider = null;
            user.AuthenticatorSecretKey = null;
            user.AuthenticatorEnabledAt = null;

            RevokeBackupCodes(user.Id);
            ClearTrustedDevices(user.Id);
            ClearPendingAuthenticatorSecret();
            _context.SaveChanges();

            TempData["Success"] = "Application Authenticator désactivée.";
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
                TempData["Error"] = "Aucune adresse email associée.";
                return RedirectToAction("Manage2FA");
            }

            user.TwoFactorEnabled = true;
            user.TwoFactorProvider = TwoFactorProviderEmail;
            user.AuthenticatorSecretKey = null;
            user.AuthenticatorEnabledAt = null;

            RevokeBackupCodes(user.Id);
            ClearTrustedDevices(user.Id);
            _context.SaveChanges();

            TempData["Success"] = "Le 2FA par email est activé.";
            LogSecurity(user.Id, "EMAIL_2FA_ENABLED", null);

            return RedirectToAction("Manage2FA");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DisableEmailTwoFactor()
        {
            var user = GetCurrentSessionUser();
            if (user == null) return RedirectToAction("Index");

            user.TwoFactorEnabled = false;
            user.TwoFactorProvider = null;
            ClearTrustedDevices(user.Id);
            _context.SaveChanges();

            TempData["Success"] = "Le 2FA par email est désactivé.";
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
            if (device != null)
            {
                var wasCurrentDevice = GetTrustedDeviceCookieId() == device.Id;
                _context.TrustedDevices.Remove(device);
                _context.SaveChanges();

                if (wasCurrentDevice)
                    Response.Cookies.Delete(TRUST_COOKIE);
            }

            TempData["Success"] = "Appareil supprimé.";
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
            TempData["Success"] = "Tous les appareils ont été supprimés.";
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
            return RedirectToAction("Manage2FA");
        }

        [HttpGet]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Index");
        }

        // TODO: SUPPRIMER AVANT PRODUCTION
        [HttpGet("/Login/DebugSchema")]
        public async Task<IActionResult> CheckSchemaDebug()
        {
            var dbSchema = new List<object>();

            try
            {
                var tables = await _context.Database.SqlQueryRaw<string>("SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'").ToListAsync();

                foreach (var table in tables)
                {
                    var columns = await _context.Database.SqlQueryRaw<string>(@$"
                        SELECT column_name || ' (' || data_type || ')'
                        FROM information_schema.columns 
                        WHERE table_name = '{table}'
                    ").ToListAsync();

                    dbSchema.Add(new { Table = table, Columns = columns });
                }

                return Json(new { Status = "Success", Schema = dbSchema });
            }
            catch (Exception ex)
            {
                return Json(new { Status = "Error", Message = ex.Message });
            }
        }

        [HttpGet("/Login/DebugUsers")]
        public async Task<IActionResult> ListUsersDebug()
        {
            var users = await _context.Utilisateurs.Select(u => new { u.Id, u.Email, u.Nom, u.Role, HasHash = u.MotDePasseHash != null, u.FailedLoginAttempts, u.LockoutUntil }).ToListAsync();
            return Json(users);
        }

        [HttpGet]
        public IActionResult ChangeTempPassword(string email)
        {
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

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(motDePasseProvisoire) ||
                string.IsNullOrWhiteSpace(nouveauMotDePasse) || string.IsNullOrWhiteSpace(confirmerMotDePasse) ||
                string.IsNullOrWhiteSpace(questionSecurite) || string.IsNullOrWhiteSpace(reponseSecurite))
            {
                ViewBag.Error = "Tous les champs sont obligatoires.";
                return View();
            }

            if (nouveauMotDePasse != confirmerMotDePasse)
            {
                ViewBag.Error = "La confirmation ne correspond pas.";
                return View();
            }

            if (!MotDePasseValide(nouveauMotDePasse))
            {
                ViewBag.Error = "Mot de passe non conforme.";
                return View();
            }

            var user = _context.Utilisateurs.FirstOrDefault(u => u.Email == email);
            if (user == null || !user.EstMotDePasseProvisoire)
            {
                ViewBag.Error = "Informations invalides.";
                return View();
            }

            if (!VerifierMotDePasseSHA256AvecSalt(motDePasseProvisoire, user.MotDePasseSalt, user.MotDePasseHash))
            {
                ViewBag.Error = "Informations invalides.";
                return View();
            }

            Guid salt = Guid.NewGuid();
            user.MotDePasseSalt = salt;
            user.MotDePasseHash = CalculerSHA256AvecSalt(nouveauMotDePasse, salt);
            user.EstMotDePasseProvisoire = false;

            string normalized = (reponseSecurite ?? "").Trim().ToLowerInvariant();
            Guid repSalt = Guid.NewGuid();
            user.QuestionSecurite = questionSecurite;
            user.ReponseSecuriteSalt = repSalt;
            user.ReponseSecuriteHash = CalculerSHA256AvecSalt(normalized, repSalt);

            _context.SaveChanges();

            try { emailService.Send(user.Email ?? "", "Mot de passe modifié", "Votre mot de passe a été modifié."); } catch { }

            LogSecurity(user.Id, "PWD_CHANGED_FROM_TEMP", null);
            TempData["Success"] = "Mot de passe changé. Vous pouvez vous connecter.";
            return RedirectToAction("Index");
        }

        [HttpGet]
        public IActionResult ForgotPassword() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ForgotPassword(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                ViewBag.Error = "Veuillez entrer votre email.";
                return View();
            }

            var user = _context.Utilisateurs.FirstOrDefault(u => u.Email == email);
            if (user == null)
            {
                ViewBag.Error = "Aucun compte trouvé.";
                return View();
            }

            return RedirectToAction("ForgotPasswordReset", new { id = user.Id });
        }

        [HttpGet]
        public IActionResult ForgotPasswordReset(int id, int? step)
        {
            var user = _context.Utilisateurs.FirstOrDefault(u => u.Id == id);
            if (user == null) return RedirectToAction("ForgotPassword");

            if (string.IsNullOrWhiteSpace(user.QuestionSecurite) || user.ReponseSecuriteSalt == null || user.ReponseSecuriteHash == null)
            {
                TempData["Info"] = "Aucune question de sécurité configurée.";
                return RedirectToAction("ForgotPassword");
            }

            ViewBag.UserId = user.Id;
            ViewBag.Question = user.QuestionSecurite;

            if (step == 4 || (HttpContext.Session.GetInt32("ResetQuestionVerifiedUserId") == user.Id))
                ViewBag.Step = 4;
            else
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
            if (user == null) { ViewBag.Step = 2; return View(); }

            ViewBag.UserId = user.Id;
            ViewBag.Question = user.QuestionSecurite;

            if (step == "2")
            {
                if (!VerifierReponseSecurite(user, reponseSecurite))
                {
                    ViewBag.Error = "Réponse incorrecte.";
                    ViewBag.Step = 2;
                    return View();
                }

                HttpContext.Session.SetInt32("ResetQuestionVerifiedUserId", user.Id);
                CreateOtp(user.Id, "RESET", TimeSpan.FromMinutes(10));
                var code = (string)HttpContext.Items["__otp_code"]!;
                try { emailService.Send(user.Email ?? "", "Code reset", $"Code : {code}"); } catch { }
                ViewBag.Step = 4;
                return View();
            }

            if (step == "4")
            {
                if (HttpContext.Session.GetInt32("ResetQuestionVerifiedUserId") != user.Id) return RedirectToAction("ForgotPasswordReset", new { id = userId });
                if (!VerifyOtp(user.Id, "RESET", (codeEmail ?? "").Trim(), out var otp))
                {
                    ViewBag.Error = "Code invalide.";
                    ViewBag.Step = 4;
                    return View();
                }
                otp!.UsedAt = DateTime.UtcNow;
                _context.SaveChanges();
                HttpContext.Session.SetInt32("ResetEmailVerifiedUserId", user.Id);
                ViewBag.Step = 3;
                return View();
            }

            if (step == "3")
            {
                if (HttpContext.Session.GetInt32("ResetEmailVerifiedUserId") != user.Id) return RedirectToAction("ForgotPasswordReset", new { id = userId, step = 4 });
                if (nouveauMotDePasse != confirmerMotDePasse || !MotDePasseValide(nouveauMotDePasse))
                {
                    ViewBag.Error = "Mot de passe invalide ou non conforme.";
                    ViewBag.Step = 3;
                    return View();
                }

                Guid salt = Guid.NewGuid();
                user.MotDePasseSalt = salt;
                user.MotDePasseHash = CalculerSHA256AvecSalt(nouveauMotDePasse, salt);
                user.EstMotDePasseProvisoire = false;
                _context.SaveChanges();

                try { emailService.Send(user.Email ?? "", "Reset Password", "Mot de passe réinitialisé."); } catch { }
                HttpContext.Session.Remove("ResetQuestionVerifiedUserId");
                HttpContext.Session.Remove("ResetEmailVerifiedUserId");
                TempData["Success"] = "Réinitialisé avec succès.";
                return RedirectToAction("Index");
            }

            ViewBag.Step = 2;
            return View();
        }

        private Utilisateur? GetCurrentSessionUser()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            return userId == null ? null : _context.Utilisateurs.FirstOrDefault(u => u.Id == userId.Value);
        }

        private void OpenFullSession(Utilisateur user)
        {
            HttpContext.Session.SetInt32("UserId", user.Id);
            HttpContext.Session.SetString("UserRole", (user.Role ?? "").Trim());
            HttpContext.Session.SetString("UserNom", user.Nom ?? "");
            HttpContext.Session.SetString("UserEmail", user.Email ?? "");
            if (user.DepartementId.HasValue) HttpContext.Session.SetInt32("DepartementId", user.DepartementId.Value);
        }

        private IActionResult RedirectSelonRole(string role)
        {
            role = (role ?? "").Trim();
            return role switch
            {
                "Administrateur" => RedirectToAction("Index", "Admin"),
                "ResponsableDépartement" => RedirectToAction("Index", "Responsable"),
                "Professeur" => RedirectToAction("Index", "Professeur"),
                _ => RedirectToAction("Index", "Home")
            };
        }

        private static string[] GetQuestionsSecurite() => new[] { "Nom de ta première école ?", "Prénom de ta mère ?", "Nom de ton premier animal ?", "Ville de naissance ?", "Plat préféré ?" };

        private void LogSecurity(int? userId, string action, string? details)
        {
            _context.SecurityLogs.Add(new SecurityLog { UserId = userId, Action = action, Details = details, IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(), CreatedAt = DateTime.UtcNow });
            _context.SaveChanges();
        }

        private static bool MotDePasseValide(string mdp)
        {
            if (string.IsNullOrWhiteSpace(mdp) || mdp.Length < 8) return false;
            return mdp.Any(char.IsUpper) && mdp.Any(char.IsLower) && mdp.Any(char.IsDigit) && mdp.Any(ch => !char.IsLetterOrDigit(ch));
        }

        private static bool VerifierMotDePasseSHA256AvecSalt(string mdp, Guid salt, byte[] hashStocke)
        {
            if (hashStocke == null) return false;

            // Rétrocompatibilité avec l'export SQL Server -> Postgres (\x...)
            if (hashStocke.Length == 66)
            {
                try
                {
                    string hex = System.Text.Encoding.UTF8.GetString(hashStocke);
                    if (hex.StartsWith(@"\x") || hex.StartsWith("\\x")) 
                    {
                        hashStocke = Convert.FromHexString(hex.Substring(2));
                    }
                }
                catch { }
            }

            byte[] hashCalcule = CalculerSHA256AvecSalt(mdp, salt);
            if (hashCalcule.Length != hashStocke.Length) return false;

            return CryptographicOperations.FixedTimeEquals(hashCalcule, hashStocke);
        }

        private static byte[] CalculerSHA256AvecSalt(string mdp, Guid saltGuid)
        {
            byte[] salt = saltGuid.ToByteArray();
            byte[] bytes = Encoding.UTF8.GetBytes(mdp);
            byte[] input = new byte[salt.Length + bytes.Length];
            Buffer.BlockCopy(salt, 0, input, 0, salt.Length);
            Buffer.BlockCopy(bytes, 0, input, salt.Length, bytes.Length);
            return SHA256.HashData(input);
        }

        private static bool VerifierReponseSecurite(Utilisateur user, string reponse)
        {
            if (user.ReponseSecuriteSalt == null || user.ReponseSecuriteHash == null) return false;
            
            var hashStocke = user.ReponseSecuriteHash;
            if (hashStocke.Length == 66)
            {
                try
                {
                    string hex = System.Text.Encoding.UTF8.GetString(hashStocke);
                    if (hex.StartsWith(@"\x") || hex.StartsWith("\\x")) 
                    {
                        hashStocke = Convert.FromHexString(hex.Substring(2));
                    }
                }
                catch { }
            }

            byte[] calc = CalculerSHA256AvecSalt((reponse ?? "").Trim().ToLowerInvariant(), user.ReponseSecuriteSalt.Value);
            if (calc.Length != hashStocke.Length) return false;

            return CryptographicOperations.FixedTimeEquals(calc, hashStocke);
        }

        private bool RequiresTwoFactor(Utilisateur user) => UsesAuthenticator(user) || UsesEmailTwoFactor(user);
        private bool UsesAuthenticator(Utilisateur user) => user.TwoFactorEnabled && string.Equals(user.TwoFactorProvider, TwoFactorProviderAuthenticator, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(user.AuthenticatorSecretKey);
        private bool UsesEmailTwoFactor(Utilisateur user) => user.TwoFactorEnabled && !UsesAuthenticator(user);
        private string GetTwoFactorProvider(Utilisateur user) => UsesAuthenticator(user) ? TwoFactorProviderAuthenticator : TwoFactorProviderEmail;
        private string GetTwoFactorProviderLabel(Utilisateur user) => UsesAuthenticator(user) ? "Application Authenticator" : (UsesEmailTwoFactor(user) ? "Code par email" : "Aucune");
        private string GetAuthenticatorIssuer() => "GestionHoraire";
        private string GetAuthenticatorAccountLabel(Utilisateur user) => user.Email ?? user.Nom ?? $"User-{user.Id}";

        private ManageTwoFactorViewModel BuildManageTwoFactorViewModel(Utilisateur user, IReadOnlyList<string>? recoveryCodes = null)
        {
            RemoveExpiredTrustedDevices(user.Id);
            var pendingSecret = GetPendingAuthenticatorSecret();
            var isSetupInProgress = !string.IsNullOrWhiteSpace(pendingSecret);
            return new ManageTwoFactorViewModel
            {
                IsTwoFactorEnabled = user.TwoFactorEnabled,
                IsAuthenticatorConfigured = UsesAuthenticator(user),
                IsEmailTwoFactorEnabled = UsesEmailTwoFactor(user),
                IsSetupInProgress = isSetupInProgress,
                ProviderLabel = GetTwoFactorProviderLabel(user),
                ManualEntryKey = isSetupInProgress ? _twoFactorService.FormatSharedKey(pendingSecret!) : null,
                QrCodeSvg = isSetupInProgress ? BuildAuthenticatorQrCodeSvg(user, pendingSecret!) : null,
                AccountLabel = GetAuthenticatorAccountLabel(user),
                RemainingBackupCodes = CountRemainingBackupCodes(user.Id),
                RecoveryCodes = recoveryCodes ?? Array.Empty<string>(),
                TrustedDevices = BuildTrustedDeviceViewModels(user.Id)
            };
        }

        private string BuildAuthenticatorQrCodeSvg(Utilisateur user, string secret) => _twoFactorService.GenerateQrCodeSvg(_twoFactorService.BuildOtpAuthUri(GetAuthenticatorIssuer(), GetAuthenticatorAccountLabel(user), secret));

        private IReadOnlyList<TrustedDeviceViewModel> BuildTrustedDeviceViewModels(int userId)
        {
            var cookieId = GetTrustedDeviceCookieId();
            return _context.TrustedDevices.Where(td => td.UserId == userId && td.ExpiresAt > DateTime.UtcNow).OrderByDescending(td => td.CreatedAt).Select(td => new TrustedDeviceViewModel { Id = td.Id, DeviceName = td.DeviceName ?? "Appareil Web", CreatedAt = td.CreatedAt, ExpiresAt = td.ExpiresAt, IsCurrentDevice = cookieId.HasValue && td.Id == cookieId.Value }).ToList();
        }

        private static string GetSecurityLogLabel(string action) => action switch { "LOGIN_SUCCESS" => "Connexion réussie", "LOGIN_FAILED" => "Échec de connexion", "LOGIN_LOCKED" => "Bloqué", "2FA_SUCCESS" => "2FA réussi", "2FA_FAIL" => "2FA échoué", _ => action };

        private VerifyTwoFactorViewModel BuildVerifyTwoFactorViewModel(Utilisateur user)
        {
            if (UsesAuthenticator(user)) return new VerifyTwoFactorViewModel { ProviderLabel = "Authenticator", Instruction = "Entrez le code à 6 chiffres.", InputLabel = "Code", AllowResend = false };
            return new VerifyTwoFactorViewModel { ProviderLabel = "Code par email", Instruction = "Entrez le code reçu par email.", InputLabel = "Code", AllowResend = true };
        }

        private string? GetPendingAuthenticatorSecret() => HttpContext.Session.GetString(PendingAuthenticatorSecretSessionKey);
        private void SetPendingAuthenticatorSecret(string secret) => HttpContext.Session.SetString(PendingAuthenticatorSecretSessionKey, secret);
        private void ClearPendingAuthenticatorSecret() => HttpContext.Session.Remove(PendingAuthenticatorSecretSessionKey);
        private IReadOnlyList<string> ReadRecoveryCodesFromTempData() { if (!TempData.TryGetValue(RecoveryCodesTempDataKey, out var val) || val == null) return Array.Empty<string>(); return JsonSerializer.Deserialize<string[]>(val.ToString()!) ?? Array.Empty<string>(); }
        private int CountRemainingBackupCodes(int userId) => _context.BackupCodes.Count(b => b.UserId == userId && b.UsedAt == null && b.RevokedAt == null);
        private List<string> ReplaceBackupCodes(int userId, int count = 8) { RevokeBackupCodes(userId); var codes = _twoFactorService.GenerateBackupCodes(count).ToList(); foreach (var c in codes) { var s = Guid.NewGuid(); _context.BackupCodes.Add(new BackupCode { UserId = userId, CodeSalt = s, CodeHash = _twoFactorService.HashBackupCode(c, s), CreatedAt = DateTime.UtcNow }); } return codes; }
        private void RevokeBackupCodes(int userId) { var olds = _context.BackupCodes.Where(b => b.UserId == userId && b.UsedAt == null && b.RevokedAt == null).ToList(); foreach (var c in olds) c.RevokedAt = DateTime.UtcNow; }
        private bool TryUseBackupCode(int userId, string code) { var norm = _twoFactorService.NormalizeBackupCode(code); if (string.IsNullOrEmpty(norm)) return false; var olds = _context.BackupCodes.Where(b => b.UserId == userId && b.UsedAt == null && b.RevokedAt == null).ToList(); foreach (var bc in olds) { if (CryptographicOperations.FixedTimeEquals(_twoFactorService.HashBackupCode(norm, bc.CodeSalt), bc.CodeHash)) { bc.UsedAt = DateTime.UtcNow; _context.SaveChanges(); return true; } } return false; }
        private static string Generate6DigitCode() { var bytes = new byte[4]; RandomNumberGenerator.Fill(bytes); return ((BitConverter.ToInt32(bytes, 0) & 0x7FFFFFFF) % 1000000).ToString("D6"); }

        private EmailOtp CreateOtp(int userId, string purpose, TimeSpan ttl)
        {
            var olds = _context.EmailOtps.Where(o => o.UserId == userId && o.Purpose == purpose && o.UsedAt == null).ToList();
            foreach (var o in olds) o.UsedAt = DateTime.UtcNow;
            string code = Generate6DigitCode();
            Guid s = Guid.NewGuid();
            var otp = new EmailOtp { UserId = userId, Purpose = purpose, CodeSalt = s, CodeHash = CalculerSHA256AvecSalt(code, s), ExpiresAt = DateTime.UtcNow.Add(ttl), LastSentAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
            _context.EmailOtps.Add(otp);
            _context.SaveChanges();
            HttpContext.Items["__otp_code"] = code;
            return otp;
        }

        private bool VerifyOtp(int userId, string purpose, string code, out EmailOtp? otp)
        {
            otp = _context.EmailOtps.Where(o => o.UserId == userId && o.Purpose == purpose && o.UsedAt == null).OrderByDescending(o => o.CreatedAt).FirstOrDefault();
            if (otp == null || otp.ExpiresAt <= DateTime.UtcNow || otp.Attempts >= 5) return false;
            otp.Attempts++;
            _context.SaveChanges();
            return CryptographicOperations.FixedTimeEquals(CalculerSHA256AvecSalt(code, otp.CodeSalt), otp.CodeHash);
        }

        private void Send2FACode(Utilisateur user, EmailService? emailService)
        {
            emailService ??= HttpContext.RequestServices.GetService(typeof(EmailService)) as EmailService;
            CreateOtp(user.Id, "2FA", TimeSpan.FromMinutes(10));
            var code = (string)HttpContext.Items["__otp_code"]!;
            try { emailService?.Send(user.Email ?? "", "Verification Code", $"Code: {code}"); } catch { }
            LogSecurity(user.Id, "2FA_CODE_SENT", null);
        }

        private string BuildTrustedDeviceName()
        {
            var ua = Request.Headers["User-Agent"].ToString();
            return ua.Contains("Chrome") ? "Chrome" : (ua.Contains("Firefox") ? "Firefox" : "Navigateur Web");
        }

        private void CreateTrustedDevice(int userId, string name)
        {
            var token = Guid.NewGuid().ToString("N");
            var salt = Guid.NewGuid();
            var td = new TrustedDevice { UserId = userId, DeviceName = name, TokenSalt = salt, TokenHash = CalculerSHA256AvecSalt(token, salt), ExpiresAt = DateTime.UtcNow.Add(TRUST_DURATION), CreatedAt = DateTime.UtcNow };
            _context.TrustedDevices.Add(td);
            _context.SaveChanges();
            Response.Cookies.Append(TRUST_COOKIE, $"{td.Id}:{token}", new CookieOptions { HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Lax, Expires = DateTimeOffset.UtcNow.Add(TRUST_DURATION) });
        }

        private void ClearTrustedDevices(int userId) { var devs = _context.TrustedDevices.Where(td => td.UserId == userId).ToList(); _context.TrustedDevices.RemoveRange(devs); Response.Cookies.Delete(TRUST_COOKIE); }
        private int? GetTrustedDeviceCookieId() { if (!Request.Cookies.TryGetValue(TRUST_COOKIE, out var val) || string.IsNullOrEmpty(val)) return null; var parts = val.Split(':'); return int.TryParse(parts[0], out var id) ? id : null; }

        private void RemoveExpiredTrustedDevices(int userId)
        {
            var olds = _context.TrustedDevices.Where(td => td.UserId == userId && td.ExpiresAt <= DateTime.UtcNow).ToList();
            if (olds.Count > 0) { _context.TrustedDevices.RemoveRange(olds); _context.SaveChanges(); }
        }

        private bool IsTrustedDevice(int userId)
        {
            if (!Request.Cookies.TryGetValue(TRUST_COOKIE, out var val) || string.IsNullOrEmpty(val)) return false;
            var parts = val.Split(':'); if (parts.Length != 2 || !int.TryParse(parts[0], out var id)) return false;
            var td = _context.TrustedDevices.FirstOrDefault(x => x.Id == id && x.UserId == userId);
            if (td == null || td.ExpiresAt <= DateTime.UtcNow) return false;
            return CryptographicOperations.FixedTimeEquals(CalculerSHA256AvecSalt(parts[1], td.TokenSalt), td.TokenHash);
        }
    }
}
