using Easy.Contracts;
using Easy.Data;
using Easy.MainApi.Grading;
using Easy.Platform;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Easy.MainApi.Examiners;

// S05 (issues 35, 40): admins deactivate and reactivate examiners, API only. Deactivating releases the examiner's
// registrations, the same as unregistering; their places reopen.
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
            await db.SaveChangesAsync(); // first: the row lock waits for running registrations, so the read below sees them
            var released = await db.Registrations.Where(r => r.ExaminerId == id).ToListAsync();
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
