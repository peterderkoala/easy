extern alias gateway;
extern alias mainapi;

using System.Net;
using Easy.Contracts;
using Easy.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using GradingHub = mainapi::Easy.MainApi.Grading.GradingHub;

namespace Easy.IntegrationTests;

// Spec 11 (issue 43): examiners watching one documentation get typed notifications from the MainApi grading hub (S06, S09).
// Real SignalR client over WebSockets, through the gateway.
[Collection(nameof(Infra))]
public class GradingHubTests(KeycloakFixture keycloak, Infra infra) : IAsyncLifetime
{
    readonly List<IAsyncDisposable> disposables = [];
    WebApplicationFactory<mainapi::Program> mainApi = null!;
    string gatewayAddress = null!;
    readonly Guid participant = Guid.NewGuid();
    readonly Guid other = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        mainApi = StartOnKestrel(infra.Configure(new WebApplicationFactory<mainapi::Program>()).WithWebHostBuilder(b => b
            .UseSetting("Keycloak:Authority", keycloak.Issuer)
            .UseSetting("Keycloak:RequireHttpsMetadata", "false")));
        var gateway = StartOnKestrel(new WebApplicationFactory<gateway::Program>().WithWebHostBuilder(b => b
            .UseSetting("ReverseProxy:Clusters:main-api-hubs:Destinations:main-api:Address", mainApi.ClientOptions.BaseAddress.ToString())));
        gatewayAddress = gateway.ClientOptions.BaseAddress.ToString();
        using var scope = mainApi.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyDb>();
        var cycle = db.Cycles.Add(new() { Type = CycleType.Winter, Year = 2026 }).Entity;
        db.Participants.Add(new() { Id = participant, CycleId = cycle.Id, DisplayNo = "WS2026-001", CsvId = [], Name = [], ProjectTitle = [] });
        db.Participants.Add(new() { Id = other, CycleId = cycle.Id, DisplayNo = "WS2026-002", CsvId = [], Name = [], ProjectTitle = [] });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var d in Enumerable.Reverse(disposables)) await d.DisposeAsync();
    }

    [Fact]
    public async Task Examiner_joins_documentation_group_and_receives_its_messages()
    {
        var connection = await Connect(await keycloak.LoginAsync("examiner"));
        var received = new TaskCompletionSource<(Guid, string)>();
        connection.On<Guid, string>(nameof(IGradingClient.RegistrationTaken), (id, examiner) => received.TrySetResult((id, examiner)));
        await connection.InvokeAsync("JoinDocumentation", participant);

        await Notify(participant);

        Assert.Equal((participant, "Dev Examiner"), await received.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Message_for_one_documentation_does_not_reach_another_group()
    {
        var watcher = await Connect(await keycloak.LoginAsync("examiner"));
        var received = new List<Guid>();
        var ownArrived = new TaskCompletionSource();
        watcher.On<Guid, string>(nameof(IGradingClient.RegistrationTaken), (id, _) =>
        {
            lock (received) received.Add(id);
            if (id == other) ownArrived.TrySetResult();
        });
        await watcher.InvokeAsync("JoinDocumentation", other);
        await watcher.InvokeAsync("JoinDocumentation", participant);
        await watcher.InvokeAsync("LeaveDocumentation", participant);

        await Notify(participant);
        await Notify(other); // one connection delivers in order, so once this arrives the first would have too

        await ownArrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal([other], received);
    }

    [Fact]
    public async Task Join_unknown_participant_fails()
    {
        var connection = await Connect(await keycloak.LoginAsync("examiner"));

        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("JoinDocumentation", Guid.NewGuid()));
    }

    [Fact]
    public async Task Hub_requires_examiner_role()
    {
        var none = await Assert.ThrowsAsync<HttpRequestException>(() => Connect(token: null));
        var service = await Assert.ThrowsAsync<HttpRequestException>(async () => await Connect(await keycloak.ServiceAccountTokenAsync()));

        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, service.StatusCode); // valid token, no examiner role
    }

    // Sends as a later spec would after its commit.
    Task Notify(Guid participantId) =>
        mainApi.Services.GetRequiredService<IHubContext<GradingHub, IGradingClient>>()
            .Clients.Group(GradingHub.Group(participantId)).RegistrationTaken(participantId, "Dev Examiner");

    async Task<HubConnection> Connect(string? token)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(gatewayAddress), "hubs/grading"), o => o.AccessTokenProvider = () => Task.FromResult(token))
            .Build();
        Track(connection);
        await connection.StartAsync();
        return connection;
    }

    WebApplicationFactory<T> StartOnKestrel<T>(WebApplicationFactory<T> factory) where T : class
    {
        factory = Track(factory.WithWebHostBuilder(b => b.UseSetting("urls", "http://127.0.0.1:0")));
        factory.UseKestrel();
        factory.StartServer();
        return factory;
    }

    T Track<T>(T disposable) where T : IAsyncDisposable
    {
        disposables.Add(disposable);
        return disposable;
    }
}
