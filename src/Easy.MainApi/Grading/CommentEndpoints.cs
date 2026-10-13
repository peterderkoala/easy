using System.Security.Claims;
using Easy.Contracts;
using Easy.Data;
using Easy.MainApi.Participants;
using Easy.MainApi.Registrations;
using Easy.Platform;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Easy.MainApi.Grading;

// S06 (issues 37, 38): one open comment thread per documentation, optionally on a section, by any examiner. No points,
// so not blinded; the text is encrypted under the participant key (S02). The hub event carries the ID and author only.
public static class CommentEndpoints
{
    public record Body(string? Section, string? Text);

    static readonly HashSet<string> Sections = [.. GradingMatrix.Sections.Select(s => s.Key)];

    public static void MapComments(this WebApplication app)
    {
        var comments = app.MapGroup("/api/participants/{participantId:guid}/comments").RequireAuthorization(p => p.RequireRole("examiner")); // admins hold it too

        comments.MapGet("", async (Guid participantId, EasyDb db, ParticipantKeys keys) =>
        {
            if (await db.Participants.FindAsync(participantId) is not { } participant)
                return Results.NotFound();
            var rows = await (from c in db.Comments
                              join e in db.Examiners on c.ExaminerId equals e.Id
                              where c.ParticipantId == participantId
                              orderby c.CreatedAt
                              select new { c.Id, Author = e.DisplayName, c.SectionKey, c.Text, c.CreatedAt }).ToListAsync();
            using var key = participant.KeyForgotten ? null : keys.Open(participant);
            return Results.Ok(rows.Select(c => new { c.Id, c.Author, Section = c.SectionKey, Text = key?.Decrypt(c.Text), c.CreatedAt })
                .ToList()); // decrypts now: the key is zeroed when this returns
        });

        comments.MapPost("", async (Guid participantId, Body body, ClaimsPrincipal user, EasyDb db, ParticipantKeys keys,
            IHubContext<GradingHub, IGradingClient> hub, HttpContext context) =>
        {
            if (body.Section is not null && !Sections.Contains(body.Section))
                return Results.Problem($"Unknown section {body.Section}.", statusCode: 400);
            if (string.IsNullOrWhiteSpace(body.Text))
                return Results.Problem("A comment needs text.", statusCode: 400);
            // Shared locks until UseOutbox commits: a concurrent deactivation or graded transition waits and then sees this comment.
            if (await RegistrationEndpoints.LockedCaller(db, user) is not { Active: true } examiner)
                return Results.Problem("Only active examiners comment.", statusCode: 403);
            if (await db.Participants.FromSql($"SELECT * FROM participants WHERE id = {participantId} FOR SHARE").SingleOrDefaultAsync() is not { KeyForgotten: false } participant)
                return Results.Problem("Unknown participant.", statusCode: 404);
            if (participant.DocumentationStatus is DocumentationStatus.Graded)
                return Results.Problem("The documentation is graded; the thread is closed.", statusCode: 409);

            using var key = keys.Open(participant);
            var comment = db.Comments.Add(new()
            {
                ParticipantId = participantId, ExaminerId = examiner.Id, SectionKey = body.Section,
                Text = key.Encrypt(body.Text), CreatedAt = DateTimeOffset.UtcNow,
            }).Entity;
            await db.SaveChangesAsync();
            context.AfterCommit(() => hub.Clients.Group(GradingHub.Group(participantId)).CommentAdded(participantId, comment.Id, examiner.DisplayName));
            return Results.Created((string?)null, new { comment.Id }); // no single-comment route
        });
    }
}
