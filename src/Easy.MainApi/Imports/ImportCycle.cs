using System.Globalization;
using Easy.Contracts;
using Easy.Data;
using Easy.MainApi.Participants;
using Easy.MainApi.Settings;
using Easy.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rebus.Bus;
using Rebus.Handlers;
using Rebus.Pipeline;
using Rebus.Retry.Simple;

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
// An unexpected exception (DB down, key mismatch) ends, after all Rebus retries, in the IFailed handler below.
public class ImportCycleHandler(EasyDb db, ParticipantKeys keys, IOptions<ImportOptions> options, IMessageContext context, IBus bus,
    ILogger<ImportCycleHandler> logger) : IHandleMessages<ImportCycle>, IHandleMessages<IFailed<ImportCycle>>
{
    // S02 (issue 28): fail the job and return, so Rebus drops the message and no copy of the encrypted CSV reaches the
    // error queue. Only if this throws too is the message dead-lettered (7-day TTL there).
    public async Task Handle(IFailed<ImportCycle> failed)
    {
        var job = await db.ImportJobs.SingleAsync(j => j.Id == failed.Message.JobId);
        if (job.Status != ImportStatus.Queued)
            return;
        // Type only: an exception message could quote CSV values. Rebus logs each attempt's exception in full.
        logger.LogError("Import job {JobId} failed after all retries: {ExceptionType}", job.Id, failed.Exceptions.LastOrDefault()?.Type);
        job.Status = ImportStatus.Failed;
        job.Errors = [new(0, "", "Import failed. Please upload the file again.")];
        await db.SaveChangesAsync();
    }

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
        // A concurrent import of the same cycle can pass this check too; its cycle insert then hits the unique index (23505),
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

        var cycle = db.Cycles.Add(new()
        {
            Id = Guid.NewGuid(), Type = job.CycleType, Year = job.Year, Weights = await SettingsEndpoints.DefaultWeights(db), // S06
        }).Entity;
        string prefix = Cycle.Label(job.CycleType, job.Year);
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
