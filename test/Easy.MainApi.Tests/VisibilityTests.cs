using Easy.Data;
using Easy.MainApi.Grading;

namespace Easy.MainApi.Tests;

// Specs 13 and 15 (issues 45, 47), ADR 0005: who may read an examiner grading's points and remarks.
public class VisibilityTests
{
    static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), Other = Guid.NewGuid();

    [Theory]
    [InlineData("A", false, DocumentationStatus.InProgress, false, true)] // own
    [InlineData("B", false, DocumentationStatus.InProgress, false, false)] // blinded grader
    [InlineData("B", true, DocumentationStatus.InProgress, false, false)] // admin who grades is blinded too
    [InlineData("B", false, DocumentationStatus.Graded, false, true)] // graded unblinds the graders
    [InlineData("B", false, DocumentationStatus.InProgress, true, true)] // B opened the summary view (spec 15)
    [InlineData("Other", true, DocumentationStatus.InProgress, false, true)] // admin not grading
    [InlineData("Other", false, DocumentationStatus.Graded, false, false)] // other examiners: status only
    [InlineData("Other", false, DocumentationStatus.InProgress, true, false)] // a summary view unblinds graders only
    [InlineData(null, false, DocumentationStatus.Graded, false, false)] // no examiner mirror
    public void MayRead_A_grading(string? viewer, bool admin, DocumentationStatus status, bool summaryView, bool expected)
    {
        Guid? id = viewer switch { "A" => A, "B" => B, "Other" => Other, _ => null };

        Assert.Equal(expected, ExaminerGradingEndpoints.MayRead(id, admin, A, [A, B], status, summaryView));
    }
}
