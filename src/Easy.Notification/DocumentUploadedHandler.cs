using Easy.Contracts;
using Easy.Data;
using Easy.Platform;
using Microsoft.EntityFrameworkCore;
using Rebus.Handlers;
using Rebus.Pipeline;

namespace Easy.Notification;

// S12: mail the examiners per uploaded documentation. Idempotent per event ID.
public class DocumentUploadedHandler(EasyDb db, IMessageContext context, IConfiguration config) : IHandleMessages<DocumentUploaded>
{
    public async Task Handle(DocumentUploaded message)
    {
        context.UseOutboxTransaction(db);
        if (!await context.FirstDelivery(message.EventId))
            return;

        string displayNo = await db.Participants.Where(p => p.Id == message.ParticipantId).Select(p => p.DisplayNo).SingleAsync();
        await Mailer.MailActiveExaminers(db, config, $"Neue Dokumentation: {displayNo}",
            $"Für Teilnehmer {displayNo} wurde eine Dokumentation hochgeladen.");
    }
}
