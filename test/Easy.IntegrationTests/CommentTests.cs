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

// Spec 14 (issue 46): any examiner discusses a documentation in one open comment thread, optionally pinned to a
// section (S06, issue 37). Not blinded, not audited; the text is encrypted under the participant key (S02).
[Collection(nameof(Infra))]
public class CommentTests(KeycloakFixture keycloak, Infra infra) : IAsyncLifetime
{
    WebApplicationFactory<mainapi::Program> mainApi = null!;
    string examiner = null!, admin = null!;
    Guid participant;

    const string Secret = "Abschnitt 3 wirkt kopiert";

    public async Task InitializeAsync()
    {
        mainApi = infra.Configure(new WebApplicationFactory<mainapi::Program>()).WithWebHostBuilder(b => b
            .UseSetting("Keycloak:Authority", keycloak.Issuer)
            .UseSetting("Keycloak:RequireHttpsMetadata", "false"));
        var keys = mainApi.Services.GetRequiredService<ParticipantKeys>(); // starts MainApi: migrations run
        using var scope = mainApi.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyDb>();
        var cycle = db.Cycles.Add(new() { Type = CycleType.Winter, Year = 2026 }).Entity;
        using var key = keys.Create();
        participant = db.Participants.Add(new()
        {
            CycleId = cycle.Id, DisplayNo = "WS2026-001", WrappedKey = key.Wrapped, KeyVersion = key.Version,
            CsvId = key.Encrypt("1"), Name = key.Encrypt("Erika Muster"), ProjectTitle = key.Encrypt("Shop"),
            DocumentationStatus = DocumentationStatus.Submitted,
        }).Entity.Id;
        await db.SaveChangesAsync();
        examiner = await Login("examiner");
        admin = await Login("admin");
    }

    public async Task DisposeAsync() => await mainApi.DisposeAsync();

    [Fact]
    public async Task Any_examiner_adds_comment_on_a_section()
    {
        // Neither is registered: commenting is open to every examiner.
        Assert.Equal(HttpStatusCode.Created, (await Add(examiner, "fiae-2026:3", Secret)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Add(admin, "fiae-2026:3", "Sehe ich auch so")).StatusCode);

        var comments = (await List(admin)).EnumerateArray().ToList();
        Assert.Equal([("Dev Examiner", "fiae-2026:3", Secret), ("Dev Admin", "fiae-2026:3", "Sehe ich auch so")],
            comments.Select(c => (c.GetProperty("author").GetString(), c.GetProperty("section").GetString(), c.GetProperty("text").GetString())));
    }

    [Fact]
    public async Task General_comment_has_no_section()
    {
        Assert.Equal(HttpStatusCode.Created, (await Add(examiner, null, "Insgesamt solide")).StatusCode);

        var comment = Assert.Single((await List(examiner)).EnumerateArray());
        Assert.Equal(JsonValueKind.Null, comment.GetProperty("section").ValueKind);
    }

    [Fact]
    public async Task Unknown_section_is_rejected()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Add(examiner, "fiae-2026:9", "Wo?")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Add(examiner, "fiae-2026:3.2", "Ein Kriterium, kein Abschnitt")).StatusCode);
        Assert.Empty(await Db(db => db.Comments.ToListAsync()));
    }

    [Fact]
    public async Task Comment_without_text_is_rejected()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Add(examiner, null, " ")).StatusCode);
        Assert.Empty(await Db(db => db.Comments.ToListAsync()));
    }

    [Fact]
    public async Task Deactivated_examiner_cannot_comment()
    {
        await Db(db => db.Examiners.Where(e => e.Email == "examiner@easy.local").ExecuteUpdateAsync(s => s.SetProperty(e => e.Active, false)));

        Assert.Equal(HttpStatusCode.Forbidden, (await Add(examiner, null, "Noch da?")).StatusCode);
        Assert.Empty(await Db(db => db.Comments.ToListAsync()));
    }

    [Fact]
    public async Task Comment_text_is_stored_encrypted()
    {
        await Add(examiner, null, Secret);

        var stored = await Db(db => db.Comments.SingleAsync());
        Assert.DoesNotContain(Secret, System.Text.Encoding.UTF8.GetString(stored.Text));
        Assert.Empty(await Db(db => db.AuditEntries.Where(e => e.Entity == nameof(Comment)).ToListAsync())); // not audited
    }

    [Fact]
    public async Task Comment_added_event_carries_no_text()
    {
        var messages = new List<string>();
        var added = new TaskCompletionSource();
        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(mainApi.Server.BaseAddress, "hubs/grading"), o =>
            {
                o.HttpMessageHandlerFactory = _ => mainApi.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling; // TestServer has no WebSockets
                o.AccessTokenProvider = () => Task.FromResult<string?>(admin);
            })
            .Build();
        hub.On<Guid, Guid, string>(nameof(IGradingClient.CommentAdded), (p, id, author) =>
        {
            messages.Add(JsonSerializer.Serialize(new object[] { p, id, author }));
            added.TrySetResult();
            return Task.CompletedTask;
        });
        await hub.StartAsync();
        await hub.InvokeAsync("JoinDocumentation", participant);

        var id = (await (await Add(examiner, "fiae-2026:1", Secret)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await added.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal([$"[\"{participant}\",\"{id}\",\"Dev Examiner\"]"], messages); // ID and author only
    }

    Task<HttpResponseMessage> Add(string token, string? section, string text) =>
        Send(HttpMethod.Post, Comments, token, new { section, text });

    async Task<JsonElement> List(string token)
    {
        var response = await Send(HttpMethod.Get, Comments, token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    string Comments => $"/api/participants/{participant}/comments";

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
