extern alias gateway;
extern alias mainapi;
extern alias examroom;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Easy.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Easy.IntegrationTests;

// Spec 8 (issue 23): an examiner joins an oral session through the ExamRoom hub (S07, S09). Real SignalR client over Kestrel.
[Collection(nameof(Infra))]
public class ExamRoomTests(KeycloakFixture keycloak, Infra infra) : IAsyncLifetime
{
    readonly List<IAsyncDisposable> disposables = [];
    readonly string database = infra.NewDatabase();
    WebApplicationFactory<mainapi::Program> mainApi = null!;
    readonly Guid participant = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        mainApi = Track(Keycloak(infra.Configure(new WebApplicationFactory<mainapi::Program>(), database)));
        using var scope = mainApi.Services.CreateScope(); // starts MainApi, which migrates the shared database
        var db = scope.ServiceProvider.GetRequiredService<EasyDb>();
        var cycle = db.Cycles.Add(new() { Type = CycleType.Winter, Year = 2026 }).Entity;
        db.Participants.Add(new() { Id = participant, CycleId = cycle.Id, DisplayNo = "WS2026-001", CsvId = [], Name = [], ProjectTitle = [] });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var d in Enumerable.Reverse(disposables)) await d.DisposeAsync();
    }

    [Fact]
    public async Task Examiner_can_join_open_session_and_others_are_notified()
    {
        string examRoom = StartExamRoom();
        Guid session = await CreateSession(await keycloak.LoginAsync("examiner"));
        var first = await Connect(examRoom, await keycloak.LoginAsync("examiner"));
        var joined = new TaskCompletionSource<string>();
        first.On<string>("ExaminerJoined", name => { if (name == "Dev Admin") joined.TrySetResult(name); });
        await first.InvokeAsync("JoinSession", session);

        var second = await Connect(examRoom, await keycloak.LoginAsync("admin"));
        await second.InvokeAsync("JoinSession", session);

        Assert.Equal("Dev Admin", await joined.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Join_unknown_session_is_rejected()
    {
        var connection = await Connect(StartExamRoom(), await keycloak.LoginAsync("examiner"));

        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("JoinSession", Guid.NewGuid()));
    }

    [Fact]
    public async Task Join_closed_session_is_rejected()
    {
        string examRoom = StartExamRoom();
        Guid session = await CreateSession(await keycloak.LoginAsync("examiner"));
        using (var scope = mainApi.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<EasyDb>().OralSessions.Where(s => s.Id == session)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, OralSessionStatus.Closed));
        var connection = await Connect(examRoom, await keycloak.LoginAsync("examiner"));

        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("JoinSession", session));
    }

    [Fact]
    public async Task Second_session_for_participant_is_rejected()
    {
        string token = await keycloak.LoginAsync("examiner");
        await CreateSession(token);

        Assert.Equal(HttpStatusCode.Conflict, (await PostSession(token)).StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_connection_is_rejected()
    {
        string examRoom = StartExamRoom();

        var none = await Assert.ThrowsAsync<HttpRequestException>(() => Connect(examRoom, token: null));
        var service = await Assert.ThrowsAsync<HttpRequestException>(async () => await Connect(examRoom, await keycloak.ServiceAccountTokenAsync()));

        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, service.StatusCode); // valid token, no examiner role
    }

    [Fact]
    public async Task Connection_closes_when_token_expires()
    {
        // The one-second token still authenticates within the 30 s clock skew; the hub closes at its expiry, not the skew's.
        var connection = await Connect(StartExamRoom(), await keycloak.OneSecondTokenAsync());
        var closed = new TaskCompletionSource();
        connection.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };

        await closed.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Fact, Trait("Category", "Slow")] // ~105 s by design; everyday runs skip it (see CLAUDE.md)
    public async Task Hub_connection_survives_idle_beyond_100s_through_gateway()
    {
        // Keep-alives off on both sides, so nothing crosses the gateway while idle: only its activity timeout decides.
        string examRoom = StartExamRoom(services => services.Configure<HubOptions>(o =>
        {
            o.KeepAliveInterval = TimeSpan.FromMinutes(5);
            o.ClientTimeoutInterval = TimeSpan.FromMinutes(10);
        }));
        var gateway = Track(new WebApplicationFactory<gateway::Program>().WithWebHostBuilder(b => b
            .UseSetting("urls", "http://127.0.0.1:0")
            .UseSetting("ReverseProxy:Clusters:exam-room:Destinations:exam-room:Address", examRoom)));
        gateway.UseKestrel();
        gateway.StartServer();
        Guid session = await CreateSession(await keycloak.LoginAsync("examiner"));
        var connection = await Connect(gateway.ClientOptions.BaseAddress.ToString(), await keycloak.LoginAsync("examiner"), c =>
        {
            c.KeepAliveInterval = TimeSpan.FromMinutes(5);
            c.ServerTimeout = TimeSpan.FromMinutes(10);
        });
        Exception? closedBy = null;
        connection.Closed += e => { closedBy = e; return Task.CompletedTask; };

        await Task.Delay(TimeSpan.FromSeconds(105));

        Assert.True(connection.State == HubConnectionState.Connected, $"closed: {closedBy?.Message}");
        await connection.InvokeAsync("JoinSession", session);
    }

    // ExamRoom on Kestrel (real WebSockets), on MainApi's database. Returns its base address.
    string StartExamRoom(Action<IServiceCollection>? services = null)
    {
        var examRoom = Track(Keycloak(infra.Configure(new WebApplicationFactory<examroom::Program>(), database)).WithWebHostBuilder(b => b
            .UseSetting("urls", "http://127.0.0.1:0")
            .ConfigureTestServices(services ?? (_ => { }))));
        examRoom.UseKestrel();
        examRoom.StartServer();
        return examRoom.ClientOptions.BaseAddress.ToString();
    }

    async Task<Guid> CreateSession(string token)
    {
        var response = await PostSession(token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    Task<HttpResponseMessage> PostSession(string token) =>
        mainApi.CreateClient().SendAsync(new(HttpMethod.Post, "/api/oral-sessions")
        {
            Content = JsonContent.Create(new { participantId = participant }),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        });

    async Task<HubConnection> Connect(string baseAddress, string? token, Action<HubConnection>? configure = null)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(baseAddress), "hubs/exam-room"), o =>
            {
                o.AccessTokenProvider = () => Task.FromResult(token);
                o.WebSocketConfiguration = ws => ws.KeepAliveInterval = TimeSpan.Zero; // like a browser: no WebSocket pings of its own
            })
            .Build();
        Track(connection);
        configure?.Invoke(connection); // before start: the first server timeout is armed on start
        await connection.StartAsync();
        return connection;
    }

    WebApplicationFactory<T> Keycloak<T>(WebApplicationFactory<T> factory) where T : class => factory.WithWebHostBuilder(b => b
        .UseSetting("Keycloak:Authority", keycloak.Issuer)
        .UseSetting("Keycloak:RequireHttpsMetadata", "false"));

    T Track<T>(T disposable) where T : IAsyncDisposable
    {
        disposables.Add(disposable);
        return disposable;
    }
}
