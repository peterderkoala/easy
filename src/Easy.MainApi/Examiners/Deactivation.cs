using Easy.Contracts;
using Easy.Data;
using Easy.MainApi.Grading;
using Easy.Platform;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Easy.MainApi.Examiners;

// S05 (issues 35, 40): admins deactivate and reactivate examiners, API only. Deactivating releases the examiner's
// registrations without a submitted grading, the same as unregistering; their places reopen. Submitted gradings stay and count.
public static class Deactivation
{
    public static void MapDeactivation(this WebApplication app)
    {
        var admin = app.MapGroup("/api/examiners/{id:guid}").RequireAuthorization(p => p.RequireRole("admin"));

        admin.MapPost("/deactivate", async (Guid id, EasyDb db, IHubContext<GradingHub, IGradingClient> hub, HttpContext context) =>
        {
            if (await db.Examiners.FindAsync(id) is not { } examiner)
                return Results.NotFound();
            examiner.Active = false;
            await db.SaveChangesAsync(); // first: the row lock waits for running registrations and grading writes, so the reads below see them
            var released = await db.Registrations.Where(r => r.ExaminerId == id && !db.ExaminerGradings
                .Any(g => g.ParticipantId == r.ParticipantId && g.ExaminerId == id && g.Status == ExaminerGradingStatus.Submitted)).ToListAsync();
            // ponytail: bypasses the audit (S11 decides drafts); load and remove the rows if discarded drafts must be logged
            await db.ExaminerGradings.Where(g => g.ExaminerId == id && g.Status != ExaminerGradingStatus.Submitted).ExecuteDeleteAsync();
            db.Registrations.RemoveRange(released);
            await db.SaveChangesAsync();
            foreach (var r in released)
                context.AfterCommit(() => hub.Clients.Group(GradingHub.Group(r.ParticipantId)).RegistrationReleased(r.ParticipantId, examiner.DisplayName));
            return Results.NoContent();
        });

        admin.MapPost("/reactivate", async (Guid id, EasyDb db) =>
        {
            if (await db.Examiners.FindAsync(id) is not { } examiner)
                return Results.NotFound();
            examiner.Active = true;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
