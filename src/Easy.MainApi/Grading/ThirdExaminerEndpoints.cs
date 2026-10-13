using System.Security.Claims;
using Easy.Contracts;
using Easy.Data;
using Easy.MainApi.Registrations;
using Easy.Platform;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Easy.MainApi.Grading;

// S06 (issues 34, 40): on a stored deviation either registered examiner invites a third examiner by name; the invitee
// accepts or declines, and may step back (decline) before submit, which discards their draft. An open or accepted
// invitation freezes both registered gradings. The accepted third examiner grades through ExaminerGradingEndpoints;
// their submit grades with the average of three (DocumentationScore.Check). Every transition takes the participant row lock.
public static class ThirdExaminerEndpoints
{
    public record Body(Guid ExaminerId);

    public static void MapThirdExaminer(this WebApplication app)
    {
        var third = app.MapGroup("/api/participants/{participantId:guid}/third-examiner").RequireAuthorization(p => p.RequireRole("examiner")); // admins hold it too

        third.MapPost("", async (Guid participantId, Body body, ClaimsPrincipal user, EasyDb db,
            IHubContext<GradingHub, IGradingClient> hub, HttpContext context) =>
        {
            if (await ExaminerGradingEndpoints.Grader(db, user, participantId) is not { } inviter)
                return Results.Problem("Only active registered examiners invite.", statusCode: 403);
            // Locks until UseOutbox commits: the participant row serializes with withdraw, accept and a second invite; the
            // shared invitee lock makes a concurrent deactivation wait and then release this invitation.
            var participant = await db.Participants.FromSql($"SELECT * FROM participants WHERE id = {participantId} FOR UPDATE").SingleAsync();
            if (participant.KeyForgotten)
                return Results.Problem("Unknown participant.", statusCode: 404);
            if (participant.DocumentationStatus is DocumentationStatus.Graded)
                return Results.Problem(ExaminerGradingEndpoints.GradedFinal, statusCode: 409);
            if (!await db.Deviations.AnyAsync(d => d.ParticipantId == participantId))
                return Results.Problem("A third examiner is invited only on a deviation.", statusCode: 409);
            if (await Frozen(db, participantId))
                return Results.Problem("A third examiner is already invited.", statusCode: 409);
            var invitee = await db.Examiners.FromSql($"SELECT * FROM examiners WHERE id = {body.ExaminerId} FOR SHARE").SingleOrDefaultAsync();
            if (invitee is not { Active: true } || await db.Registrations.AnyAsync(r => r.ParticipantId == participantId && r.ExaminerId == invitee.Id))
                return Results.Problem("Invite an active examiner other than the registered pair.", statusCode: 400);

            db.ThirdExaminerInvitations.Add(new()
            {
                ParticipantId = participantId, ExaminerId = invitee.Id, InvitedBy = inviter.Id, Status = ThirdExaminerStatus.Invited, CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            context.AfterCommit(() => hub.Clients.Group(GradingHub.Group(participantId)).ThirdExaminerInvited(participantId, invitee.DisplayName));
            return Results.NoContent();
        });

        third.MapPost("/accept", async (Guid participantId, ClaimsPrincipal user, EasyDb db,
            IHubContext<GradingHub, IGradingClient> hub, HttpContext context) =>
        {
            if (await Invitee(db, user, participantId) is not ({ } examiner, { } invitation, { } participant))
                return Results.Problem("Only the invited examiner accepts.", statusCode: 403);
            if (participant.KeyForgotten)
                return Results.Problem("Unknown participant.", statusCode: 404);
            if (invitation.Status is ThirdExaminerStatus.Accepted)
                return Results.NoContent();
            invitation.Status = ThirdExaminerStatus.Accepted;
            await db.SaveChangesAsync();
            context.AfterCommit(() => hub.Clients.Group(GradingHub.Group(participantId)).ThirdExaminerAccepted(participantId, examiner.DisplayName));
            return Results.NoContent();
        });

        // Declining an invitation, or stepping back after accepting: back to a plain deviation (issue 40).
        third.MapPost("/decline", async (Guid participantId, ClaimsPrincipal user, EasyDb db,
            IHubContext<GradingHub, IGradingClient> hub, HttpContext context) =>
        {
            if (await Invitee(db, user, participantId) is not ({ } examiner, { } invitation, { } participant))
                return Results.Problem("Only the invited examiner declines.", statusCode: 403);
            if (participant.DocumentationStatus is DocumentationStatus.Graded)
                return Results.Problem(ExaminerGradingEndpoints.GradedFinal, statusCode: 409); // the third submit grades
            invitation.Status = ThirdExaminerStatus.Declined;
            // ponytail: bypasses the audit (S11 decides drafts); load and remove the rows if discarded drafts must be logged
            await db.ExaminerGradings.Where(g => g.ParticipantId == participantId && g.ExaminerId == examiner.Id).ExecuteDeleteAsync(); // criteria cascade
            await db.SaveChangesAsync();
            context.AfterCommit(() => hub.Clients.Group(GradingHub.Group(participantId)).ThirdExaminerDeclined(participantId, examiner.DisplayName));
            return Results.NoContent();
        });
    }

    // An invited or accepted invitation freezes both registered gradings: no withdraw, no accepting the deviation.
    internal static Task<bool> Frozen(EasyDb db, Guid participantId) =>
        db.ThirdExaminerInvitations.AnyAsync(i => i.ParticipantId == participantId && i.Status != ThirdExaminerStatus.Declined);

    // The active caller's open or accepted invitation. Locks until UseOutbox commits, in the order of the third examiner's
    // grading writes (examiner, invitation, participant), so a step back and a submit queue instead of deadlocking: the
    // shared examiner lock makes a concurrent deactivation wait and then see this transition; the invitation row makes
    // a concurrent draft save (shared lock) finish first; the participant row serializes with the pair's writes.
    static async Task<(Examiner, ThirdExaminerInvitation, Participant)?> Invitee(EasyDb db, ClaimsPrincipal user, Guid participantId)
    {
        if (await RegistrationEndpoints.LockedCaller(db, user) is not { Active: true } examiner)
            return null;
        var invitation = await db.ThirdExaminerInvitations.FromSql(
            $"SELECT * FROM third_examiner_invitations WHERE participant_id = {participantId} AND examiner_id = {examiner.Id} AND status <> 'Declined' FOR UPDATE").SingleOrDefaultAsync();
        if (invitation is null)
            return null;
        var participant = await db.Participants.FromSql($"SELECT * FROM participants WHERE id = {participantId} FOR UPDATE").SingleAsync();
        return (examiner, invitation, participant);
    }
}
