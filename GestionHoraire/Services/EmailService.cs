using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Mail;

namespace GestionHoraire.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;
        public EmailService(IConfiguration config) { _config = config; }

        public bool IsConfigured()
        {
            return !string.IsNullOrWhiteSpace(_config["Smtp:Host"])
                && !string.IsNullOrWhiteSpace(_config["Smtp:User"])
                && !string.IsNullOrWhiteSpace(_config["Smtp:Pass"])
                && !string.IsNullOrWhiteSpace(_config["Smtp:From"])
                && !string.IsNullOrWhiteSpace(_config["Smtp:Port"]);
        }

        public void Send(string to, string subject, string body)
        {
            if (!IsConfigured()) return; // évite crash si pas configuré

            var host = _config["Smtp:Host"]!;
            var port = int.Parse(_config["Smtp:Port"]!);
            var user = _config["Smtp:User"]!;
            var pass = _config["Smtp:Pass"]!;
            var from = _config["Smtp:From"]!;

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(user, pass)
            };

            client.Send(new MailMessage(from, to, subject, body));
        }
    }
}