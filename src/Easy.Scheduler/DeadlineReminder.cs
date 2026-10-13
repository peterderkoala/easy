using Easy.Contracts;
using Easy.Data;
using Easy.Platform;
using Hangfire.Server;
using Microsoft.EntityFrameworkCore;
using Rebus.Bus;
using Serilog.Context;

namespace Easy.Scheduler;

// S12 skeleton reminder: documentation due dates only. Publishes directly, no outbox (the job writes nothing);
// a failed publish fails the job and Hangfire retries, and the event ID keeps retries to one mail.
public class DeadlineReminder(EasyDb db, IBus bus, TimeProvider clock, IConfiguration config)
{
    public const string JobId = "deadline-reminder";
    public static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"); // due dates are German calendar days

    // Hangfire entry point: the job id is the correlation ID (S11).
    public Task Execute(PerformContext context) => Run(context.BackgroundJob.Id);

    public async Task Run(string correlationId)
    {
        CorrelationId.Current = correlationId;
        using var _ = LogContext.PushProperty("CorrelationId", correlationId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Berlin).DateTime);
        var last = today.AddDays(config.GetValue("Reminder:WindowDays", 3));
        var due = await db.Cycles.Where(c => c.DocumentationDueDate >= today && c.DocumentationDueDate <= last)
            .Select(c => new { c.Id, Due = c.DocumentationDueDate!.Value }).ToListAsync();
        foreach (var cycle in due)
            await bus.Publish(new DeadlineApproaching(cycle.Id, "documentation", cycle.Due, cycle.Due.DayNumber - today.DayNumber));
    }
}
