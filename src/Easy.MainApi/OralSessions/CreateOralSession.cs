using Easy.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Easy.MainApi.OralSessions;

// S09 skeleton join path: any examiner opens an oral session; ExamRoom only reads the table.
// One session per participant (unique index), so per cycle.
// ponytail: no assignment check; the grading slice adds the rules.
public static class CreateOralSession
{
    public record Body(Guid ParticipantId);

    public static void MapCreateOralSession(this WebApplication app) =>
        app.MapPost("/api/oral-sessions", async (Body body, EasyDb db) =>
        {
            var session = db.OralSessions.Add(new() { ParticipantId = body.ParticipantId, Status = OralSessionStatus.Open }).Entity;
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
            {
                return Results.Problem("Unknown participant.", statusCode: 404);
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return Results.Problem("The participant already has an oral session.", statusCode: 409);
            }
            return Results.Created($"/api/oral-sessions/{session.Id}", new { session.Id });
        }).RequireAuthorization(p => p.RequireRole("examiner"));
}
