using GestionHoraire.Data;
using GestionHoraire.Models;
using GestionHoraire.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace GestionHoraire.Controllers
{
    public class LoginController : Controller
    {
        private readonly AppDbContext _context;

        private const int LOCKOUT_MAX_ATTEMPTS = 5;
        private static readonly TimeSpan LOCKOUT_DURATION = TimeSpan.FromMinutes(10);

        private const string TRUST_COOKIE = "gh_trusted";
        private static readonly TimeSpan TRUST_DURATION = TimeSpan.FromDays(30);

        public LoginController(AppDbContext context)
        {
            _context = context;
        }

        // =========================
        // LOGIN
        // =========================
        [HttpGet]
        public IActionResult Index()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            var role = HttpContext.Session.GetString("UserRole");
            if (userId != null && !string.IsNullOrEmpty(role))
                return RedirectSelonRole(role);

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
                .FirstOrDefault(u => u.Email == email);

            if (user == null)
            {
                ViewBag.Error = "Email ou mot de passe incorrect";
                return View();
            }

            // LOCKOUT
            if (user.LockoutUntil != null && user.LockoutUntil > DateTime.UtcNow)
            {
                ViewBag.Error = "Compte verrouillé temporairement. Réessayez plus tard.";
                LogSecurity(user.Id, "LOGIN_LOCKED", $"LockoutUntil={user.LockoutUntil:O}");
                return View();
            }

            if (!VerifierMotDePasseSHA256AvecSalt(motDePasse, user.MotDePasseSalt, user.MotDePasseHash))
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

            // login success => reset
            user.FailedLoginAttempts = 0;
            user.LockoutUntil = null;
            _context.SaveChanges();
            LogSecurity(user.Id, "LOGIN_SUCCESS", null);

            // mot de passe provisoire
            if (user.EstMotDePasseProvisoire)
            {
                TempData["Info"] = "Vous devez changer votre mot de passe provisoire.";
                return RedirectToAction("ChangeTempPassword", new { email = user.Email });
            }

            // 2FA (si activé et device pas trusted)
            if (user.TwoFactorEnabled && !IsTrustedDevice(user.Id))
            {
                HttpContext.Session.SetInt32("Pending2FAUserId", user.Id);
                Send2FACode(user, null);
                return RedirectToAction("Verify2FA");
            }

            // Session normale
            OpenFullSession(user);
            return RedirectSelonRole(user.Role ?? "");
        }

        // =========================
        // VERIFY 2FA
        // =========================
        [HttpGet]
        public IActionResult Verify2FA()
        {
            var pending = HttpContext.Session.GetInt32("Pending2FAUserId");
            if (pending == null) return RedirectToAction("Index");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Verify2FA(string codeEmail, bool rememberDevice)
        {
            var pending = HttpContext.Session.GetInt32("Pending2FAUserId");
            if (pending == null) return RedirectToAction("Index");

            var user = _context.Utilisateurs.FirstOrDefault(u => u.Id == pending.Value);
            if (user == null) return RedirectToAction("Index");

            if (string.IsNullOrWhiteSpace(codeEmail))
            {
                ViewBag.Error = "Veuillez entrer le code.";
                return View();
            }

            if (!VerifyOtp(user.Id, "2FA", codeEmail.Trim(), out var otp))
            {
                ViewBag.Error = "Code invalide ou expiré.";
                LogSecurity(user.Id, "2FA_FAIL", null);
                return View();
            }

            otp!.UsedAt = DateTime.UtcNow;
            _context.SaveChanges();

            if (rememberDevice)
                CreateTrustedDevice(user.Id, "Web");

            HttpContext.Session.Remove("Pending2FAUserId");

            OpenFullSession(user);
            LogSecurity(user.Id, "2FA_SUCCESS", $"Remember={rememberDevice}");
            return RedirectSelonRole(user.Role ?? "");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Resend2FA([FromServices] EmailService emailService)
        {
            var pending = HttpContext.Session.GetInt32("Pending2FAUserId");
            if (pending == null) return RedirectToAction("Index");

            var user = _context.Utilisateurs.FirstOrDefault(u => u.Id == pending.Value);
            if (user == null) return RedirectToAction("Index");

            Send2FACode(user, emailService);
            TempData["Info"] = "Nouveau code envoyé.";
            return RedirectToAction("Verify2FA");
        }

        // =========================
        // ENABLE/DISABLE 2FA
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Toggle2FA()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Index");

            var user = _context.Utilisateurs.FirstOrDefault(u => u.Id == userId.Value);
            if (user == null) return RedirectToAction("Logout");

            user.TwoFactorEnabled = !user.TwoFactorEnabled;
            _context.SaveChanges();

            LogSecurity(user.Id, "2FA_TOGGLED", $"Enabled={user.TwoFactorEnabled}");
            TempData["Success"] = user.TwoFactorEnabled ? "2FA activé." : "2FA désactivé.";
            return RedirectToAction("Index", "Home");
        }

        // =========================
        // LOGOUT
        // =========================
        [HttpGet]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Index");
        }

        // =========================
        // CHANGER MOT DE PASSE PROVISOIRE + question sécurité
        // =========================
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
            if (user == null)
            {
                ViewBag.Error = "Informations invalides.";
                return View();
            }

            if (!user.EstMotDePasseProvisoire)
            {
                ViewBag.Error = "Aucun mot de passe provisoire actif.";
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

            if (string.IsNullOrWhiteSpace(user.QuestionSecurite) ||
                user.ReponseSecuriteSalt == null ||
                user.ReponseSecuriteHash == null)
            {
                TempData["Info"] = "Aucune question de sécurité n'est configurée pour ce compte.";
                return RedirectToAction("ForgotPassword");
            }

            ViewBag.UserId = user.Id;
            ViewBag.Question = user.QuestionSecurite;

            if (step.HasValue && step.Value == 4)
            {
                ViewBag.Step = 4;
                return View();
            }

            var qOk = HttpContext.Session.GetInt32("ResetQuestionVerifiedUserId");
            if (qOk != null && qOk.Value == user.Id)
            {
                ViewBag.Step = 4;
                return View();
            }

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

            if (step == "2")
            {
                if (!VerifierReponseSecurite(user, reponseSecurite))
                {
                    ViewBag.Error = "Réponse incorrecte.";
                    ViewBag.Step = 2;
                    LogSecurity(user.Id, "PWD_RESET_Q_FAIL", null);
                    return View();
                }

                HttpContext.Session.SetInt32("ResetQuestionVerifiedUserId", user.Id);

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

                otp!.UsedAt = DateTime.UtcNow;
                _context.SaveChanges();

                HttpContext.Session.SetInt32("ResetEmailVerifiedUserId", user.Id);
                ViewBag.Step = 3;
                return View();
            }

            if (step == "3")
            {
                var ok = HttpContext.Session.GetInt32("ResetEmailVerifiedUserId");
                if (ok == null || ok.Value != user.Id)
                {
                    ViewBag.Error = "Veuillez valider le code email avant.";
                    ViewBag.Step = 4;
                    return View();
                }

                if (nouveauMotDePasse != confirmerMotDePasse)
                {
                    ViewBag.Error = "La confirmation ne correspond pas.";
                    ViewBag.Step = 3;
                    return View();
                }

                if (!MotDePasseValide(nouveauMotDePasse))
                {
                    ViewBag.Error = "Mot de passe non conforme.";
                    ViewBag.Step = 3;
                    return View();
                }

                if (VerifierMotDePasseSHA256AvecSalt(nouveauMotDePasse, user.MotDePasseSalt, user.MotDePasseHash))
                {
                    ViewBag.Error = "Le nouveau mot de passe doit être différent de l'ancien.";
                    ViewBag.Step = 3;
                    return View();
                }

                Guid salt = Guid.NewGuid();
                user.MotDePasseSalt = salt;
                user.MotDePasseHash = CalculerSHA256AvecSalt(nouveauMotDePasse, salt);
                user.EstMotDePasseProvisoire = false;

                _context.SaveChanges();

                try
                {
                    emailService.Send(user.Email ?? "", "Mot de passe réinitialisé", "Votre mot de passe a été réinitialisé.");
                }
                catch { }

                LogSecurity(user.Id, "PWD_RESET_SUCCESS", null);

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
            var qOk = HttpContext.Session.GetInt32("ResetQuestionVerifiedUserId");
            if (qOk == null || qOk.Value != userId)
            {
                TempData["Info"] = "Veuillez répondre à la question avant.";
                return RedirectToAction("ForgotPasswordReset", new { id = userId });
            }

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
        private void OpenFullSession(Utilisateur user)
        {
            HttpContext.Session.SetInt32("UserId", user.Id);
            HttpContext.Session.SetString("UserRole", user.Role ?? "");
            HttpContext.Session.SetString("UserNom", user.Nom ?? "");
            HttpContext.Session.SetString("UserEmail", user.Email ?? "");
            if (user.DepartementId.HasValue)
                HttpContext.Session.SetInt32("DepartementId", user.DepartementId.Value);
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
            if (string.IsNullOrWhiteSpace(motDePasse) || motDePasse.Length < 8) return false;
            bool hasUpper = motDePasse.Any(char.IsUpper);
            bool hasLower = motDePasse.Any(char.IsLower);
            bool hasDigit = motDePasse.Any(char.IsDigit);
            bool hasSpecial = motDePasse.Any(ch => !char.IsLetterOrDigit(ch));
            return hasUpper && hasLower && hasDigit && hasSpecial;
        }

        private static bool VerifierMotDePasseSHA256AvecSalt(string motDePasse, Guid saltGuid, byte[] hashStocke)
        {
            byte[] hashCalcule = CalculerSHA256AvecSalt(motDePasse, saltGuid);
            return CryptographicOperations.FixedTimeEquals(hashCalcule, hashStocke);
        }

        private static byte[] CalculerSHA256AvecSalt(string motDePasse, Guid saltGuid)
        {
            byte[] salt = saltGuid.ToByteArray();
            byte[] mdpBytes = Encoding.UTF8.GetBytes(motDePasse);

            byte[] input = new byte[salt.Length + mdpBytes.Length];
            Buffer.BlockCopy(salt, 0, input, 0, salt.Length);
            Buffer.BlockCopy(mdpBytes, 0, input, salt.Length, mdpBytes.Length);

            return SHA256.HashData(input);
        }

        private static bool VerifierReponseSecurite(Utilisateur user, string reponse)
        {
            if (user.ReponseSecuriteSalt == null || user.ReponseSecuriteHash == null) return false;
            string normalized = (reponse ?? "").Trim().ToLowerInvariant();
            byte[] hashCalcule = CalculerSHA256AvecSalt(normalized, user.ReponseSecuriteSalt.Value);
            return CryptographicOperations.FixedTimeEquals(hashCalcule, user.ReponseSecuriteHash);
        }

        // ===== OTP helpers =====
        private static string Generate6DigitCode()
        {
            var bytes = new byte[4];
            RandomNumberGenerator.Fill(bytes);
            int val = BitConverter.ToInt32(bytes, 0) & 0x7FFFFFFF;
            return (val % 1000000).ToString("D6");
        }

        private EmailOtp CreateOtp(int userId, string purpose, TimeSpan ttl)
        {
            var olds = _context.EmailOtps
                .Where(o => o.UserId == userId && o.Purpose == purpose && o.UsedAt == null)
                .ToList();

            foreach (var o in olds)
                o.UsedAt = DateTime.UtcNow;

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

            HttpContext.Items["__otp_code"] = code;
            return otp;
        }

        private bool VerifyOtp(int userId, string purpose, string code, out EmailOtp? otp)
        {
            otp = _context.EmailOtps
                .Where(o => o.UserId == userId && o.Purpose == purpose && o.UsedAt == null)
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefault();

            if (otp == null) return false;
            if (otp.ExpiresAt <= DateTime.UtcNow) return false;
            if (otp.Attempts >= 5) return false;

            otp.Attempts += 1;
            _context.SaveChanges();

            byte[] calc = CalculerSHA256AvecSalt(code, otp.CodeSalt);
            return CryptographicOperations.FixedTimeEquals(calc, otp.CodeHash);
        }

        private void Send2FACode(Utilisateur user, EmailService? emailService)
        {
            emailService ??= HttpContext.RequestServices.GetService(typeof(EmailService)) as EmailService;

            CreateOtp(user.Id, "2FA", TimeSpan.FromMinutes(10));
            var code = (string)HttpContext.Items["__otp_code"]!;

            try
            {
                emailService?.Send(user.Email ?? "", "Code de connexion (2FA)", $"Votre code est : {code}\nValable 10 minutes.");
            }
            catch { }

            LogSecurity(user.Id, "2FA_CODE_SENT", null);
        }

        // ===== Trusted device =====
        private void CreateTrustedDevice(int userId, string deviceName)
        {
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

            Response.Cookies.Append(TRUST_COOKIE, $"{td.Id}:{token}", new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.Add(TRUST_DURATION)
            });
        }

        private bool IsTrustedDevice(int userId)
        {
            if (!Request.Cookies.TryGetValue(TRUST_COOKIE, out var value)) return false;
            if (string.IsNullOrWhiteSpace(value)) return false;

            var parts = value.Split(':');
            if (parts.Length != 2) return false;

            if (!int.TryParse(parts[0], out var id)) return false;
            var token = parts[1];

            var td = _context.TrustedDevices.FirstOrDefault(x => x.Id == id && x.UserId == userId);
            if (td == null) return false;
            if (td.ExpiresAt <= DateTime.UtcNow) return false;

            var calc = CalculerSHA256AvecSalt(token, td.TokenSalt);
            return CryptographicOperations.FixedTimeEquals(calc, td.TokenHash);
        }
    }
}