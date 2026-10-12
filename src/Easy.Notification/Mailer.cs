using System.Net;
using System.Net.Mail;
using Easy.Data;
using Microsoft.EntityFrameworkCore;

namespace Easy.Notification;

// S12: one plain-text German mail per active examiner of the given ones (default: all).
// ponytail: if a later mail of the batch fails, the retry resends the earlier ones; per-recipient dedupe when that hurts
public static class Mailer
{
    public static async Task MailActiveExaminers(EasyDb db, IConfiguration config, string subject, string body, IQueryable<Examiner>? examiners = null)
    {
        var recipients = await (examiners ?? db.Examiners).Where(e => e.Active && e.Email != "").Select(e => e.Email).ToListAsync();

        using var smtp = new SmtpClient(config["Smtp:Host"] ?? throw new InvalidOperationException("Smtp:Host is not configured."), config.GetValue("Smtp:Port", 25))
        {
            EnableSsl = config.GetValue("Smtp:EnableSsl", false), // STARTTLS for Mailcow; Mailpit has none
            Credentials = config["Smtp:User"] is { Length: > 0 } user ? new NetworkCredential(user, config["Smtp:Password"]) : null,
        };
        foreach (string to in recipients)
        {
            using var mail = new MailMessage(config["Smtp:From"] ?? "easy@localhost", to, subject,
                body + "\n\nDiese Nachricht wurde automatisch von Easy versendet.");
            await smtp.SendMailAsync(mail);
        }
    }
}
