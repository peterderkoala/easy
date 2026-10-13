using Easy.Contracts;
using Easy.Data;
using Easy.Platform;
using Microsoft.EntityFrameworkCore;
using Rebus.Handlers;
using Rebus.Pipeline;

namespace Easy.Notification;

// S12 skeleton reminder: mail the examiners that the documentation of a cycle is due soon. Idempotent per event ID.
public class DeadlineApproachingHandler(EasyDb db, IMessageContext context, IConfiguration config) : IHandleMessages<DeadlineApproaching>
{
    public async Task Handle(DeadlineApproaching message)
    {
        context.UseOutboxTransaction(db);
        if (!await context.FirstDelivery(message.EventId))
            return;

        var cycle = await db.Cycles.Where(c => c.Id == message.CycleId).Select(c => new { c.Type, c.Year }).SingleAsync();
        string name = Cycle.Label(cycle.Type, cycle.Year);
        string days = message.DaysLeft switch { 0 => "heute", 1 => "morgen", _ => $"in {message.DaysLeft} Tagen" };
        await Mailer.MailActiveExaminers(db, config, $"Abgabe Dokumentation {name}: {days} fällig",
            $"Die Dokumentationen des Durchgangs {name} sind {days} fällig ({message.DueDate:dd.MM.yyyy}).");
    }
}
