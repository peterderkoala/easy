using System.Security.Claims;
using Easy.Contracts;
using Easy.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Rebus.Bus;

namespace Easy.MainApi.Grades;

// S04/S11: set the current value of one point source. The audit interceptor adds the audit entry to the same save,
// and UseOutbox commits grade, audit entry and GradeChanged together.
public static class SetGrade
{
    public record Body(decimal Value);

    public static void MapSetGrade(this WebApplication app) =>
        app.MapPut("/api/participants/{participantId:guid}/grades/{source}", async (Guid participantId, GradeSource source, Body body,
            ClaimsPrincipal user, EasyDb db, IBus bus) =>
        {
            if (body.Value is < 0 or > 9999.9m || decimal.Round(body.Value, 1) != body.Value) // numeric(5,1), non-negative
                return Results.Problem("Value must be between 0 and 9999.9 with at most one decimal place.", statusCode: 400);

            string subject = user.FindFirstValue("sub")!;
            var examiner = await db.Examiners.SingleOrDefaultAsync(e => e.Subject == subject);
            if (examiner is null)
                return Results.Problem("No examiner mirror; the client posts /api/me after login.", statusCode: 403);

            // ponytail: no optimistic concurrency (out of scope); two concurrent first writes of one source: one gets a 500
            var grade = await db.Grades.FindAsync(participantId, source)
                ?? db.Grades.Add(new() { ParticipantId = participantId, Source = source }).Entity;
            if (grade.Value == body.Value && db.Entry(grade).State == EntityState.Unchanged)
                return Results.NoContent(); // no change: no audit entry, no event
            grade.Value = body.Value + 0.0m; // scale 1, as Postgres returns it, so audit old/new values read alike
            grade.UpdatedBy = examiner.Id;
            grade.UpdatedAt = DateTimeOffset.UtcNow;
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
            {
                return Results.Problem("Unknown participant.", statusCode: 404); // UseOutbox rolls back
            }

            await bus.Publish(new GradeChanged(participantId, source.ToString(), Guid.NewGuid()));
            return Results.NoContent();
        }).RequireAuthorization(p => p.RequireRole("examiner"));
}
