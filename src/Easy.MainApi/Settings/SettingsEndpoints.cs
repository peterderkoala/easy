using System.Globalization;
using Easy.Contracts;
using Easy.Data;
using Microsoft.EntityFrameworkCore;

namespace Easy.MainApi.Settings;

// S06 (issue 39): admin-only system-wide settings (ConfigEntry) and per-cycle weights. The audit interceptor logs
// each changed ConfigEntry value (S11); cycle weights are not audited.
public static class SettingsEndpoints
{
    public record Body(int? DeviationSectionPoints, int? DeviationTotalPoints, int[]? DefaultWeights);
    public record WeightsBody(int[]? Weights);

    static int[] ParseWeights(string value) => value.Split('/').Select(w => int.Parse(w, CultureInfo.InvariantCulture)).ToArray();

    public static async Task<int[]> DefaultWeights(EasyDb db) =>
        ParseWeights((await db.ConfigEntries.SingleAsync(c => c.Key == ConfigEntry.DefaultWeights)).Value);

    public static void MapSettings(this WebApplication app)
    {
        var admin = app.MapGroup("/api").RequireAuthorization(p => p.RequireRole("admin"));

        admin.MapGet("/settings", async (EasyDb db) =>
        {
            var values = await db.ConfigEntries.ToDictionaryAsync(c => c.Key, c => c.Value);
            return new Body(int.Parse(values[ConfigEntry.DeviationSectionPoints], CultureInfo.InvariantCulture),
                int.Parse(values[ConfigEntry.DeviationTotalPoints], CultureInfo.InvariantCulture), ParseWeights(values[ConfigEntry.DefaultWeights]));
        });

        admin.MapPut("/settings", async (Body body, EasyDb db) =>
        {
            // A section has 10 points, the total 100; a missing threshold must not bind as 0.
            if (body.DeviationSectionPoints is not (>= 0 and <= 10) || body.DeviationTotalPoints is not (>= 0 and <= 100))
                return Results.Problem("Thresholds must be 0 to 10 (section) and 0 to 100 (total).", statusCode: 400);
            if (!GradingMatrix.ValidWeights(body.DefaultWeights))
                return Results.Problem("Weights must be five non-negative integers adding up to 100.", statusCode: 400);

            // Unchanged values are not modified, so they get no audit entry.
            var entries = await db.ConfigEntries.ToDictionaryAsync(c => c.Key);
            entries[ConfigEntry.DeviationSectionPoints].Value = body.DeviationSectionPoints.Value.ToString(CultureInfo.InvariantCulture);
            entries[ConfigEntry.DeviationTotalPoints].Value = body.DeviationTotalPoints.Value.ToString(CultureInfo.InvariantCulture);
            entries[ConfigEntry.DefaultWeights].Value = string.Join('/', body.DefaultWeights!);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ponytail: no lock yet; the lock after the first submitted examiner grading comes with spec 13
        admin.MapPut("/cycles/{id:guid}/weights", async (Guid id, WeightsBody body, EasyDb db) =>
        {
            if (!GradingMatrix.ValidWeights(body.Weights))
                return Results.Problem("Weights must be five non-negative integers adding up to 100.", statusCode: 400);
            if (await db.Cycles.FindAsync(id) is not { } cycle)
                return Results.NotFound();
            cycle.Weights = body.Weights!;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
