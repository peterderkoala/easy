using Easy.Contracts;

namespace Easy.MainApi.Tests;

// Spec 10 (issue 42), ADR 0004.
public class GradingMatrixTests
{
    [Fact]
    public void Matrix_has_five_sections_and_fourteen_criteria_with_versioned_keys()
    {
        Assert.Equal([2, 2, 3, 5, 2], GradingMatrix.Sections.Select(s => s.Criteria.Count));
        Assert.All(GradingMatrix.Sections, s => Assert.Equal(10, s.Criteria.Sum(c => c.Max)));
        Assert.Equal(["fiae-2026:3.1", "fiae-2026:3.2", "fiae-2026:3.3"], GradingMatrix.Sections[2].Criteria.Select(c => c.Key));
        Assert.Equal([4, 3, 3], GradingMatrix.Sections[2].Criteria.Select(c => c.Max));
        Assert.Equal(14, GradingMatrix.Sections.SelectMany(s => s.Criteria).Select(c => c.Key).Distinct().Count());
    }

    // Calibration examples from docs/samples/bewertungsmatrix: Teilnehmer Z and Prüfer A of Teilnehmer X.
    [Fact]
    public void Total_is_weighted_sum_of_section_points()
    {
        int[] weights = [15, 10, 40, 25, 10];
        Assert.Equal(85.5m, GradingMatrix.Total(Points(4, 5, 5, 5, 3, 3, 3, 2, 2, 1, 2, 1, 3, 3), weights));
        Assert.Equal(47.5m, GradingMatrix.Total(Points(3, 3, 1, 2, 2, 1, 1, 1, 1, 1, 2, 0, 5, 2), weights));
        Assert.Equal(9m, GradingMatrix.Total(Points(3, 3), weights)); // a draft: missing criteria count 0
        Assert.Equal(9, GradingMatrix.SectionPoints(GradingMatrix.Sections[0], Points(4, 5)));
    }

    // Attributes cannot hold decimals, hence double.
    [Theory]
    [InlineData(100, 1)]
    [InlineData(92, 1)]
    [InlineData(91.5, 2)]
    [InlineData(81, 2)]
    [InlineData(67, 3)]
    [InlineData(50, 4)]
    [InlineData(49.9, 5)]
    [InlineData(30, 5)]
    [InlineData(29.9, 6)]
    [InlineData(0, 6)]
    public void German_grade_uses_lower_bounds(double total, int grade) => Assert.Equal(grade, GradingMatrix.GermanGrade((decimal)total));

    // Points in matrix order, keyed by criterion.
    static Dictionary<string, int> Points(params int[] points) =>
        GradingMatrix.Sections.SelectMany(s => s.Criteria).Zip(points).ToDictionary(p => p.First.Key, p => p.Second);
}
