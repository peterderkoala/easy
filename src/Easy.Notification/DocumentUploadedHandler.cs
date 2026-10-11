using System.Net;
using System.Net.Mail;
using Easy.Contracts;
using Easy.Data;
using Easy.Platform;
using Microsoft.EntityFrameworkCore;
using Rebus.Handlers;
using Rebus.Pipeline;

namespace Easy.Notification;

// S12: one plain-text German mail to every active examiner per uploaded documentation. Idempotent per event ID.
// ponytail: if a later mail of the batch fails, the retry resends the earlier ones; per-recipient dedupe when that hurts
public class DocumentUploadedHandler(EasyDb db, IMessageContext context, IConfiguration config) : IHandleMessages<DocumentUploaded>
{
    public async Task Handle(DocumentUploaded message)
    {
        context.UseOutboxTransaction(db);
        if (!await context.FirstDelivery(message.EventId))
            return;

        string displayNo = await db.Participants.Where(p => p.Id == message.ParticipantId).Select(p => p.DisplayNo).SingleAsync();
        var recipients = await db.Examiners.Where(e => e.Active && e.Email != "").Select(e => e.Email).ToListAsync();

        using var smtp = new SmtpClient(config["Smtp:Host"] ?? throw new InvalidOperationException("Smtp:Host is not configured."), config.GetValue("Smtp:Port", 25))
        {
            EnableSsl = config.GetValue("Smtp:EnableSsl", false), // STARTTLS for Mailcow; Mailpit has none
            Credentials = config["Smtp:User"] is { Length: > 0 } user ? new NetworkCredential(user, config["Smtp:Password"]) : null,
        };
        foreach (string to in recipients)
        {
            using var mail = new MailMessage(config["Smtp:From"] ?? "easy@localhost", to,
                $"Neue Dokumentation: {displayNo}",
                $"Für Teilnehmer {displayNo} wurde eine Dokumentation hochgeladen.\n\nDiese Nachricht wurde automatisch von Easy versendet.");
            await smtp.SendMailAsync(mail);
        }
    }
}
