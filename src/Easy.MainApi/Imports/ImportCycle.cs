using System.Globalization;
using Easy.Contracts;
using Easy.Data;
using Easy.MainApi.Participants;
using Easy.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rebus.Bus;
using Rebus.Handlers;
using Rebus.Pipeline;

namespace Easy.MainApi.Imports;

// MainApi-internal command (SendLocal): only MainApi holds the master secret. The CSV travels encrypted under a
// one-off data key, so no plain participant data sits in the outbox table or the broker.
public record ImportCycle(Guid JobId, byte[] WrappedKey, int KeyVersion, byte[] Csv)
{
    public string EventId => $"import-cycle:{JobId}";

    public static ImportCycle Create(Guid jobId, byte[] csv, ParticipantKeys keys)
    {
        using var key = keys.Create();
        return new(jobId, key.Wrapped, key.Version, key.EncryptBytes(csv));
    }
}

// S02: all-or-nothing. Dedupe, job outcome, cycle, participants and CycleImported commit in the message's one transaction.
// ponytail: an unexpected exception (DB down, key mismatch) dead-letters the message after Rebus retries and the job stays
// Queued; mark it failed from a second-level retry handler when that matters.
public class ImportCycleHandler(EasyDb db, ParticipantKeys keys, IOptions<ImportOptions> options, IMessageContext context, IBus bus)
    : IHandleMessages<ImportCycle>
{
    public async Task Handle(ImportCycle message)
    {
        context.UseOutboxTransaction(db);
        if (!await context.FirstDelivery(message.EventId))
            return;

        var job = await db.ImportJobs.SingleAsync(j => j.Id == message.JobId);
        byte[] csv;
        using (var fileKey = keys.Open(message.WrappedKey, message.KeyVersion))
            csv = fileKey.DecryptBytes(message.Csv);
        var result = CsvImport.Parse(csv, options.Value);
        // A concurrent import of the same cycle passes this check too; the unique index then fails its commit,
        // which rolls back everything incl. FirstDelivery, and the Rebus retry lands here and fails the job.
        if (await db.Cycles.AnyAsync(c => c.Type == job.CycleType && c.Year == job.Year))
            result = result with { Errors = [new(0, "", "Cycle already exists.")] };
        if (result.Errors.Count > 0)
        {
            job.Status = ImportStatus.Failed;
            job.Errors = result.Errors;
            await db.SaveChangesAsync();
            return;
        }

        var cycle = db.Cycles.Add(new() { Id = Guid.NewGuid(), Type = job.CycleType, Year = job.Year }).Entity;
        string prefix = $"{(job.CycleType == CycleType.Winter ? "WS" : "SS")}{job.Year}";
        for (int i = 0; i < result.Rows.Count; i++)
        {
            var row = result.Rows[i];
            string csvId = row.CsvId.ToString(CultureInfo.InvariantCulture);
            using var key = keys.Create();
            db.Participants.Add(new()
            {
                CycleId = cycle.Id,
                DisplayNo = $"{prefix}-{i + 1:000}", // file order (S02)
                WrappedKey = key.Wrapped,
                KeyVersion = key.Version,
                CsvId = key.Encrypt(csvId),
                CsvIdHash = keys.BlindIndex(cycle.Id, csvId),
                Name = key.Encrypt(row.Name),
                ProjectTitle = key.Encrypt(row.ProjectTitle),
                ApplicationStatus = ApplicationStatus.Submitted,
            });
        }
        job.Status = ImportStatus.Succeeded;
        job.CycleId = cycle.Id;
        await db.SaveChangesAsync();
        await bus.Publish(new CycleImported(cycle.Id));
    }
}
