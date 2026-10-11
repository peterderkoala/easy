using Easy.Data;
using Easy.MainApi.Participants;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Rebus.Bus;

namespace Easy.MainApi.Imports;

// S02/S05: admin-only CSV import. The upload only queues a job; the import runs in ImportCycleHandler.
public static class ImportEndpoints
{
    public static void MapImports(this WebApplication app)
    {
        var imports = app.MapGroup("/api/imports").RequireAuthorization(p => p.RequireRole("admin"));

        // Bearer token, no cookies: no antiforgery token needed for the form post.
        imports.MapPost("/", async ([FromForm] CycleType type, [FromForm] int year, IFormFile file,
            EasyDb db, IBus bus, ParticipantKeys keys, IOptions<ImportOptions> options) =>
        {
            if (year is < 2000 or > 2100)
                return Results.Problem("Year must be between 2000 and 2100.", statusCode: 400);
            if (file.Length > options.Value.MaxBytes) // the handler checks again; this keeps big files out of the broker
                return Results.Problem($"File larger than {options.Value.MaxBytes} bytes.", statusCode: 400);

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer);
            var job = db.ImportJobs.Add(new()
            {
                Id = Guid.NewGuid(), CycleType = type, Year = year, Status = ImportStatus.Queued, CreatedAt = DateTimeOffset.UtcNow,
            }).Entity;
            await db.SaveChangesAsync();
            await bus.SendLocal(ImportCycle.Create(job.Id, buffer.ToArray(), keys)); // outbox: sent only if the job row commits
            return Results.Accepted($"/api/imports/{job.Id}", new { job.Id });
        }).DisableAntiforgery();

        // ponytail: the client polls this; SignalR progress (optional in spec 6) comes with the hubs of spec 8
        imports.MapGet("/{id:guid}", async (Guid id, EasyDb db) =>
            await db.ImportJobs.FindAsync(id) is { } job
                ? Results.Ok(new { job.Id, Status = job.Status.ToString(), job.CycleId, job.Errors })
                : Results.NotFound());
    }
}
