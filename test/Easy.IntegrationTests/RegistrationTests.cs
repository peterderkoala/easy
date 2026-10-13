extern alias mainapi;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Easy.Contracts;
using Easy.Data;
using mainapi::Easy.MainApi.Participants;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Easy.IntegrationTests;

// Spec 12 (issue 44): examiners register themselves for a documentation, at most two (S05, issue 35).
[Collection(nameof(Infra))]
public class RegistrationTests(KeycloakFixture keycloak, Infra infra) : IAsyncLifetime
{
    WebApplicationFactory<mainapi::Program> mainApi = null!;
    string examiner = null!, admin = null!;
    Guid cycle, participant;

    public async Task InitializeAsync()
    {
        mainApi = infra.Configure(new WebApplicationFactory<mainapi::Program>()).WithWebHostBuilder(b => b
            .UseSetting("Keycloak:Authority", keycloak.Issuer)
            .UseSetting("Keycloak:RequireHttpsMetadata", "false"));
        var keys = mainApi.Services.GetRequiredService<ParticipantKeys>(); // starts MainApi: migrations run
        await Db(async db =>
        {
            cycle = db.Cycles.Add(new() { Type = CycleType.Winter, Year = 2026, Weights = [15, 10, 40, 25, 10] }).Entity.Id;
            using var key = keys.Create();
            participant = db.Participants.Add(new()
            {
                CycleId = cycle, DisplayNo = "WS2026-001", WrappedKey = key.Wrapped, KeyVersion = key.Version,
                CsvId = key.Encrypt("1"), Name = key.Encrypt("Erika Muster"), ProjectTitle = key.Encrypt("Shop"),
            }).Entity.Id;
            return await db.SaveChangesAsync();
        });
        examiner = await Login("examiner");
        admin = await Login("admin");
    }

    public async Task DisposeAsync() => await mainApi.DisposeAsync();

    [Fact]
    public async Task Examiner_registers_and_appears_in_documentation_list()
    {
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Post, Registration, examiner)).StatusCode);

        var row = Assert.Single(await Documentations(examiner));
        Assert.Equal("WS2026-001", row.GetProperty("displayNo").GetString());
        Assert.Equal("Erika Muster", row.GetProperty("name").GetString());
        Assert.Equal(["Dev Examiner"], row.GetProperty("examiners").EnumerateArray().Select(e => e.GetString()));
        Assert.True(row.GetProperty("registered").GetBoolean());
        Assert.False(Assert.Single(await Documentations(admin)).GetProperty("registered").GetBoolean());
    }

    // Spec 19 (issue 51): the client's way to a cycle's documentation list.
    [Fact]
    public async Task Examiner_lists_cycles_newest_first()
    {
        await Db(db => { db.Cycles.Add(new() { Type = CycleType.Summer, Year = 2026, Weights = [15, 10, 40, 25, 10] }); return db.SaveChangesAsync(); });

        var response = await Send(HttpMethod.Get, "/api/cycles", examiner);

        response.EnsureSuccessStatusCode();
        var cycles = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.Equal([("Winter", 2026), ("Summer", 2026)], cycles!.Select(c => (c.GetProperty("type").GetString(), c.GetProperty("year").GetInt32())));
        Assert.Equal(cycle, cycles![0].GetProperty("id").GetGuid());
        Assert.Equal([15, 10, 40, 25, 10], cycles[1].GetProperty("weights").EnumerateArray().Select(w => w.GetInt32())); // Spec 21: the settings page
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "/api/cycles", null)).StatusCode);
    }

    [Fact]
    public async Task Third_registration_is_rejected()
    {
        await Seed(slot: 1, "Erste");
        await Seed(slot: 2, "Zweite");

        Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Post, Registration, examiner)).StatusCode);

        Assert.Equal(["Erste", "Zweite"], Assert.Single(await Documentations(examiner)).GetProperty("examiners").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Concurrent_registrations_never_exceed_two()
    {
        await Seed(slot: 1, "Erste");

        var responses = await Task.WhenAll(Send(HttpMethod.Post, Registration, examiner), Send(HttpMethod.Post, Registration, admin));

        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
        Assert.Equal(2, await Db(db => db.Registrations.CountAsync()));
    }

    [Fact]
    public async Task Concurrent_registrations_on_free_places_all_succeed()
    {
        // Two examiners at once, and one of them twice (double click): both places taken, no 409.
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => Send(HttpMethod.Post, Registration, i % 2 == 0 ? examiner : admin)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        Assert.Equal(2, await Db(db => db.Registrations.CountAsync()));
    }

    [Fact]
    public async Task Examiner_unregisters_and_place_reopens()
    {
        await Seed(slot: 1, "Erste");
        await Send(HttpMethod.Post, Registration, examiner);

        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, Registration, examiner)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Post, Registration, admin)).StatusCode);

        Assert.Equal(["Erste", "Dev Admin"], Assert.Single(await Documentations(examiner)).GetProperty("examiners").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Inactive_examiner_cannot_register()
    {
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Post, $"/api/examiners/{await ExaminerId("examiner")}/deactivate", admin)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, Registration, examiner)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Post, $"/api/examiners/{await ExaminerId("examiner")}/reactivate", admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Post, Registration, examiner)).StatusCode);
    }

    [Fact]
    public async Task Deactivation_releases_registrations()
    {
        await Send(HttpMethod.Post, Registration, examiner);
        await Send(HttpMethod.Post, Registration, admin);

        await Send(HttpMethod.Post, $"/api/examiners/{await ExaminerId("examiner")}/deactivate", admin);

        Assert.Equal(["Dev Admin"], Assert.Single(await Documentations(admin)).GetProperty("examiners").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Only_admin_deactivates()
    {
        var path = $"/api/examiners/{await ExaminerId("admin")}/deactivate";

        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, path, examiner)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Post, $"/api/examiners/{Guid.NewGuid()}/deactivate", admin)).StatusCode);
        Assert.True(await Db(db => db.Examiners.Where(e => e.Subject != "").AllAsync(e => e.Active)));
    }

    [Fact]
    public async Task Registration_events_reach_the_documentation_group()
    {
        var events = new List<string>();
        var taken = new TaskCompletionSource<int>();
        var released = new TaskCompletionSource();
        await using var watcher = new HubConnectionBuilder()
            .WithUrl(new Uri(mainApi.Server.BaseAddress, "hubs/grading"), o =>
            {
                o.HttpMessageHandlerFactory = _ => mainApi.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling; // TestServer has no WebSockets
                o.AccessTokenProvider = () => Task.FromResult<string?>(admin);
            })
            .Build();
        // Notify then refetch (issue 38): the event comes after the commit, so a read on arrival sees the change.
        watcher.On<Guid, string>(nameof(IGradingClient.RegistrationTaken), async (_, name) =>
        {
            lock (events) events.Add($"taken {name}");
            taken.TrySetResult((await Documentations(admin))[0].GetProperty("examiners").GetArrayLength());
        });
        watcher.On<Guid, string>(nameof(IGradingClient.RegistrationReleased), (_, name) =>
        {
            lock (events) events.Add($"released {name}");
            if (name == "Dev Admin") released.TrySetResult();
        });
        await watcher.StartAsync();
        await watcher.InvokeAsync("JoinDocumentation", participant);

        await Send(HttpMethod.Post, Registration, examiner);
        Assert.Equal(1, await taken.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        await Send(HttpMethod.Delete, Registration, examiner);
        await Send(HttpMethod.Post, Registration, admin);
        await Send(HttpMethod.Post, $"/api/examiners/{await ExaminerId("admin")}/deactivate", admin);

        await released.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(["taken Dev Examiner", "released Dev Examiner", "taken Dev Admin", "released Dev Admin"], events);
    }

    Task<Guid> ExaminerId(string user) => Db(db => db.Examiners.Where(e => e.Email == $"{user}@easy.local").Select(e => e.Id).SingleAsync());

    // Another examiner, registered directly: Keycloak has only the two dev users.
    Task<int> Seed(int slot, string name) => Db(db =>
    {
        var other = db.Examiners.Add(new() { Subject = name, DisplayName = name, Email = $"{name}@easy.local" }).Entity;
        db.Registrations.Add(new() { ParticipantId = participant, ExaminerId = other.Id, Slot = slot, CreatedAt = DateTimeOffset.UtcNow });
        return db.SaveChangesAsync();
    });

    string Registration => $"/api/participants/{participant}/registration";

    async Task<List<JsonElement>> Documentations(string token)
    {
        var response = await Send(HttpMethod.Get, $"/api/cycles/{cycle}/documentations", token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
    }

    // Logs in and creates the examiner mirror, as the client does after login.
    async Task<string> Login(string user)
    {
        string token = await keycloak.LoginAsync(user);
        (await Send(HttpMethod.Post, "/api/me", token)).EnsureSuccessStatusCode();
        return token;
    }

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
