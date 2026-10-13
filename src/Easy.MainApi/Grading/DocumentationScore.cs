using Easy.Contracts;
using Easy.Data;
using Easy.MainApi.Settings;
using Easy.Platform;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Rebus.Bus;

namespace Easy.MainApi.Grading;

// S06 (issues 34, 39, 40): from two submitted examiner gradings to one documentation score. On "both registered
// submitted" the pair is either graded with ROUND((T1 + T2) / 2, 1) or held as a stored deviation, which the pair
// resolves by revising (withdraw) or by both accepting it (endpoints in ExaminerGradingEndpoints). Every transition runs
// under the participant row lock.
public static class DocumentationScore
{
    public const string Total = "total";

    // Call after the submit is saved, under the participant row lock: a concurrent second submit waits for it.
    public static async Task Check(EasyDb db, IBus bus, IHubContext<GradingHub, IGradingClient> hub, HttpContext context, Participant participant, Examiner by)
    {
        var gradings = await Submitted(db, participant.Id);
        // The accepted third examiner's submit: average of three, no further deviation check (S06).
        if (await db.ExaminerGradings.Include(g => g.Criteria).SingleOrDefaultAsync(g => g.ParticipantId == participant.Id && g.Status == ExaminerGradingStatus.Submitted
                && db.ThirdExaminerInvitations.Any(i => i.ParticipantId == participant.Id && i.ExaminerId == g.ExaminerId && i.Status == ThirdExaminerStatus.Accepted)) is { } third)
        {
            await Grade(db, bus, hub, context, participant, by, [.. gradings, third]);
            return;
        }
        if (gradings.Count < 2)
            return;
        var (sectionThreshold, totalThreshold) = await SettingsEndpoints.DeviationThresholds(db);
        string[] deviating = [.. Gaps(gradings[0], gradings[1], await Weights(db, participant))
            .Where(g => g.Value > (g.Key == Total ? totalThreshold : sectionThreshold)).Select(g => g.Key)]; // exactly at the threshold: none
        if (deviating.Length == 0)
        {
            await Grade(db, bus, hub, context, participant, by, gradings);
            return;
        }
        db.Deviations.Add(new() { ParticipantId = participant.Id, Sections = deviating, SectionThreshold = sectionThreshold, TotalThreshold = totalThreshold, FoundAt = DateTimeOffset.UtcNow });
        context.AfterCommit(() => hub.Clients.Group(GradingHub.Group(participant.Id)).DeviationFound(participant.Id));
    }

    // Graded is final (issue 40). The score goes to Grade (audited), GradeChanged through the outbox, same transaction.
    internal static async Task Grade(EasyDb db, IBus bus, IHubContext<GradingHub, IGradingClient> hub, HttpContext context, Participant participant, Examiner by,
        IReadOnlyList<ExaminerGrading> gradings)
    {
        var weights = await Weights(db, participant);
        participant.DocumentationStatus = DocumentationStatus.Graded;
        var grade = await db.Grades.FindAsync(participant.Id, GradeSource.Documentation)
            ?? db.Grades.Add(new() { ParticipantId = participant.Id, Source = GradeSource.Documentation }).Entity;
        grade.Value = Math.Round(gradings.Average(g => GradingMatrix.Total(Points(g), weights)), 1, MidpointRounding.AwayFromZero) + 0.0m; // scale 1 (SetGrade)
        grade.UpdatedBy = by.Id;
        grade.UpdatedAt = DateTimeOffset.UtcNow;
        await bus.Publish(new GradeChanged(participant.Id, GradeSource.Documentation.ToString(), Guid.NewGuid()));
        context.AfterCommit(() => hub.Clients.Group(GradingHub.Group(participant.Id)).Graded(participant.Id));
    }

    // Absolute gap per section (points 0 to 10) and "total" (out of 100).
    public static Dictionary<string, decimal> Gaps(ExaminerGrading a, ExaminerGrading b, IReadOnlyList<int> weights)
    {
        var (pa, pb) = (Points(a), Points(b));
        var gaps = GradingMatrix.Sections.ToDictionary(s => s.Key, s => (decimal)Math.Abs(GradingMatrix.SectionPoints(s, pa) - GradingMatrix.SectionPoints(s, pb)));
        gaps[Total] = Math.Abs(GradingMatrix.Total(pa, weights) - GradingMatrix.Total(pb, weights));
        return gaps;
    }

    // The registered examiners' submitted gradings.
    internal static Task<List<ExaminerGrading>> Submitted(EasyDb db, Guid participantId) =>
        db.ExaminerGradings.Include(g => g.Criteria)
            .Where(g => g.ParticipantId == participantId && g.Status == ExaminerGradingStatus.Submitted
                && db.Registrations.Any(r => r.ParticipantId == participantId && r.ExaminerId == g.ExaminerId))
            .OrderBy(g => g.ExaminerId).ToListAsync();

    internal static async Task<int[]> Weights(EasyDb db, Participant participant) => (await db.Cycles.FindAsync(participant.CycleId))!.Weights;

    internal static Dictionary<string, int> Points(ExaminerGrading g) =>
        g.Criteria.Where(c => c.Points is not null).ToDictionary(c => c.CriterionKey, c => c.Points!.Value);
}
