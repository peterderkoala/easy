namespace Easy.Contracts;

// S06, ADR 0004: the IHK documentation matrix as a constant, shared with the client. Keys of a released version never change;
// a new IHK matrix is a new version next to this one.
public static class GradingMatrix
{
    public record Criterion(string Key, string Name, int Max);
    public record Section(string Key, string Name, IReadOnlyList<Criterion> Criteria); // Key: versioned like criteria, e.g. fiae-2026:3

    // Source: docs/samples/bewertungsmatrix/bewertungsmatrix-merged.md. Each section's criteria add up to 10.
    public static readonly IReadOnlyList<Section> Sections =
    [
        new("fiae-2026:1", "Ausgangssituation", [
            new("fiae-2026:1.1", "Projektziel, Teilaufgaben, Kundenwünsche", 5),
            new("fiae-2026:1.2", "Projektumfeld, Projektschnittstellen", 5)]),
        new("fiae-2026:2", "Ressourcen- und Ablaufplanung", [
            new("fiae-2026:2.1", "Ablaufplanung", 5),
            new("fiae-2026:2.2", "Personal-, Sachmittel-, Terminplanung und ggf. Kostenplanung", 5)]),
        new("fiae-2026:3", "Durchführung und Auftragsbearbeitung", [
            new("fiae-2026:3.1", "Prozessschritte, Vorgehensweise, Qualitätssicherung", 4),
            new("fiae-2026:3.2", "Abweichung, Anpassung, Entscheidung", 3),
            new("fiae-2026:3.3", "Komplexität der Aufgabenstellung", 3)]),
        new("fiae-2026:4", "Projektergebnisse", [
            new("fiae-2026:4.1", "Kommentierter Quellcode", 2),
            new("fiae-2026:4.2", "Soll-Ist-Vergleich", 2),
            new("fiae-2026:4.3", "Qualitätskontrolle", 2),
            new("fiae-2026:4.4", "Abweichungen, Anpassungen", 2),
            new("fiae-2026:4.5", "Benutzerdokumentation", 2)]),
        new("fiae-2026:5", "Gestaltung der Dokumentation", [
            new("fiae-2026:5.1", "Äußere Form", 5),
            new("fiae-2026:5.2", "Inhaltliche Form", 5)]),
    ];

    // Missing criteria (a draft) count 0.
    public static int SectionPoints(Section section, IReadOnlyDictionary<string, int> points) =>
        section.Criteria.Sum(c => points.GetValueOrDefault(c.Key));

    // Out of 100: section points (max 10) × weight (percent) / 10. Integer inputs, so exact to one decimal place.
    public static decimal Total(IReadOnlyDictionary<string, int> points, IReadOnlyList<int> weights) =>
        Sections.Select((s, i) => SectionPoints(s, points) * weights[i]).Sum() / 10m;

    // One weight per section in percent, adding up to 100 (S06, issue 37).
    public static bool ValidWeights(int[]? weights) =>
        weights?.Length == Sections.Count && weights.All(w => w >= 0) && weights.Sum() == 100;

    // S06 (issue 40): lower bounds; display only, the stored score is the total.
    public static int GermanGrade(decimal total) => total switch
    {
        >= 92 => 1,
        >= 81 => 2,
        >= 67 => 3,
        >= 50 => 4,
        >= 30 => 5,
        _ => 6,
    };
}
