using System.Security.Claims;
using Easy.Contracts;
using Easy.Data;
using Easy.MainApi.Participants;
using Easy.MainApi.Registrations;
using Easy.Platform;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Rebus.Bus;

namespace Easy.MainApi.Grading;

// S06 (issues 37, 38, 40): a registered examiner saves drafts of their examiner grading, submits and withdraws it.
// Every read of points and remarks goes through MayRead (ADR 0005); hub events carry the display name and the change only.
public static class ExaminerGradingEndpoints
{
    public const int MinRemarkLength = 10;
    const string NotGrader = "Only active registered examiners grade.", GradedFinal = "The documentation is graded; its grading is final.",
        Stale = "The grading changed meanwhile; reload it.";

    // The whole draft: criteria missing here are cleared. Version is the one last read; null for a first save.
    public record Body(uint? Version, Dictionary<string, int>? Points, Dictionary<string, string?>? Remarks, int?[]? Pages);

    static readonly Dictionary<string, GradingMatrix.Criterion> Criteria = GradingMatrix.Sections.SelectMany(s => s.Criteria).ToDictionary(c => c.Key);

    public static void MapExaminerGradings(this WebApplication app)
    {
        var grading = app.MapGroup("/api/participants/{participantId:guid}/grading").RequireAuthorization(p => p.RequireRole("examiner")); // admins hold it too

        grading.MapGet("", async (Guid participantId, ClaimsPrincipal user, EasyDb db, ParticipantKeys keys) =>
        {
            if (await db.Participants.FindAsync(participantId) is not { } participant)
                return Results.NotFound();
            Guid? viewer = (await RegistrationEndpoints.Caller(db, user))?.Id;
            var registered = await (from r in db.Registrations
                                    join e in db.Examiners on r.ExaminerId equals e.Id
                                    where r.ParticipantId == participantId
                                    orderby r.Slot
                                    select new { r.ExaminerId, e.DisplayName }).ToListAsync();
            var gradings = await db.ExaminerGradings.Include(g => g.Criteria).Where(g => g.ParticipantId == participantId).ToDictionaryAsync(g => g.ExaminerId);
            var graders = registered.Select(r => r.ExaminerId).ToList();
            bool summaryView = await db.SummaryViews.AnyAsync(v => v.ParticipantId == participantId && v.ExaminerId == viewer);
            using var key = participant.KeyForgotten ? null : keys.Open(participant);
            // Deviation notice (issue 39): graders see the section names and/or "total", never gaps or direction; an
            // admin not grading sees the gaps too, since MayRead gives them both gradings; everyone else only that there is one.
            var deviation = await db.Deviations.FindAsync(participantId);
            bool grader = viewer is { } id && graders.Contains(id), adminView = !grader && user.IsInRole("admin");
            var pair = registered.Select(r => gradings.GetValueOrDefault(r.ExaminerId)).OfType<ExaminerGrading>().ToList();
            bool gaps = !grader && pair.Count == 2 && pair.All(g => MayRead(viewer, user.IsInRole("admin"), g.ExaminerId, graders, participant.DocumentationStatus, summaryView));

            return Results.Ok(new
            {
                Status = participant.DocumentationStatus?.ToString(),
                Deviation = deviation is null ? null : new
                {
                    Sections = grader || adminView ? deviation.Sections : null,
                    Gaps = gaps ? DocumentationScore.Gaps(pair[0], pair[1], await DocumentationScore.Weights(db, participant)) : null,
                    Thresholds = adminView ? new { Section = deviation.SectionThreshold, Total = deviation.TotalThreshold } : null,
                    AcceptedBy = grader || adminView ? registered.Where(r => deviation.AcceptedBy.Contains(r.ExaminerId)).Select(r => r.DisplayName).ToList() : null,
                },
                Gradings = registered.Select(r =>
                {
                    var g = gradings.GetValueOrDefault(r.ExaminerId);
                    bool mine = r.ExaminerId == viewer, readable = g is not null && MayRead(viewer, user.IsInRole("admin"), r.ExaminerId, graders, participant.DocumentationStatus, summaryView);
                    return new
                    {
                        Examiner = r.DisplayName,
                        Mine = mine,
                        Status = g?.Status.ToString(),
                        Version = mine ? g?.Version : null,
                        g?.Pages, // not blinded (issue 41)
                        Points = readable ? DocumentationScore.Points(g!) : null,
                        Remarks = readable && key is not null ? g!.Criteria.Where(c => c.Remark is not null).ToDictionary(c => c.CriterionKey, c => key.Decrypt(c.Remark!)) : null,
                    };
                }).ToList(), // decrypts now: the key is zeroed when this returns
            });
        });

        grading.MapPut("/mine", async (Guid participantId, Body body, ClaimsPrincipal user, EasyDb db, ParticipantKeys keys,
            IHubContext<GradingHub, IGradingClient> hub, HttpContext context) =>
        {
            if (Invalid(body) is { } error)
                return Results.Problem(error, statusCode: 400);
            if (await Grader(db, user, participantId) is not { } examiner)
                return Results.Problem(NotGrader, statusCode: 403);
            var participant = (await db.Participants.FindAsync(participantId))!; // registered, so it exists
            if (participant.KeyForgotten)
                return Results.Problem("Unknown participant.", statusCode: 404);
            if (participant.DocumentationStatus is DocumentationStatus.Graded)
                return Results.Problem(GradedFinal, statusCode: 409);

            var grading = await Mine(db, participantId, examiner).Include(g => g.Criteria).SingleOrDefaultAsync();
            if (grading?.Version != body.Version) // also: no row but a version, or a row but none
                return Results.Problem(Stale, statusCode: 409);
            if (grading?.Status == ExaminerGradingStatus.Submitted)
                return Results.Problem("Withdraw the submit first.", statusCode: 409);
            grading ??= db.ExaminerGradings.Add(new() { ParticipantId = participantId, ExaminerId = examiner.Id }).Entity;

            using (var key = keys.Open(participant))
                foreach (var criterion in Criteria.Keys)
                {
                    int? points = body.Points?.TryGetValue(criterion, out int p) == true ? p : null;
                    string? remark = body.Remarks?.GetValueOrDefault(criterion) is { Length: > 0 } r ? r : null;
                    var row = grading.Criteria.SingleOrDefault(c => c.CriterionKey == criterion);
                    if (row is null && points is null && remark is null)
                        continue;
                    if (row is null)
                        grading.Criteria.Add(row = new() { CriterionKey = criterion });
                    row.Points = points;
                    // Re-encrypting an unchanged remark would give new ciphertext and a needless audit entry.
                    if (remark != (row.Remark is null ? null : key.Decrypt(row.Remark)))
                        row.Remark = remark is null ? null : key.Encrypt(remark);
                }
            grading.Pages = body.Pages ?? new int?[GradingMatrix.Sections.Count];
            grading.UpdatedAt = DateTimeOffset.UtcNow; // changes xmin even if only criteria changed
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException e) when (e is DbUpdateConcurrencyException || e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return Results.Problem(Stale, statusCode: 409); // UseOutbox rolls back
            }
            Notify(context, hub, participantId, examiner, ExaminerGradingEvent.Draft); // every save: an open summary view refetches (S06)
            return Results.Ok(new { grading.Version });
        });

        grading.MapPost("/mine/submit", async (Guid participantId, ClaimsPrincipal user, EasyDb db, ParticipantKeys keys, IBus bus,
            IHubContext<GradingHub, IGradingClient> hub, HttpContext context) =>
        {
            if (await Grader(db, user, participantId) is not { } examiner)
                return Results.Problem(NotGrader, statusCode: 403);
            // Locks until UseOutbox commits. Shared cycle lock: a concurrent weights change waits and then sees this
            // submit (S06, issue 37). Participant row lock: a concurrent upload waits and then sees in progress, or this
            // submit waits for the upload; a concurrent second submit, withdraw or accept waits, so the check sees both.
            await db.Database.ExecuteSqlAsync($"SELECT 1 FROM cycles WHERE id = (SELECT cycle_id FROM participants WHERE id = {participantId}) FOR SHARE");
            var participant = await db.Participants.FromSql($"SELECT * FROM participants WHERE id = {participantId} FOR UPDATE").SingleAsync();
            if (participant.DocumentationStatus is DocumentationStatus.Graded)
                return Results.Problem(GradedFinal, statusCode: 409);
            var grading = await Mine(db, participantId, examiner).Include(g => g.Criteria).SingleOrDefaultAsync();
            if (grading?.Status == ExaminerGradingStatus.Submitted)
                return Results.NoContent();
            if (participant.KeyForgotten)
                return Results.Problem("Unknown participant.", statusCode: 404);
            if (participant.DocumentationStatus is null)
                return Results.Problem("No documentation uploaded yet.", statusCode: 409);
            using (var key = keys.Open(participant))
                if (grading is null || !Complete(grading, key))
                    return Results.Problem($"Every criterion needs points and a remark of at least {MinRemarkLength} characters.", statusCode: 400);

            grading.Status = ExaminerGradingStatus.Submitted;
            grading.UpdatedAt = DateTimeOffset.UtcNow;
            if (participant.DocumentationStatus is DocumentationStatus.Submitted)
                participant.DocumentationStatus = DocumentationStatus.InProgress; // the first submit: grading has begun (issue 40)
            await db.SaveChangesAsync(); // first: the check queries submitted gradings
            Notify(context, hub, participantId, examiner, ExaminerGradingEvent.Submitted);
            await DocumentationScore.Check(db, bus, hub, context, participant, examiner);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Unblinds the opener for good, drafts included (S06, issue 38). Opening twice changes nothing.
        app.MapPost("/api/participants/{participantId:guid}/summary-view", async (Guid participantId, ClaimsPrincipal user, EasyDb db,
            IHubContext<GradingHub, IGradingClient> hub, HttpContext context) =>
        {
            if (await Grader(db, user, participantId) is not { } examiner)
                return Results.Problem(NotGrader, statusCode: 403);
            if (await db.SummaryViews.AnyAsync(v => v.ParticipantId == participantId && v.ExaminerId == examiner.Id))
                return Results.NoContent();
            // Shared participant lock until UseOutbox commits: the graded transition (spec 16) waits and then sees this.
            if (await db.Participants.FromSql($"SELECT * FROM participants WHERE id = {participantId} FOR SHARE").SingleAsync() is not { KeyForgotten: false } participant)
                return Results.Problem("Unknown participant.", statusCode: 404);
            if (participant.DocumentationStatus is DocumentationStatus.Graded)
                return Results.Problem("The documentation is graded; every grading is visible.", statusCode: 409);
            db.SummaryViews.Add(new() { ParticipantId = participantId, ExaminerId = examiner.Id, OpenedAt = DateTimeOffset.UtcNow });
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return Results.NoContent(); // a parallel open of the same examiner won; EF rolled back to its savepoint, so no second audit row
            }
            context.AfterCommit(() => hub.Clients.Group(GradingHub.Group(participantId)).SummaryViewOpened(participantId, examiner.DisplayName));
            return Results.NoContent();
        }).RequireAuthorization(p => p.RequireRole("examiner")); // admins hold it too

        // Either registered examiner accepts the deviation; when both have, graded with the average of two (issue 40).
        grading.MapPost("/deviation/accept", async (Guid participantId, ClaimsPrincipal user, EasyDb db, IBus bus,
            IHubContext<GradingHub, IGradingClient> hub, HttpContext context) =>
        {
            if (await Grader(db, user, participantId) is not { } examiner)
                return Results.Problem(NotGrader, statusCode: 403);
            // Participant row lock until UseOutbox commits: serializes with submit and withdraw.
            var participant = await db.Participants.FromSql($"SELECT * FROM participants WHERE id = {participantId} FOR UPDATE").SingleAsync();
            if (participant.DocumentationStatus is DocumentationStatus.Graded)
                return Results.Problem(GradedFinal, statusCode: 409);
            if (await db.Deviations.FindAsync(participantId) is not { } deviation)
                return Results.Problem("There is no deviation to accept.", statusCode: 409);
            if (deviation.AcceptedBy.Contains(examiner.Id))
                return Results.NoContent();

            deviation.AcceptedBy = [.. deviation.AcceptedBy, examiner.Id];
            context.AfterCommit(() => hub.Clients.Group(GradingHub.Group(participantId)).DeviationAccepted(participantId, examiner.DisplayName));
            if (deviation.AcceptedBy.Count == 2) // both registered examiners
                await DocumentationScore.Grade(db, bus, hub, context, participant, examiner, await DocumentationScore.Submitted(db, participantId));
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // The documentation stays in progress. Graded is final.
        grading.MapPost("/mine/withdraw", async (Guid participantId, ClaimsPrincipal user, EasyDb db,
            IHubContext<GradingHub, IGradingClient> hub, HttpContext context) =>
        {
            if (await Grader(db, user, participantId) is not { } examiner)
                return Results.Problem(NotGrader, statusCode: 403);
            // Participant row lock until UseOutbox commits: serializes with a submit's "both submitted" check and with accept.
            var participant = await db.Participants.FromSql($"SELECT * FROM participants WHERE id = {participantId} FOR UPDATE").SingleAsync();
            if (participant.DocumentationStatus is DocumentationStatus.Graded)
                return Results.Problem(GradedFinal, statusCode: 409);
            var grading = await Mine(db, participantId, examiner).SingleOrDefaultAsync();
            if (grading?.Status != ExaminerGradingStatus.Submitted)
                return Results.NoContent();
            if (await db.Deviations.FindAsync(participantId) is { } deviation)
                db.Deviations.Remove(deviation); // acceptances go with it; the next "both submitted" checks again (issue 39)
            grading.Status = ExaminerGradingStatus.Draft;
            grading.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            Notify(context, hub, participantId, examiner, ExaminerGradingEvent.Withdrawn);
            return Results.NoContent();
        });
    }

    // ADR 0005: the one visibility function. Own grading always; a grader sees the others once graded or after opening
    // the summary view; an admin not grading sees all, read-only; everyone else the status only.
    public static bool MayRead(Guid? viewer, bool admin, Guid author, IReadOnlyCollection<Guid> graders, DocumentationStatus? status, bool summaryView) =>
        author == viewer || (viewer is { } v && graders.Contains(v) ? status == DocumentationStatus.Graded || summaryView : admin);

    static string? Invalid(Body body)
    {
        string[] keys = [.. body.Points?.Keys ?? Enumerable.Empty<string>(), .. body.Remarks?.Keys ?? Enumerable.Empty<string>()];
        if (keys.FirstOrDefault(k => !Criteria.ContainsKey(k)) is { } unknown)
            return $"Unknown criterion {unknown}.";
        if (body.Points?.FirstOrDefault(p => p.Value < 0 || p.Value > Criteria[p.Key].Max) is { Key: not null } outOfRange)
            return $"Points for {outOfRange.Key} must be 0 to {Criteria[outOfRange.Key].Max}.";
        if (body.Pages is { } pages && (pages.Length != GradingMatrix.Sections.Count || pages.Any(p => p < 1)))
            return $"Pages: one page number (1 or more) or null per section, {GradingMatrix.Sections.Count} in all.";
        return null;
    }

    // Whitespace doesn't count towards the remark length.
    static bool Complete(ExaminerGrading grading, DataKey key) =>
        Criteria.Keys.All(k => grading.Criteria.SingleOrDefault(c => c.CriterionKey == k) is { Points: not null, Remark: { } remark }
            && key.Decrypt(remark).Trim().Length >= MinRemarkLength);

    static IQueryable<ExaminerGrading> Mine(EasyDb db, Guid participantId, Examiner examiner) =>
        db.ExaminerGradings.Where(g => g.ParticipantId == participantId && g.ExaminerId == examiner.Id);

    static void Notify(HttpContext context, IHubContext<GradingHub, IGradingClient> hub, Guid participantId, Examiner examiner, ExaminerGradingEvent change) =>
        context.AfterCommit(() => hub.Clients.Group(GradingHub.Group(participantId)).ExaminerGradingChanged(participantId, examiner.DisplayName, change));

    // An active examiner registered for this documentation. Shared locks until UseOutbox commits: a concurrent
    // deactivation (examiner row) or unregistration (registration row) waits and then sees this write.
    static async Task<Examiner?> Grader(EasyDb db, ClaimsPrincipal user, Guid participantId)
    {
        var examiner = await RegistrationEndpoints.LockedCaller(db, user);
        if (examiner is not { Active: true })
            return null;
        bool registered = await db.Registrations
            .FromSql($"SELECT * FROM registrations WHERE participant_id = {participantId} AND examiner_id = {examiner.Id} FOR SHARE").AnyAsync();
        return registered ? examiner : null;
    }
}
