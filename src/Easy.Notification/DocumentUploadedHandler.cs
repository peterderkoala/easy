using Easy.Contracts;
using Easy.Data;
using Easy.Platform;
using Microsoft.EntityFrameworkCore;
using Rebus.Handlers;
using Rebus.Pipeline;

namespace Easy.Notification;

// S12: mail the registered examiners per uploaded documentation, or all active ones if nobody registered yet (issue 40).
// Idempotent per event ID.
public class DocumentUploadedHandler(EasyDb db, IMessageContext context, IConfiguration config) : IHandleMessages<DocumentUploaded>
{
    public async Task Handle(DocumentUploaded message)
    {
        context.UseOutboxTransaction(db);
        if (!await context.FirstDelivery(message.EventId))
            return;

        string displayNo = await db.Participants.Where(p => p.Id == message.ParticipantId).Select(p => p.DisplayNo).SingleAsync();
        var registered = db.Examiners.Where(e => e.Active && e.Email != ""
            && db.Registrations.Any(r => r.ParticipantId == message.ParticipantId && r.ExaminerId == e.Id)); // reachable ones only
        await Mailer.MailActiveExaminers(db, config, $"Neue Dokumentation: {displayNo}",
            $"Für Teilnehmer {displayNo} wurde eine Dokumentation hochgeladen.", await registered.AnyAsync() ? registered : null);
    }
}
