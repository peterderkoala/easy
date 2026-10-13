using System.Security.Claims;
using Easy.Contracts;
using Easy.Data;
using Easy.MainApi.Grading;
using Easy.MainApi.Participants;
using Easy.Platform;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Easy.MainApi.Registrations;

// S05 (issue 35): an active examiner registers themselves for a documentation from the import on, first come first
// served, at most two. Registrations (names only) are visible to all examiners.
public static class RegistrationEndpoints
{
    public static void MapRegistrations(this WebApplication app)
    {
        var examiners = app.MapGroup("/api").RequireAuthorization(p => p.RequireRole("examiner")); // admins hold it too

        examiners.MapPost("/participants/{participantId:guid}/registration", async (Guid participantId, ClaimsPrincipal user, EasyDb db,
            IHubContext<GradingHub, IGradingClient> hub, HttpContext context) =>
        {
            // Locks, held until UseOutbox commits: the participant row serializes registrations for one documentation,
            // so the slot choice below sees the others; the shared examiner lock makes a concurrent deactivation wait.
            var examiner = await LockedCaller(db, user);
            if (examiner is not { Active: true })
                return Results.Problem("Only active examiners register.", statusCode: 403);
            if (!await db.Participants.FromSql($"SELECT * FROM participants WHERE id = {participantId} FOR UPDATE").AnyAsync(p => !p.KeyForgotten))
                return Results.Problem("Unknown participant.", statusCode: 404);

            var taken = await db.Registrations.Where(r => r.ParticipantId == participantId).ToListAsync();
            if (taken.Any(r => r.ExaminerId == examiner.Id))
                return Results.NoContent();
            int slot = new[] { 1, 2 }.Except(taken.Select(r => r.Slot)).FirstOrDefault(); // 0: none free
            if (slot == 0)
                return Results.Problem("Both places are taken.", statusCode: 409);

            db.Registrations.Add(new() { ParticipantId = participantId, ExaminerId = examiner.Id, Slot = slot, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            context.AfterCommit(() => hub.Clients.Group(GradingHub.Group(participantId)).RegistrationTaken(participantId, examiner.DisplayName));
            return Results.NoContent();
        });

        // Only before submit (issue 35); discards the draft. The row lock makes a concurrent grading write (shared lock) finish first.
        examiners.MapDelete("/participants/{participantId:guid}/registration", async (Guid participantId, ClaimsPrincipal user, EasyDb db,
            IHubContext<GradingHub, IGradingClient> hub, HttpContext context) =>
        {
            if (await Caller(db, user) is not { } examiner
                || await db.Registrations.FromSql($"SELECT * FROM registrations WHERE participant_id = {participantId} AND examiner_id = {examiner.Id} FOR UPDATE")
                    .SingleOrDefaultAsync() is not { } registration)
                return Results.NoContent();
            if (await db.Participants.AnyAsync(p => p.Id == participantId && p.DocumentationStatus == DocumentationStatus.Graded))
                return Results.Problem("The documentation is graded; its grading is final.", statusCode: 409);
            var grading = db.ExaminerGradings.Where(g => g.ParticipantId == participantId && g.ExaminerId == examiner.Id);
            if (await grading.AnyAsync(g => g.Status == ExaminerGradingStatus.Submitted))
                return Results.Problem("A submitted grading stays; withdraw it first.", statusCode: 409);
            // ponytail: bypasses the audit (S11 decides drafts); load and remove the rows if discarded drafts must be logged
            await grading.ExecuteDeleteAsync(); // criteria cascade
            db.Registrations.Remove(registration);
            await db.SaveChangesAsync();
            context.AfterCommit(() => hub.Clients.Group(GradingHub.Group(participantId)).RegistrationReleased(participantId, examiner.DisplayName));
            return Results.NoContent();
        });

        // S05, S06 (issue 52): active examiners to invite as third examiner, names only.
        examiners.MapGet("/examiners", async (EasyDb db) =>
            await db.Examiners.Where(e => e.Active).OrderBy(e => e.DisplayName).Select(e => new { e.Id, e.DisplayName }).ToListAsync());

        // S02, S05 (issue 51): the client's way to a cycle's documentation list. Newest first: winter (WS2026/27) after summer of a year.
        examiners.MapGet("/cycles", async (EasyDb db) =>
            await db.Cycles.OrderByDescending(c => c.Year).ThenByDescending(c => c.Type == CycleType.Winter)
                .Select(c => new { c.Id, Type = c.Type.ToString(), c.Year }).ToListAsync());

        examiners.MapGet("/cycles/{cycleId:guid}/documentations", async (Guid cycleId, ClaimsPrincipal user, EasyDb db, ParticipantKeys keys) =>
        {
            if (!await db.Cycles.AnyAsync(c => c.Id == cycleId))
                return Results.NotFound();
            Guid? caller = (await Caller(db, user))?.Id;
            var participants = await db.Participants.Where(p => p.CycleId == cycleId).OrderBy(p => p.DisplayNo).ToListAsync();
            var registrations = await (from r in db.Registrations
                                       join e in db.Examiners on r.ExaminerId equals e.Id
                                       join p in db.Participants on r.ParticipantId equals p.Id
                                       where p.CycleId == cycleId
                                       orderby r.Slot
                                       select new { r.ParticipantId, r.ExaminerId, e.DisplayName }).ToListAsync();
            return Results.Ok(participants.Select(p =>
            {
                var own = registrations.Where(r => r.ParticipantId == p.Id).ToList();
                return new
                {
                    p.Id,
                    p.DisplayNo,
                    Name = Name(p, keys),
                    DocumentationStatus = p.DocumentationStatus?.ToString(), // as in the grading read
                    Examiners = own.Select(r => r.DisplayName),
                    Registered = own.Any(r => r.ExaminerId == caller),
                };
            }));
        });
    }

    internal static Task<Examiner?> Caller(EasyDb db, ClaimsPrincipal user)
    {
        string subject = user.FindFirstValue("sub")!;
        return db.Examiners.SingleOrDefaultAsync(e => e.Subject == subject);
    }

    // Shared row lock until UseOutbox commits: a concurrent deactivation waits and then sees this write.
    internal static Task<Examiner?> LockedCaller(EasyDb db, ClaimsPrincipal user)
    {
        string subject = user.FindFirstValue("sub")!;
        return db.Examiners.FromSql($"SELECT * FROM examiners WHERE subject = {subject} FOR SHARE").SingleOrDefaultAsync();
    }

    // Null after forget (S02).
    static string? Name(Participant p, ParticipantKeys keys)
    {
        if (p.KeyForgotten) return null;
        using var key = keys.Open(p);
        return key.Decrypt(p.Name);
    }
}
