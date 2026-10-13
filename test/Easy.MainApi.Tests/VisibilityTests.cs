using Easy.Data;
using Easy.MainApi.Grading;

namespace Easy.MainApi.Tests;

// Spec 13 (issue 45), ADR 0005: who may read an examiner grading's points and remarks.
public class VisibilityTests
{
    static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), Other = Guid.NewGuid();

    [Theory]
    [InlineData("A", false, DocumentationStatus.InProgress, true)] // own
    [InlineData("B", false, DocumentationStatus.InProgress, false)] // blinded grader
    [InlineData("B", true, DocumentationStatus.InProgress, false)] // admin who grades is blinded too
    [InlineData("B", false, DocumentationStatus.Graded, true)] // graded unblinds the graders
    [InlineData("Other", true, DocumentationStatus.InProgress, true)] // admin not grading
    [InlineData("Other", false, DocumentationStatus.Graded, false)] // other examiners: status only
    [InlineData(null, false, DocumentationStatus.Graded, false)] // no examiner mirror
    public void MayRead_A_grading(string? viewer, bool admin, DocumentationStatus status, bool expected)
    {
        Guid? id = viewer switch { "A" => A, "B" => B, "Other" => Other, _ => null };

        Assert.Equal(expected, ExaminerGradingEndpoints.MayRead(id, admin, A, [A, B], status));
    }
}
