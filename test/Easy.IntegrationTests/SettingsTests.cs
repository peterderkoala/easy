extern alias mainapi;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Easy.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Easy.IntegrationTests;

// Spec 10 (issue 42): system-wide settings in ConfigEntry and per-cycle weights (S06, issue 39).
[Collection(nameof(Infra))]
public class SettingsTests(KeycloakFixture keycloak, Infra infra) : IAsyncLifetime
{
    WebApplicationFactory<mainapi::Program> mainApi = null!;
    string admin = null!;

    public async Task InitializeAsync()
    {
        mainApi = infra.Configure(new WebApplicationFactory<mainapi::Program>()).WithWebHostBuilder(b => b
            .UseSetting("Keycloak:Authority", keycloak.Issuer)
            .UseSetting("Keycloak:RequireHttpsMetadata", "false"));
        admin = await keycloak.LoginAsync("admin");
    }

    public async Task DisposeAsync() => await mainApi.DisposeAsync();

    [Fact]
    public async Task Settings_are_seeded_by_migration()
    {
        var response = await Send(HttpMethod.Get, "/api/settings", admin);

        response.EnsureSuccessStatusCode();
        var settings = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, settings.GetProperty("deviationSectionPoints").GetInt32());
        Assert.Equal(10, settings.GetProperty("deviationTotalPoints").GetInt32());
        Assert.Equal([15, 10, 40, 25, 10], settings.GetProperty("defaultWeights").EnumerateArray().Select(w => w.GetInt32()));
    }

    [Fact]
    public async Task Admin_updates_settings_and_change_is_audited()
    {
        var update = new { deviationSectionPoints = 4, deviationTotalPoints = 12, defaultWeights = new[] { 15, 10, 40, 20, 15 } };

        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Put, "/api/settings", admin, update)).StatusCode);

        var settings = await (await Send(HttpMethod.Get, "/api/settings", admin)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(12, settings.GetProperty("deviationTotalPoints").GetInt32());
        Assert.Equal([15, 10, 40, 20, 15], settings.GetProperty("defaultWeights").EnumerateArray().Select(w => w.GetInt32()));
        var entries = await Db(db => db.AuditEntries.OrderBy(e => e.Id).ToListAsync());
        Assert.Equal([("10", "12"), ("15/10/40/25/10", "15/10/40/20/15")], entries.Select(e => (e.Old, e.New))); // unchanged section points: none
        Assert.All(entries, e => Assert.Equal(("ConfigEntry", "Value", (Guid?)null), (e.Entity, e.Field, e.ParticipantId)));
    }

    [Fact]
    public async Task Examiner_cannot_update_settings()
    {
        var update = new { deviationSectionPoints = 1, deviationTotalPoints = 1, defaultWeights = new[] { 20, 20, 20, 20, 20 } };
        string examiner = await keycloak.LoginAsync("examiner");

        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Put, "/api/settings", null, update)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Put, "/api/settings", examiner, update)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/settings", examiner)).StatusCode);
        Assert.Equal("4", await Db(db => db.ConfigEntries.Where(c => c.Key == ConfigEntry.DeviationSectionPoints).Select(c => c.Value).SingleAsync()));
    }

    [Fact]
    public async Task Weights_not_adding_up_to_100_are_rejected()
    {
        var cycle = await NewCycle();
        int[][] invalid = [[15, 10, 40, 25, 11], [15, 10, 40, 35], [15, 10, 40, 25, 10, 0], [-10, 35, 40, 25, 10]];

        foreach (var weights in invalid)
        {
            var settings = new { deviationSectionPoints = 4, deviationTotalPoints = 10, defaultWeights = weights };
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put, "/api/settings", admin, settings)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put, $"/api/cycles/{cycle}/weights", admin, new { weights })).StatusCode);
        }
        var negative = new { deviationSectionPoints = -1, deviationTotalPoints = 10, defaultWeights = new[] { 15, 10, 40, 25, 10 } };
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put, "/api/settings", admin, negative)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put, "/api/settings", admin, new { deviationSectionPoints = 4, deviationTotalPoints = 10 })).StatusCode);
        int[] valid = [15, 10, 40, 25, 10];
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put, "/api/settings", admin, new { defaultWeights = valid })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put, "/api/settings", admin, new { deviationSectionPoints = 11, deviationTotalPoints = 10, defaultWeights = valid })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put, "/api/settings", admin, new { deviationSectionPoints = 4, deviationTotalPoints = 101, defaultWeights = valid })).StatusCode);
        Assert.Empty(await Db(db => db.AuditEntries.ToListAsync()));
    }

    [Fact]
    public async Task Admin_updates_cycle_weights()
    {
        var cycle = await NewCycle();

        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Put, $"/api/cycles/{cycle}/weights", admin, new { weights = new[] { 15, 10, 40, 20, 15 } })).StatusCode);

        var weights = await Db(async db => (await db.Cycles.SingleAsync()).Weights);
        Assert.Equal([15, 10, 40, 20, 15], weights);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Put, $"/api/cycles/{Guid.NewGuid()}/weights", admin, new { weights = new[] { 20, 20, 20, 20, 20 } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Put, $"/api/cycles/{cycle}/weights", await keycloak.LoginAsync("examiner"), new { weights = new[] { 20, 20, 20, 20, 20 } })).StatusCode);
    }

    Task<Guid> NewCycle() => Db(async db =>
    {
        var cycle = db.Cycles.Add(new() { Type = CycleType.Winter, Year = 2026, Weights = [15, 10, 40, 25, 10] }).Entity;
        await db.SaveChangesAsync();
        return cycle.Id;
    });

    Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return mainApi.CreateClient().SendAsync(request);
    }

    async Task<T> Db<T>(Func<EasyDb, Task<T>> query)
    {
        using var scope = mainApi.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<EasyDb>());
    }
}
